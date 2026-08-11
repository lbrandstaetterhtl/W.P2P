# W.P2P

Ein C#-Lernprojekt zur Entwicklung eines eigenen binären Kommunikationsprotokolls, inspiriert vom Aufbau eines Ethernet-Frames. Das Protokoll ist darauf ausgelegt, über RF-Hardware übertragen zu werden (Arduino-seitige C/C++-Implementierung existiert separat). Diese Codebasis ist der PC-seitige Client: eine Konsolenanwendung, die Frames baut, serialisiert, per ECDH einen Schlüsselaustausch (Handshake) durchführt und Kontakte verwaltet.

> **Status:** In aktiver Entwicklung. Der eigentliche RF-Transport ist noch nicht angebunden – `Send()` serialisiert aktuell nur und deserialisiert lokal zum Debuggen (Loopback). Siehe [Bekannte Baustellen](#bekannte-baustellen).

---

## Inhalt

- [Architektur](#architektur)
- [Protokoll / Frame-Format](#protokoll--frame-format)
- [Ablauf: Handshake](#ablauf-handshake)
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
| `CommandProcessor.cs` | Übersetzt Benutzerbefehle (`send`, `handshake`, `saveid`, `config`, …) in Aufrufe am `P2PClient` bzw. an `Config`. Argument-Validierung und Fehlerausgabe. |
| `P2PClient.cs` | Kernlogik des Protokolls: Frames bauen (`BuildByteFrame`), Handshake initiieren und beantworten, Frames senden (aktuell Loopback). Hält die Liste offener Handshakes. |
| `Models.cs` | Datenmodelle: `ByteFrame` (inkl. Serialisierung/Deserialisierung + CRC), `StringFrame` (lesbare Sicht), `FrameType`-Enum, `Contact`. |
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
| 2 | Id | 36 | Frame-ID (`Guid` als ASCII-String) |
| 38 | TargetId | 36 | Ziel-ID (ASCII) |
| 74 | SourceId | 36 | Quell-ID (ASCII) |
| 110 | DataLen | 1 | Länge des Datenfelds in Bytes (max. 255) |
| 111 | Data | 0–255 | Nutzdaten |
| 111+DataLen | Checksum | 1 | CRC-8 über TargetId + SourceId + Data + Id |

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

Der Schlüsselaustausch basiert auf **ECDH (nistP256)**. Vereinfacht:

1. **Initiator** erzeugt ein ECDH-Keypair, merkt es sich in `Handshakes[contact.Id]` und sendet seinen Public Key als `HandshakeInit`.
2. **Empfänger** (`GotHandshakeInitRequest`) erzeugt sein eigenes Keypair, leitet daraus + dem empfangenen Public Key den Shared Key ab (`SecurityManager.DeriveKey`), speichert ihn am Kontakt und sendet seinen Public Key als `HandshakeReply` zurück.
3. **Initiator** (`GotHandshakeReply`) leitet mit seinem gemerkten Keypair + dem empfangenen Public Key denselben Shared Key ab, speichert ihn und entfernt den offenen Handshake. Antwortet mit `OkReply`.

Der abgeleitete Schlüssel landet in `Contact.Key` und wird in der `config.json` mitgespeichert.

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
| `saveid` | `saveid <id> <name>` | Speichert einen Kontakt. `MyId` ist als Name reserviert. |
| `idmap` | `idmap` | Gibt die Kontaktliste aus. |
| `send` | `send <name> <text>` | Sendet ein `Data`-Frame an den Kontakt mit diesem Namen. Erfordert vorhandenen Key. |
| `handshake` | `handshake <name>` | Startet den ECDH-Handshake mit dem Kontakt. |
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

Die App startet in eine interaktive Konsolen-Schleife. Config wird automatisch angelegt.

---

## Bekannte Baustellen

Ehrliche Bestandsaufnahme – das hier gehört noch angefasst:

- **Kein echter RF-Transport.** `P2PClient.Send()` serialisiert und deserialisiert nur lokal (Loopback zum Debuggen). Die Anbindung an die RF-Hardware fehlt komplett – ohne sie kommunizieren zwei Instanzen nicht miteinander.
- **`Config.LoadConfig()` – potenzieller NRE.** `Directory.GetDirectoryName(ConfigFilePath)` kann theoretisch `null` liefern, wird aber ungeprüft an `CreateDirectory` gegeben. Außerdem ruft der Else-Zweig rekursiv `LoadConfig()` auf – funktioniert, ist aber fragil.
- **`Config.BuildDefault()` wird im Load-Pfad verschenkt.** In `LoadConfig` wird ein lokales `config` gebaut und mit `BuildDefault` befüllt, dann aber sofort überschrieben, wenn die Datei existiert. Die Logik ist verschachtelter als nötig.
- **`GotHandshakeReply` (Fehlerpfad) baut das Reply-Frame ins Leere.** Im `catch` wird `BuildByteFrame(...)` aufgerufen, das Ergebnis aber nicht dem zurückgegebenen `reply` zugewiesen – es wird ein leeres `ByteFrame` zurückgegeben. Vergleiche den erfolgreichen Pfad.
- **`CommandProcessor.HandleHandshakeCommand` läuft nach zu wenig Argumenten weiter.** Bei `parts.Length < 2` wird die Fehlermeldung ausgegeben, aber **nicht** returned → danach folgt eine `IndexOutOfRangeException` auf `parts[1]`.
- **`config renameid`/`editid` fehlt teils die Längenprüfung.** `renameid` prüft auf `>= 4`, greift aber schon vorher nicht immer sauber; `viewid` liest `parts[2]` ohne Längenprüfung.
- **`Contact.Key` bei `send`.** `send` erwartet einen Key, der erst nach erfolgreichem Handshake existiert. Ohne vorherigen `handshake` fliegt eine Exception – korrektes Verhalten, aber die Fehlermeldung könnte klarer sein.
- **Tippfehler in Ausgaben:** z. B. „Not no valid options“, „No valid command was give“.
- **`MyId` in `Program`** wird gesetzt, aber der Client nutzt durchgehend `config.Id`. Feld ist quasi redundant.
- **Deutsch/Englisch gemischt** in Exceptions (`Ungültiges Frame!` vs. englische Meldungen) – kosmetisch, aber inkonsistent.

---

*Lernprojekt – Fokus liegt auf dem Protokolldesign und dem ECDH-Handshake, nicht auf Produktionsreife.*
