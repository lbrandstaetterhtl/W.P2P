# W.P2P

A C# learning project for building a custom binary communication protocol, inspired by the structure of an Ethernet frame. The protocol is intended to be transmitted over RF hardware (nRF24L01); the Arduino-side C/C++ implementation lives separately. This codebase is the PC-side client: an **Avalonia desktop application** that builds frames, serializes them, performs an ECDH key exchange (handshake), derives and applies an AES key, maintains a single-peer connection (channel), and manages contacts.

> **Status:** Under active development. The actual RF transport is **not** wired up yet. `SendFrame()` currently only logs the outgoing frame (there is a `// TODO: SEND LOGIC`) — it neither serializes to a wire nor delivers anything. On top of that, `Connect()` runs the entire handshake against **itself** on a single instance. So no real two-party communication is exercised yet — this is deliberate simulation until the hardware is here. See [Known issues](#known-issues).

---

## Contents

- [Architecture](#architecture)
- [Protocol / frame format](#protocol--frame-format)
- [Handshake flow](#handshake-flow)
- [Encryption](#encryption)
- [Connection model (channel)](#connection-model-channel)
- [Transmit & retransmit](#transmit--retransmit)
- [Configuration](#configuration)
- [UI & usage](#ui--usage)
- [Build & run](#build--run)
- [Known issues](#known-issues)

---

## Architecture

The project follows an Avalonia MVVM layout. Core protocol logic is UI-independent and lives under `Models/`.

| File | Responsibility |
|------|----------------|
| `Program.cs` | Entry point. Bootstraps the Avalonia application (`BuildAvaloniaApp`). |
| `App.axaml` / `App.axaml.cs` | Application-level styles and theme (FluentAvalonia). |
| `Views/MainWindow.axaml(.cs)` | The window: a menu (Connect / Disconnect) plus a terminal-output panel. The `ConnectClick` code-behind handler forwards the clicked contact to the view model. |
| `ViewModels/MainViewModel.cs` | Exposes `Contacts` and `TerminalOutputText` to the UI, holds the `P2PClient`, and drives `Connect` / `Disconnect`. |
| `Models/AppData.cs` | Static app-wide state: the `Config`, plus `ObservableCollection`s for `TerminalOutput`, `SentMessages`, `ReceivedMessages`. |
| `Models/Config.cs` | Persistence: loads/saves `config.json` under `%AppData%\W.P2P\`, manages the `IdMap` (contact list). |
| `Models/Client/P2PClient.cs` | Core protocol logic: build frames, run/answer the handshake, connect/disconnect, enqueue and (eventually) send frames, retransmit on error. Holds the current `Connection`, the pending-handshake table, the send queue, and the last-sent buffer. |
| `Models/Client/ByteFrame.cs` | The frame itself: serialize/deserialize + CRC-8. |
| `Models/Client/DataModels.cs` | `StringFrame` (readable view), `FrameType` enum, `Contact`, `Connection`. |
| `Models/SecurityManager.cs` | ECDH key derivation (nistP256 + SHA256) and AES-CBC encrypt/decrypt. |
| `Models/Exceptions.cs` | Project-specific exceptions: `ContactNotFound`, `BrokenFrame`. |

> Note: the old console front end (`CommandProcessor`, the `Program.cs` command loop) has been replaced by the Avalonia UI and is no longer part of the project.

---

## Protocol / frame format

`ByteFrame.Serialize()` produces the following byte sequence:

| Offset | Field | Length | Description |
|--------|-------|--------|-------------|
| 0 | Start byte | 1 | Constant `0xAA` (frame delimiter) |
| 1 | Type | 1 | `FrameType` (see below) |
| 2 | Id | 36 | ID field (`Guid` as ASCII string) |
| 38 | TargetId | 36 | Destination ID (ASCII) |
| 74 | SourceId | 36 | Source ID (ASCII) |
| 110 | DataLen | 1 | Length of the data field in bytes (max 255) |
| 111 | Data | 0–255 | Payload |
| 111+DataLen | Checksum | 1 | CRC-8 over TargetId + SourceId + Data + Id |

Total frame length is therefore `112 + DataLen` bytes.

**On the Id field:** it carries two different meanings depending on frame type. For **handshake frames** it is a fresh, per-frame `Guid`. For **data frames** the active connection's `ConnectionId` is used instead — the start of a session-ID concept: all frames of one session share the same ID. A separate **per-frame sequence number** does not exist yet (needed for dedup/retransmit ordering on the lossy RF link — see issues).

### FrameType

| Value | Name | Meaning |
|-------|------|---------|
| `0x01` | `Data` | Payload frame (encrypted) |
| `0x02` | `HandshakeInit` | Handshake request (carries public key) |
| `0x03` | `HandshakeReply` | Handshake response (carries public key) |
| `0x04` | `OkReply` | Acknowledgement |
| `0x05` | `ErrorReply` | Error (Data = the failed frame's id, used to trigger a resend) |
| `0x06` | `Disconnect` | Connection teardown request |

### Checksum

CRC-8 with polynomial `0x07`, initial value `0x00`. Computed over `TargetId + SourceId + Data + Id` (not over the start byte, Type, or DataLen). Deserialization recomputes and throws `BrokenFrame` on mismatch, on a wrong start byte, or on truncated data.

---

## Handshake flow

The key exchange uses **ECDH (nistP256)** and is triggered inside `P2PClient.Connect()`:

1. **Initiator** (`Handshake`) creates an ECDH key pair, stores it in `Handshakes[contact.Id]`, and sends its public key as `HandshakeInit`.
2. **Responder** (`GotHandshakeInitRequest`) creates its own key pair, derives the shared key from it plus the received public key (`SecurityManager.DeriveKey`), stores it on the contact, and replies with its own public key as `HandshakeReply`.
3. **Initiator** (`GotHandshakeReply`) derives the same shared key from its stored key pair plus the received public key, stores it, removes the pending handshake, and answers with `OkReply`.

The derived key is stored in `Contact.Key` and, on connect, copied into `Connection.SharedKey`.

---

## Encryption

Data frames are **encrypted with AES** using the ECDH-derived shared key (see `SecurityManager`):

- `Encrypt` uses AES-CBC with PKCS7 padding, generates a random IV, and prepends the IV to the ciphertext (`IV || ciphertext`).
- `Decrypt` reads the leading 16-byte IV back off, then decrypts the remainder.
- The SHA256 output of the ECDH exchange (32 bytes) serves as the AES-256 key.

`SendMessage` encrypts the payload before building a `Data` frame; `GotMessage` decrypts on receipt and shows the plaintext. (This closes the earlier gap where the derived key was computed but never applied.)

> **Caveat:** AES-CBC provides confidentiality but **no authentication** — the ciphertext is malleable and there is no MAC. For a learning project this is fine, but an authenticated mode (e.g. AES-GCM) or an encrypt-then-MAC construction would be the production choice. See issues.

---

## Connection model (channel)

Instead of sending to arbitrary contacts, the client works with **exactly one active connection** to a single peer. This matches the reality of a shared, half-duplex radio medium: genuine simultaneous multi-peer conversations aren't possible on the hardware anyway.

The `Connection` object holds `TargetId`, `TargetName`, `ConnectionId`, `SharedKey`, and `IsConnected`.

- Connecting (via the Connect menu) runs the ECDH handshake and populates `Connection`.
- While connected, `SendMessage` checks `Connection.IsConnected` before building a frame.
- Disconnecting sends a `Disconnect` frame and tears the connection down (`GotDisconnectRequest` resets `Connection`; `GotOkReply` confirms).

`ConnectionId` is intended as a session ID and is written into the Id field of data frames. **Current limitation:** it is created locally at connect time, not negotiated between the two sides during the handshake — so in a real two-party setup the initiator and responder would hold different IDs. There is also no **receive-side filtering yet**: incoming frames are not checked against the active connection's peer/id (only the send path is guarded).

---

## Transmit & retransmit

`P2PClient` maintains two structures for outgoing traffic:

- `_frameQueue` (a `Queue<ByteFrame>`) — frames are enqueued, then `SendFrame()` dequeues one.
- `_lastSentFrames` (a `List<ByteFrame>`) — every sent frame is retained here so it can be resent.

On an `ErrorReply`, `GotErrorReply` reads the failed frame's id (carried in the error reply's Data field), looks that frame up in `_lastSentFrames`, and resends it via `SendFrame(errorReply: true, id)`. This is the beginning of an ARQ-style retransmit.

> **But:** `SendFrame()` does not actually transmit anything yet — it only writes debug lines to `TerminalOutput` and carries a `// TODO: SEND LOGIC`. Nothing is serialized to a wire or delivered. This is the seam where the real serial/RF transport will plug in.

---

## Configuration

On startup, `Config.LoadConfig()`:

- **If `%AppData%\W.P2P\config.json` exists:** it is read and deserialized (wrapped in a try/catch that rebuilds a default on parse failure). The contact list is loaded by **clearing and re-filling** the existing `IdMap` (so the bound collection reference stays stable for the UI).
- **If it does not exist:** a default is built (`BuildDefault`: fresh `Guid` as own `Id`, `MachineName` as name, self-entry in `IdMap`) and saved.

Own identity (`Id`, `Name`) and the contact list (`IdMap` of `Contact` objects with `Id`, `Name`, `Key`) are serialized to JSON.

---

## UI & usage

The application is menu-driven (there is no console command loop):

- **Connect** — a menu whose items are the contacts from `Contacts`. Clicking a contact runs `ConnectClick`, which forwards the contact to `MainViewModel.Connect` and starts the handshake.
- **Disconnect** — bound to `DisconnectCommand`; tears down the active connection.
- **Terminal Output** — a panel bound to `TerminalOutputText` that shows the protocol log (handshake progress, sends, errors, debug lines).

`SentMessages` and `ReceivedMessages` collections exist in `AppData` but are not yet surfaced in the UI (the right-hand panel is currently empty — this is where a chat/send view will go).

---

## Build & run

Target framework **net10.0**, output type `WinExe`. Key dependencies (see `W.P2P.csproj`): Avalonia `12.1.1`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Fonts.Inter`, `FluentAvaloniaUI` `3.0.2`, `CommunityToolkit.Mvvm` `8.4.2`.

```bash
dotnet build
dotnet run
```

Or open `W.P2P.sln` in Rider/Visual Studio. `config.json` is created automatically on first run.

---

## Known issues

Honest inventory, roughly by severity:

1. **`ByteFrame.Serialize()` does not compile as written.** `frame.AddRange((byte)Type)` passes a single `byte` to `AddRange`, which expects an `IEnumerable<byte>`; there is no custom `AddRange` extension in the project to make this legal. It must be `frame.Add((byte)Type)`. If your local build runs, that working copy differs from this file on this line — reconcile them.

2. **No real transport — `SendFrame()` is a stub.** It only writes debug output and has a `// TODO: SEND LOGIC`; nothing is serialized to a wire or delivered. Until it's implemented, no frame actually leaves the process.

3. **`Connect()` runs the handshake against itself.** It calls `Handshake` → `GotHandshakeInitRequest` → `GotHandshakeReply` → `GotOkReply` in sequence **on the same instance** — the machine handshakes with itself. This is the facade of a connection, not real two-party communication. Deliberate until the nRF24 hardware is wired up, but note: all of the error handling is effectively untested this way, because nothing is lost, corrupted, or reordered in a same-thread self-call.

4. **`Connect()` — NRE on handshake failure.** If `GotHandshakeInitRequest` returns `null` (its `ContactNotFound` catch does), the next call `GotHandshakeReply(null)` dereferences `frame` and throws — and even that method's catch rethrows on `frame.ToStringFrame()`. With nothing catching higher up, the app crashes. Then `Connect` also reads `reply.Type` on a possibly-null reply.

5. **`ConnectionId` is not negotiated between the two parties.** It is created only in `Connect()`. In a real two-party setup both sides would hold different IDs. Fix: send it in `HandshakeInit` and mirror it back in `HandshakeReply`.

6. **No per-frame sequence number.** The Id field carries the `ConnectionId` for data frames (session-ID approach ✓), and `_lastSentFrames` + resend-by-id is a partial retransmit — but there is no `Seq` per frame, so duplicate detection and in-order reassembly are impossible. Both become mandatory once the RF link (collisions, loss, duplicates) is real.

7. **No receive-side connection filtering.** `GotMessage` decrypts and displays, but does not check the incoming frame's source/id against the active `Connection`. Only the send path is guarded.

8. **AES-CBC is unauthenticated.** Confidentiality only; the ciphertext is malleable and there is no MAC. Consider AES-GCM or encrypt-then-MAC.

9. **`Config.LoadConfig()` is fragile.** `Path.GetDirectoryName(ConfigFilePath)` may in theory return `null` but is passed unchecked to `CreateDirectory`; the else branch also calls `LoadConfig()` recursively; and the `BuildDefault()` at the top of the method is discarded when the file exists.

10. **`GotDisconnectRequest` logs against a reset connection.** It sets `Connection = new Connection()` and then reads `Connection.TargetName` / builds the reply with `Connection.TargetId` — both now empty. Cosmetic, but the teardown ordering is off.

11. **Leftover debug output.** `SendFrame()` writes several `DEBUG:` lines to the terminal on every send; `SecurityManager` has an unused `using Microsoft.VisualBasic;`.

---

*Learning project — the focus is protocol design, the ECDH handshake, applied AES, and the connection model, not production readiness.*
