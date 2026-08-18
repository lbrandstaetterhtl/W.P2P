#include <SPI.h>
#include <RF24.h>

const int  HEADER_LENGTH = 145;
const byte SYNC_BYTE     = 0xAA;
const byte CONFIG_BYTE   = 0xFF;
const byte LOG_BYTE      = 0xEE;
const int  MAX_FRAME     = 260;

const uint64_t ADDR_PREFIX    = 0xC5E8F0F0C5LL;
const uint64_t BROADCAST_ADDR = ADDR_PREFIX;

const byte FT_HANDSHAKE_INIT = 0x02;

const int TYPE_OFFSET_IN_HEADER   = 36;
const int CONNID_OFFSET_IN_HEADER = 0;

const byte HANDSHAKE_ID[36] = {
  '0','0','0','0','0','0','0','0','-',
  '0','0','0','0','-',
  '0','0','0','0','-',
  '0','0','0','0','-',
  '0','0','0','0','0','0','0','0','0','0','0','0'
};

uint64_t targetId;
uint64_t myId;
bool configured = false;
bool setupRan = false;

RF24 radio(9, 10);

// Globale Buffer - Stack bleibt frei
byte frameBuf[MAX_FRAME];      // Reassembly Funk -> PC
int  frameLen = 0;
byte header[HEADER_LENGTH];    // PC -> Funk: Header-Teil
byte data[255];                // PC -> Funk: Data-Teil
// sendBuf entfernt - wir chunken direkt aus den Einzelteilen

// ---- Logging ----

void logMsg(const char* msg) {
  int len = strlen(msg);
  if (len > 255) len = 255;
  Serial.write(LOG_BYTE);
  Serial.write((byte)len);
  Serial.write((const byte*)msg, len);
}

void logHex(const char* label, byte value) {
  char buf[64];
  snprintf(buf, sizeof(buf), "%s0x%02X", label, value);
  logMsg(buf);
}

void logInt(const char* label, long value) {
  char buf[64];
  snprintf(buf, sizeof(buf), "%s%ld", label, value);
  logMsg(buf);
}

// ---- Setup / Loop ----

void setup() {
  Serial.begin(9600);
  Serial.setTimeout(2000);

  logMsg("SETUP RUNNING");

  if (!radio.begin()) {
    while (true) {
      logMsg("FEHLER: Radio nicht erreichbar - SPI/Verdrahtung pruefen");
      delay(1000);
    }
  }

  radio.setPALevel(RF24_PA_LOW);
  radio.setDataRate(RF24_1MBPS);
  radio.enableDynamicPayloads();

  radio.closeReadingPipe(0);
  radio.openReadingPipe(1, BROADCAST_ADDR);
  radio.setAutoAck(1, false);
  radio.startListening();

  logMsg("Setup fertig, lausche auf Broadcast");
  setupRan = true;
}

void loop() {
  static unsigned long last = 0;
  if (millis() - last > 2000) {
    char buf[64];
    snprintf(buf, sizeof(buf), "heartbeat (configured=%d, setupRan=%d)",
             configured, setupRan);
    logMsg(buf);
    last = millis();
  }

  if (Serial.available() >= 1) handleSerial();
  receiveFromRadio();
}

// ---- PC -> Funk ----

// Baut einen "virtuellen" Frame aus den Einzelteilen (sync, header, dataLen,
// data, crc, endSync) und schickt ihn direkt in 32-Byte-Chunks - ohne den
// ganzen Frame nochmal in einen Buffer zu kopieren. Spart 260 Byte RAM.
void sendFromParts(byte sync, const byte* hdr, byte dataLen, const byte* dat,
                   byte crc, byte endSync, int totalLength, bool broadcast) {
  radio.stopListening();
  radio.openWritingPipe(broadcast ? BROADCAST_ADDR : targetId);

  logInt("Sende Bytes gesamt: ", totalLength);

  byte chunk[32];
  int chunkPos = 0;
  int chunkNum = 0;
  bool allOk = true;

  // Kleine Helfer-Lambda-Ersatz (C++ auf AVR mag keine Lambdas gut) -
  // wir schreiben Bytes in chunk[] und flushen bei 32.
  #define WRITE_BYTE(b) do {                                     \
      chunk[chunkPos++] = (b);                                   \
      if (chunkPos == 32) {                                      \
        bool ok = radio.write(chunk, 32, broadcast);             \
        if (!ok) allOk = false;                                  \
        char buf[64];                                            \
        snprintf(buf, sizeof(buf), "Chunk %d: 32 Byte, ack=%d",  \
                 chunkNum, ok ? 1 : 0);                          \
        logMsg(buf);                                             \
        chunkPos = 0;                                            \
        chunkNum++;                                              \
      }                                                          \
    } while (0)

  WRITE_BYTE(sync);
  for (int i = 0; i < HEADER_LENGTH; i++) WRITE_BYTE(hdr[i]);
  WRITE_BYTE(dataLen);
  for (int i = 0; i < dataLen; i++) WRITE_BYTE(dat[i]);
  WRITE_BYTE(crc);
  WRITE_BYTE(endSync);

  // Rest-Chunk (weniger als 32 Byte) rausschicken
  if (chunkPos > 0) {
    bool ok = radio.write(chunk, chunkPos, broadcast);
    if (!ok) allOk = false;
    char buf[64];
    snprintf(buf, sizeof(buf), "Chunk %d: %d Byte, ack=%d",
             chunkNum, chunkPos, ok ? 1 : 0);
    logMsg(buf);
  }

  #undef WRITE_BYTE

  radio.startListening();

  logMsg(broadcast ? "Broadcast fertig (ack bedeutungslos)"
                   : (allOk ? "Alle Chunks ack" : "Mind. 1 Chunk ohne ack"));
}

