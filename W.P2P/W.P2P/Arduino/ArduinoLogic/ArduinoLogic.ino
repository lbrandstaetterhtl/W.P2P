#include <RF24.h>
#include <RF24_config.h>
#include <nRF24L01.h>
#include <printf.h>

#include <SPI.h>
#include <RF24.h>

const int  HEADER_LENGTH = 145;
const byte SYNC_BYTE     = 0xAA;
const byte CONFIG_BYTE   = 0xFF;
const byte LOG_BYTE      = 0xEE;
const int  MAX_FRAME     = 149 + 255;   // 404

const uint64_t ADDR_PREFIX    = 0xE8E8F0F000LL;
const uint64_t BROADCAST_ADDR = ADDR_PREFIX | 0x00;

const byte FT_HANDSHAKE_INIT = 0x02;

// Offset des Type-Bytes im Header. ANPASSEN an dein echtes Frame-Layout!
const int TYPE_OFFSET_IN_HEADER = 36;

// Feste Handshake-Connection-ID (Null-GUID als ASCII), identisch auf allen Geraeten.
const byte HANDSHAKE_ID[36] = {
  '0','0','0','0','0','0','0','0','-',
  '0','0','0','0','-',
  '0','0','0','0','-',
  '0','0','0','0','-',
  '0','0','0','0','0','0','0','0','0','0','0','0'
};

// Offset der ConnectionId im Frame. ANPASSEN an dein echtes Layout!
const int CONNID_OFFSET_IN_HEADER = 0;

uint64_t targetId;
uint64_t myId;
bool configured = false;

RF24 radio(9, 10);   // CE, CSN

byte frameBuf[MAX_FRAME];
int  frameLen = 0;

// ---- Logging (laengenpraefixiert, kollidiert nicht mit Frames) ----

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

  if (!radio.begin()) {
    // NICHT still hängen bleiben - weiter loggen, damit man's sieht
    while (true) {
      logMsg("FEHLER: Radio nicht erreichbar - SPI/Verdrahtung pruefen");
      delay(1000);
    }
  }

  radio.setPALevel(RF24_PA_LOW);
  radio.setDataRate(RF24_1MBPS);
  radio.enableDynamicPayloads();
  radio.openReadingPipe(1, BROADCAST_ADDR);
  radio.setAutoAck(1, false);
  radio.startListening();

  logMsg("Setup fertig, lausche auf Broadcast");
}

void loop() {
  static unsigned long last = 0;
  if (millis() - last > 2000) {
    logMsg("heartbeat");
    last = millis();
  }

  if (Serial.available() >= 1) handleSerial();
  if (radio.available()) receiveFromRadio();
}

// ---- PC -> Funk ----

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

  byte header[HEADER_LENGTH];
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

  byte data[255];
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

  byte result[totalLength];
  int pos = 0;
  result[pos++] = openSync;
  memcpy(&result[pos], header, HEADER_LENGTH); pos += HEADER_LENGTH;
  result[pos++] = dataLength;
  if (dataLength > 0) { memcpy(&result[pos], data, dataLength); pos += dataLength; }
  result[pos++] = crc;
  result[pos] = endSync;

  byte frameType = result[1 + TYPE_OFFSET_IN_HEADER];
  logHex("FrameType gelesen: ", frameType);
  bool broadcast = (frameType == FT_HANDSHAKE_INIT);
  logMsg(broadcast ? "-> Broadcast-Versand" : "-> privater Versand");

  sendOverRadio(result, totalLength, broadcast);
}

void sendOverRadio(byte* frame, int length, bool broadcast) {
  radio.stopListening();
  radio.openWritingPipe(broadcast ? BROADCAST_ADDR : targetId);

  logInt("Sende Bytes gesamt: ", length);

  int offset = 0;
  bool allOk = true;
  int chunkNum = 0;
  char buf[48];

  while (offset < length) {
    int chunkSize = min(32, length - offset);
    bool ok = radio.write(&frame[offset], chunkSize, broadcast);
    if (!ok) allOk = false;
    snprintf(buf, sizeof(buf), "Chunk %d: %d Byte, ack=%d", chunkNum, chunkSize, ok ? 1 : 0);
    logMsg(buf);
    offset += chunkSize;
    chunkNum++;
  }

  radio.startListening();

  logMsg(broadcast ? "Broadcast fertig (ack bedeutungslos)"
                   : (allOk ? "Alle Chunks ack" : "Mind. 1 Chunk ohne ack"));
}

// ---- Funk -> PC ----

void receiveFromRadio() {
  uint8_t pipeNum;
  while (radio.available(&pipeNum)) {
    uint8_t len = radio.getDynamicPayloadSize();
    char buf[48];
    snprintf(buf, sizeof(buf), "Funk rein: Pipe %d, %d Byte", pipeNum, len);
    logMsg(buf);

    if (len == 0 || len > 32) {
      byte dump[32];
      radio.read(dump, 32);
      logMsg("-> ungueltige Laenge, verworfen");
      continue;
    }

    if (frameLen + len > MAX_FRAME) {
      logMsg("-> Puffer-Ueberlauf, reset");
      frameLen = 0;
    }

    radio.read(&frameBuf[frameLen], len);
    frameLen += len;
    logInt("-> frameLen jetzt: ", frameLen);

    flushCompleteFrames(pipeNum);
  }
}

void flushCompleteFrames(uint8_t pipeNum) {
  while (true) {
    if (frameLen < 1) return;

    if (frameBuf[0] != SYNC_BYTE) {
      logHex("Reassembly: Byte 0 kein Sync: ", frameBuf[0]);
      frameLen = 0;
      return;
    }

    const int dataLenOffset = 1 + HEADER_LENGTH;   // 146
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