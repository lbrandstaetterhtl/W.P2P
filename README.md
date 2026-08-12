# W.P2P

Ein C#-Lernprojekt zur Entwicklung eines eigenen binären Kommunikationsprotokolls, inspiriert vom Aufbau eines Ethernet-Frames. Das Protokoll ist darauf ausgelegt, über RF-Hardware (nRF24L01) übertragen zu werden – die Arduino-seitige C/C++-Implementierung existiert separat. Diese Codebasis ist der PC-seitige Client: eine Konsolenanwendung, die Frames baut, serialisiert, per ECDH einen Schlüsselaustausch (Handshake) durchführt, eine verbindungsbasierte Sitzung (Channel) zu genau einem Peer aufbaut und Kontakte verwaltet.

> **Status:** In aktiver Entwicklung. Der eigentliche RF-Transport ist noch **nicht** angebunden. `SendFrame()` serialisiert aktuell nur und deserialisiert sofort lokal (Loopback), und `Connect()` führt den kompletten Handshake auf **einer** Instanz gegen sich selbst aus. Es wird also noch keine echte Zwei-Parteien-Kommunikation getestet – das ist bewusste Simulation, bis die Hardware da ist. Siehe [Bekannte Baustellen](#bekannte-baustellen).

---

## Inhalt

- [Architektur](#architektur)
- [Protokoll / Frame-Format](#protokoll--frame-format)
- [Ablauf: Handshake](#ablauf-handshake)
- [Verbindungsmodell (Channel)](#verbindungsmodell-channel)
- [Konfiguration](#konfiguration)
- [Befehle](#befehle)
- [Build & Ausführen](#build--ausführen)
- [Bekannte Baustellen](#bekannte-baustellen)

---

## Architektur

Der Client ist in klar getrennte Verantwortlichkeiten aufgeteilt:

| Datei | Verantwortung |
|-------|---------------|
| `Program.cs` | Einstiegspunkt. Endlosschleife, die Konsoleneingaben liest, in Befehl + Argumente zerlegt und an den `CommandProcessor` verteilt. Hält das globale `Config`-Objekt. |
| `CommandProcessor.cs` | Übersetzt Benutzerbefehle (`send`, `connect`, `disconnect`, `connection`, `saveid`, `config`, …) in Aufrufe am `P2PClient` bzw. an `Config`. Argument-Validierung und Fehlerausgabe. |
| `P2PClient.cs` | Kernlogik des Protokolls: Frames bauen (`BuildByteFrame`), Handshake initiieren/beantworten, Verbindung auf-/abbauen (`Connect`/`Disconnect`), Nachrichten senden (aktuell Loopback). Hält die aktuelle `Connection` und die Liste offener Handshakes. |
| `Models.cs` | Datenmodelle: `ByteFrame` (inkl. Serialisierung/Deserialisierung + CRC), `StringFrame` (lesbare Sicht), `FrameType`-Enum, `Contact`, `Connection`. |
| `SecurityManager.cs` | ECDH-Schlüsselableitung (nistP256 + SHA256). |
| `Config.cs` | Persistenz: Laden/Speichern der `config.json` unter `%AppData%\W.P2P\`, Verwaltung der `IdMap` (Kontaktliste). |

### Abhängigkeiten der Klassen

```
Program ──> CommandProcessor ──> P2PClient ──> SecurityManager
   │              │                  │
   └──────────────┴──────────────────┴──> Config / Models
```

---

## Protokoll / Frame-Format

Ein Frame wird durch `ByteFrame.Serialize()` in folgende Byte-Sequenz übersetzt:

| Offset | Feld | Länge | Beschreibung |
|--------|------|-------|--------------|
| 0 | Start-Byte | 1 | Konstant `0xAA` (Frame-Delimiter) |
| 1 | Type | 1 | `FrameType` (siehe unten) |
| 2 | Id | 36 | ID-Feld (`Guid` als ASCII-String) |
| 38 | TargetId | 36 | Ziel-ID (ASCII) |
| 74 | SourceId | 36 | Quell-ID (ASCII) |
| 110 | DataLen | 1 | Länge des Datenfelds in Bytes (max. 255) |
| 111 | Data | 0–255 | Nutzdaten |
| 111+DataLen | Checksum | 1 | CRC-8 über TargetId + SourceId + Data + Id |

**Zum Id-Feld:** Es trägt je nach Frame-Typ zwei verschiedene Bedeutungen. Bei **Handshake-Frames** ist es eine frische, frameweite `Guid`. Bei **Data-Frames** wird stattdessen die `ConnectionId` der aktiven Verbindung eingesetzt (siehe [Verbindungsmodell](#verbindungsmodell-channel)) – das ist der Anfang eines Session-ID-Konzepts: alle Frames einer Sitzung teilen dieselbe ID. Eine separate **Sequenznummer pro Frame** existiert noch nicht (nötig für Dedup/Retransmit auf der störanfälligen RF-Strecke – siehe Baustellen).

### FrameType

| Wert | Name | Bedeutung |
|------|------|-----------|
| `0x01` | `Data` | Nutzdaten-Frame |
| `0x02` | `HandshakeInit` | Handshake-Anfrage (enthält Public Key) |
| `0x03` | `HandshakeReply` | Handshake-Antwort (enthält Public Key) |
| `0x04` | `OkReply` | Bestätigung |
| `0x05` | `ErrorReply` | Fehler (Data = UTF-8-Fehlermeldung) |

### Checksumme

CRC-8 mit Polynom `0x07`, Startwert `0x00`. Berechnet über `TargetId + SourceId + Data + Id` (nicht über Start-Byte, Type oder DataLen). Die Deserialisierung prüft die Checksumme und wirft bei Abweichung eine Exception.

---

## Ablauf: Handshake

Der Schlüsselaustausch basiert auf **ECDH (nistP256)** und wird innerhalb von `P2PClient.Connect()` ausgelöst. Vereinfacht:

1. **Initiator** (`Handshake`) erzeugt ein ECDH-Keypair, merkt es sich in `Handshakes[contact.Id]` und sendet seinen Public Key als `HandshakeInit`.
2. **Empfänger** (`GotHandshakeInitRequest`) erzeugt sein eigenes Keypair, leitet daraus + dem empfangenen Public Key den Shared Key ab (`SecurityManager.DeriveKey`), speichert ihn am Kontakt und sendet seinen Public Key als `HandshakeReply` zurück.
3. **Initiator** (`GotHandshakeReply`) leitet mit seinem gemerkten Keypair + dem empfangenen Public Key denselben Shared Key ab, speichert ihn, entfernt den offenen Handshake und antwortet mit `OkReply`.

Der abgeleitete Schlüssel landet in `Contact.Key` und wird in der `config.json` mitgespeichert.

> **Wichtig:** Der Schlüssel wird zwar korrekt abgeleitet und gespeichert, aber beim Senden **noch nicht angewendet** – Nachrichten gehen als Klartext raus (siehe Baustellen).

---

## Verbindungsmodell (Channel)

Statt beliebig an jeden Kontakt zu senden, arbeitet der Client mit **genau einer aktiven Verbindung** zu einem Peer. Das passt zur Realität eines geteilten, halbduplexen Funkmediums: echte gleichzeitige Multi-Peer-Gespräche gibt die Hardware ohnehin nicht her.

- `connect <name>` baut über den ECDH-Handshake eine Verbindung auf und legt ein `Connection`-Objekt an (`TargetId`, `SourceId`, `ConnectionId`).
- Solange eine Verbindung besteht, adressiert `send` ausschließlich diesen Peer. `SendMessage` prüft vor dem Senden, ob `targetId == Connection.TargetId`.
- `disconnect` verwirft die Verbindung (setzt `Connection` zurück).
- `connection` zeigt die aktuelle Verbindung an.

Die `ConnectionId` ist als Session-ID gedacht und wird bei Data-Frames ins Id-Feld geschrieben. **Aktuelle Grenze:** Sie wird nur lokal beim Verbinden erzeugt, nicht während des Handshakes zwischen beiden Seiten ausgetauscht – in einem echten Zwei-Parteien-Szenario hätten Initiator und Empfänger also unterschiedliche IDs. Ebenso fehlt die **Empfangsprüfung**: eingehende Frames werden noch nicht gegen die aktive Verbindung gefiltert (nur der Sende-Pfad hat einen Guard).

---

## Konfiguration

Beim Start lädt `Config.LoadConfig()`:

- **Existiert `%AppData%\W.P2P\config.json`:** wird geladen.
- **Existiert sie nicht:** wird ein Default gebaut (`BuildDefault`: neue Guid als eigene `Id`, `MachineName` als Name, Selbsteintrag in `IdMap`) und gespeichert.

Die eigene Identität (`Id`, `Name`) sowie die Kontaktliste (`IdMap` aus `Contact`-Objekten mit `Id`, `Name`, `Key`) werden als JSON serialisiert.

---

## Befehle

Eingaben in der Konsole, Format `befehl arg1 arg2 …`:

| Befehl | Syntax | Beschreibung |
|--------|--------|--------------|
| `saveid` | `saveid <id> <name>` | Speichert einen Kontakt. |
| `idmap` | `idmap` | Gibt die Kontaktliste aus. |
| `connect` | `connect <name>` | Baut über ECDH-Handshake eine Verbindung zum Kontakt auf. |
| `send` | `send <name> <text>` | Sendet ein `Data`-Frame an den verbundenen Kontakt. Erfordert aktive Verbindung + Key. |
| `disconnect` | `disconnect` | Beendet die aktuelle Verbindung. |
| `connection` | `connection` | Zeigt die aktuelle Verbindung an. |
| `config` | `config <option> …` | Kontaktverwaltung, siehe unten. |
| `exit` | `exit` | Speichert die Config und beendet. |

### `config`-Optionen

| Option | Syntax | Beschreibung |
|--------|--------|--------------|
| `renameid` | `config renameid <id> <newName>` | Kontakt umbenennen. |
| `viewid` | `config viewid <id>` | Einzelnen Kontakt anzeigen. |
| `delete` | `config delete <id>` | Kontakt löschen. |
| `editid` | `config editid <oldId> <newId>` | Kontakt-ID ändern. |
| `viewall` | `config viewall` | Gesamte Config ausgeben. |

---

## Build & Ausführen

Voraussetzung: **.NET** (die verwendeten Features – Primary Constructors, Collection Expressions – erfordern .NET 8 / C# 12 oder neuer).

```bash
dotnet build
dotnet run
```

Die App startet in eine interaktive Konsolen-Schleife (`>`-Prompt). Config wird automatisch angelegt.

---

## Bekannte Baustellen

Ehrliche Bestandsaufnahme – nach Schwere sortiert:

1. **`Serialize()` kompiliert so vermutlich nicht.** `frame.AddRange((byte)Type)` übergibt ein Einzel-Byte an `AddRange`, das aber ein `IEnumerable<byte>` erwartet. Gemeint ist `frame.Add((byte)Type)`. Falls dein lokaler Build durchläuft, weicht die hochgeladene Datei von deinem Arbeitsstand ab – dann bitte gegenchecken.

2. **Kein echter Transport – alles Loopback.** `Connect()` ruft `Handshake` → `GotHandshakeInitRequest` → `GotHandshakeReply` nacheinander auf **derselben** Instanz auf; die Maschine macht den Handshake mit sich selbst. Auch der `send`-Flow (`SendMessage` → `GotMessage` → `SendFrame`) simuliert nur den Roundtrip. Das ist die Fassade einer Verbindung, keine echte Zwei-Parteien-Kommunikation. Bewusst so, bis die nRF24-Hardware angebunden ist – aber: bis dahin ist die gesamte Fehlerbehandlung praktisch ungetestet, weil im selben-Thread-Loopback nichts verloren geht oder kollidiert.

3. **`Connect()` – NRE bei Handshake-Fehler.** Wenn `GotHandshakeInitRequest` in seinem `catch` `null` zurückgibt, läuft der nächste Aufruf `GotHandshakeReply(null)` in eine `NullReferenceException` – und selbst dessen `catch`-Zweig wirft erneut (`frame.ToStringFrame()` auf `null`). Da niemand weiter oben fängt, crasht das Programm.

4. **Key wird abgeleitet, aber nie benutzt.** `SendMessage` bekommt `key` als Parameter, verwendet ihn im Rumpf aber nicht – die Nutzdaten gehen als UTF-8-Klartext raus. Der ECDH-Schlüssel ist weiterhin totes Kapital. Genau der Channel wäre der Ort, ihn endlich zum Ver-/Entschlüsseln der Data-Frames einzusetzen.

5. **`ConnectionId` wird nicht zwischen den Parteien ausgehandelt.** Sie entsteht nur lokal in `Connect()`. In einem echten Zwei-Parteien-Aufbau hätten beide Seiten unterschiedliche IDs. Lösung: die ID schon im `HandshakeInit` mitschicken und im `HandshakeReply` zurückspiegeln.

6. **Keine Sequenznummer.** Das Id-Feld trägt bei Data-Frames die `ConnectionId` (SessionId-Ansatz ✓), aber es gibt kein Seq-Feld pro Frame. Ohne das sind weder Duplikat-Erkennung noch gezielte Neuübertragung möglich – beides braucht es zwingend, sobald die RF-Strecke (Kollisionen, Verlust, Duplikate) real wird.

7. **`GotMessage` ist ein Stub.** Baut nur ein `OkReply` und filtert eingehende Frames nicht gegen `Connection.TargetId`/`ConnectionId`. Die Channel-Zugehörigkeit wird beim Empfang nicht geprüft (nur `SendMessage` hat einen Sende-Guard).

8. **`Config.LoadConfig()` – fragil.** `Path.GetDirectoryName(ConfigFilePath)` kann theoretisch `null` liefern, wird aber ungeprüft an `CreateDirectory` gegeben. Zusätzlich ruft der Else-Zweig rekursiv `LoadConfig()` auf – funktioniert, ist aber unnötig verschachtelt.

9. **`BuildDefault()` im Load-Pfad verschenkt.** In `LoadConfig` wird ein lokales `config` gebaut und mit `BuildDefault` befüllt, dann bei existierender Datei sofort überschrieben.

10. **`config viewid`/`renameid`/`editid` – fehlende Längenprüfung.** `viewid` liest `parts[2]` ohne Prüfung → Crash bei `config viewid` ohne Id.

11. **Tippfehler in Ausgaben:** „No ceonnection established", „No valid command was give".

12. **`MyId` in `Program` redundant** – gesetzt, aber der Client nutzt durchgehend `config.Id`.

13. **Deutsch/Englisch in Exceptions gemischt** (`Ungültiges Frame!` vs. englische Meldungen) – kosmetisch, aber inkonsistent.

---

*Lernprojekt – Fokus liegt auf dem Protokolldesign, dem ECDH-Handshake und dem Verbindungsmodell, nicht auf Produktionsreife.*