void handleSerial() {
  byte openSync;
  if (Serial.readBytes(&openSync, 1) != 1) return;

  logHex("Serial rein, erstes Byte: ", openSync);

  if (openSync == CONFIG_BYTE) {
    logMsg("-> Config erkannt");
    desirializeConfig();
    return;
  }
  if (openSync != SYNC_BYTE) {
    logHex("-> kein Sync, verworfen: ", openSync);
    return;
  }

  int totalLength = 1;

  int gotHeader = Serial.readBytes(header, HEADER_LENGTH);
  if (gotHeader != HEADER_LENGTH) {
    logInt("Header unvollstaendig, nur Bytes: ", gotHeader);
    return;
  }
  totalLength += HEADER_LENGTH;

  byte dataLength;
  if (Serial.readBytes(&dataLength, 1) != 1) {
    logMsg("dataLength fehlt");
    return;
  }
  totalLength++;
  logHex("dataLength: ", dataLength);

  if (dataLength > 0) {
    if (Serial.readBytes(data, dataLength) != dataLength) {
      logMsg("data unvollstaendig");
      return;
    }
  }
  totalLength += dataLength;

  byte crc;
  if (Serial.readBytes(&crc, 1) != 1) { logMsg("crc fehlt"); return; }
  totalLength++;

  byte endSync;
  if (Serial.readBytes(&endSync, 1) != 1) { logMsg("endSync fehlt"); return; }
  totalLength++;
  if (endSync != SYNC_BYTE) {
    logHex("endSync falsch: ", endSync);
    return;
  }

  // FrameType direkt aus dem Header lesen (Sync ist Offset 0, Header beginnt bei 1)
  // In den Header-Bytes ist der Type bei Index TYPE_OFFSET_IN_HEADER = 36
  byte frameType = header[TYPE_OFFSET_IN_HEADER];
  logHex("FrameType gelesen: ", frameType);
  bool broadcast = (frameType == FT_HANDSHAKE_INIT);
  logMsg(broadcast ? "-> Broadcast-Versand" : "-> privater Versand");

  sendFromParts(openSync, header, dataLength, data, crc, endSync, totalLength, broadcast);
}

// ---- Funk -> PC ----

void receiveFromRadio() {
  uint8_t pipeNum;
  if (!radio.available(&pipeNum)) return;

  uint8_t len = radio.getDynamicPayloadSize();

  if (len == 0 || len > 32) {
    byte dump[32];
    radio.read(dump, 32);
    return;
  }

  char buf[64];
  snprintf(buf, sizeof(buf), "Funk rein: Pipe %d, %d Byte", pipeNum, len);
  logMsg(buf);

  if (frameLen + len > MAX_FRAME) {
    logMsg("-> Puffer-Ueberlauf, reset");
    frameLen = 0;
  }

  radio.read(&frameBuf[frameLen], len);
  frameLen += len;
  logInt("-> frameLen jetzt: ", frameLen);

  flushCompleteFrames(pipeNum);
}

void flushCompleteFrames(uint8_t pipeNum) {
  while (true) {
    if (frameLen < 1) return;

    if (frameBuf[0] != SYNC_BYTE) {
      logHex("Reassembly: Byte 0 kein Sync: ", frameBuf[0]);
      frameLen = 0;
      return;
    }

    const int dataLenOffset = 1 + HEADER_LENGTH;
    if (frameLen < dataLenOffset + 1) return;

    int total = 149 + frameBuf[dataLenOffset];
    if (frameLen < total) return;

    if (frameBuf[total - 1] != SYNC_BYTE) {
      logHex("Reassembly: endSync falsch: ", frameBuf[total - 1]);
      frameLen = 0;
      return;
    }

    bool pass = true;
    if (pipeNum == 1) {
      if (memcmp(&frameBuf[1 + CONNID_OFFSET_IN_HEADER], HANDSHAKE_ID, sizeof(HANDSHAKE_ID)) != 0) {
        pass = false;
        logMsg("Broadcast: ConnectionId != HandshakeId, verworfen");
      }
    }

    if (pass) {
      logInt("Frame komplett -> an PC, Bytes: ", total);
      Serial.write(frameBuf, total);
    }

    int leftover = frameLen - total;
    if (leftover > 0) memmove(frameBuf, &frameBuf[total], leftover);
    frameLen = leftover;
  }
}

// ---- Config ----

void desirializeConfig() {
  targetId = 0;
  myId = 0;

  byte targetIdGot[5];
  if (Serial.readBytes(targetIdGot, sizeof(targetIdGot)) != sizeof(targetIdGot)) {
    logMsg("Config: targetId unvollstaendig");
    return;
  }
  for (int i = 0; i < 5; i++) targetId |= (uint64_t)targetIdGot[i] << (8 * i);

  byte myIdGot[5];
  if (Serial.readBytes(myIdGot, sizeof(myIdGot)) != sizeof(myIdGot)) {
    logMsg("Config: myId unvollstaendig");
    return;
  }
  for (int i = 0; i < 5; i++) myId |= (uint64_t)myIdGot[i] << (8 * i);

  radio.openReadingPipe(2, myId);
  radio.setAutoAck(2, true);

  configured = true;
  radio.startListening();

  logMsg("Config OK, private Adresse aktiv");
}