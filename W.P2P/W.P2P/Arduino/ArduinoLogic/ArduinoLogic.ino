#include <SPI.h>
#include <RF24.h>

const int  HEADER_LENGTH = 145;
const byte SYNC_BYTE     = 0xAA;
const byte CONFIG_BYTE   = 0xFF;
const byte LOG_BYTE      = 0xEE;
const int  MAX_FRAME     = 260;
const int  MAX_DATA_LENGTH = 128;

const uint64_t ADDR_PREFIX    = 0xC5E8F0F0C5LL;
const uint64_t BROADCAST_ADDR = ADDR_PREFIX;

const byte FT_HANDSHAKE_INIT = 0x02;
const byte FT_HANDSHAKE_REPLY = 0x03;

const int TYPE_OFFSET_IN_HEADER   = 36;
const int CONNID_OFFSET_IN_HEADER = 0;

// PROGMEM: liegt im Flash, nicht im RAM (spart 36 Byte)
const byte HANDSHAKE_ID[36] PROGMEM = {
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

byte frameBuf[MAX_FRAME];
int  frameLen = 0;
byte header[HEADER_LENGTH];
byte data[MAX_DATA_LENGTH];

// ---- Logging (Strings aus Flash lesen) ----

void logMsgF(const __FlashStringHelper* msg) {
  const char* p = (const char*)msg;
  int len = strlen_P(p);
  if (len > 255) len = 255;
  Serial.write(LOG_BYTE);
  Serial.write((byte)len);
  for (int i = 0; i < len; i++) {
    Serial.write(pgm_read_byte(p + i));
  }
}

// Fuer dynamisch zusammengebaute Strings (bleiben im RAM, aber nur kurz)
void logMsgBuf(const char* buf) {
  int len = strlen(buf);
  if (len > 255) len = 255;
  Serial.write(LOG_BYTE);
  Serial.write((byte)len);
  Serial.write((const byte*)buf, len);
}

void logHex(const __FlashStringHelper* label, byte value) {
  char buf[48];
  strncpy_P(buf, (const char*)label, sizeof(buf) - 8);
  buf[sizeof(buf) - 8] = '\0';
  int pos = strlen(buf);
  snprintf(buf + pos, sizeof(buf) - pos, "0x%02X", value);
  logMsgBuf(buf);
}

void logInt(const __FlashStringHelper* label, long value) {
  char buf[48];
  strncpy_P(buf, (const char*)label, sizeof(buf) - 12);
  buf[sizeof(buf) - 12] = '\0';
  int pos = strlen(buf);
  snprintf(buf + pos, sizeof(buf) - pos, "%ld", value);
  logMsgBuf(buf);
}

// ---- Setup / Loop ----

void setup() {
  Serial.begin(9600);
  Serial.setTimeout(2000);

  logMsgF(F("SETUP RUNNING"));

  if (!radio.begin()) {
    while (true) {
      logMsgF(F("FEHLER: Radio nicht erreichbar - SPI/Verdrahtung pruefen"));
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

  logMsgF(F("Setup fertig, lausche auf Broadcast"));
  setupRan = true;
}

void loop() {
  static unsigned long last = 0;
  if (millis() - last > 2000) {
    char buf[48];
    snprintf_P(buf, sizeof(buf), PSTR("heartbeat (cfg=%d, setup=%d)"),
               configured, setupRan);
    logMsgBuf(buf);
    last = millis();
  }

  if (Serial.available() >= 1) handleSerial();
  receiveFromRadio();
}

// ---- PC -> Funk ----

void sendFromParts(byte sync, const byte* hdr, byte dataLen, const byte* dat,
                   byte crc, byte endSync, int totalLength, bool broadcast) {
  radio.stopListening();
  radio.openWritingPipe(broadcast ? BROADCAST_ADDR : targetId);

  logInt(F("Sende Bytes gesamt: "), totalLength);

  byte chunk[32];
  int chunkPos = 0;
  int chunkNum = 0;
  int failCount = 0;

  #define WRITE_BYTE(b) do {                              \
      chunk[chunkPos++] = (b);                            \
      if (chunkPos == 32) {                               \
        if (!radio.write(chunk, 32, broadcast)) failCount++; \
        chunkPos = 0;                                     \
        chunkNum++;                                       \
      }                                                   \
    } while (0)

  WRITE_BYTE(sync);
  for (int i = 0; i < HEADER_LENGTH; i++) WRITE_BYTE(hdr[i]);
  WRITE_BYTE(dataLen);
  for (int i = 0; i < dataLen; i++) WRITE_BYTE(dat[i]);
  WRITE_BYTE(crc);
  WRITE_BYTE(endSync);

  if (chunkPos > 0) {
    if (!radio.write(chunk, chunkPos, broadcast)) failCount++;
    chunkNum++;
  }

  #undef WRITE_BYTE

  radio.startListening();

  char buf[48];
  if (broadcast) {
    snprintf_P(buf, sizeof(buf), PSTR("%d Chunks broadcast"), chunkNum);
  } else {
    snprintf_P(buf, sizeof(buf), PSTR("%d Chunks, %d ohne ack"), chunkNum, failCount);
  }
  logMsgBuf(buf);
}

void handleSerial() {
  byte openSync;
  if (Serial.readBytes(&openSync, 1) != 1) return;

  logHex(F("Serial rein: "), openSync);

  if (openSync == CONFIG_BYTE) {
    logMsgF(F("-> Config erkannt"));
    desirializeConfig();
    return;
  }
  if (openSync != SYNC_BYTE) {
    logHex(F("-> kein Sync: "), openSync);
    return;
  }

  int totalLength = 1;

  int gotHeader = Serial.readBytes(header, HEADER_LENGTH);
  if (gotHeader != HEADER_LENGTH) {
    logInt(F("Header unvollstaendig: "), gotHeader);
    return;
  }
  totalLength += HEADER_LENGTH;

  byte dataLength;
  if (Serial.readBytes(&dataLength, 1) != 1) {
    logMsgF(F("dataLength fehlt"));
    return;
  }
  totalLength++;
  logHex(F("dataLength: "), dataLength);

  if (dataLength > MAX_DATA_LENGTH) {
    logInt(F("Data zu gross: "), dataLength);
    // Bytes trotzdem aus dem Puffer lesen, sonst desynct
    for (int i = 0; i < dataLength; i++) {
      byte tmp;
      Serial.readBytes(&tmp, 1);
    }
    return;
  }

  if (dataLength > 0) {
    if (Serial.readBytes(data, dataLength) != dataLength) {
      logMsgF(F("data unvollstaendig"));
      return;
    }
  }
  totalLength += dataLength;

  byte crc;
  if (Serial.readBytes(&crc, 1) != 1) { logMsgF(F("crc fehlt")); return; }
  totalLength++;

  byte endSync;
  if (Serial.readBytes(&endSync, 1) != 1) { logMsgF(F("endSync fehlt")); return; }
  totalLength++;
  if (endSync != SYNC_BYTE) {
    logHex(F("endSync falsch: "), endSync);
    return;
  }

  byte frameType = header[TYPE_OFFSET_IN_HEADER];
logHex(F("FrameType: "), frameType);
bool broadcast = (frameType == FT_HANDSHAKE_INIT || frameType == FT_HANDSHAKE_REPLY);
logMsgF(broadcast ? F("-> Broadcast-Versand") : F("-> privater Versand"));

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

  char buf[48];
  snprintf_P(buf, sizeof(buf), PSTR("Funk rein: Pipe %d, %d B"), pipeNum, len);
  logMsgBuf(buf);

  if (frameLen + len > MAX_FRAME) {
    logMsgF(F("-> Puffer voll, reset"));
    frameLen = 0;
  }

  radio.read(&frameBuf[frameLen], len);
  frameLen += len;
  logInt(F("-> frameLen: "), frameLen);

  flushCompleteFrames(pipeNum);
}

void flushCompleteFrames(uint8_t pipeNum) {
  while (true) {
    if (frameLen < 1) return;

    if (frameBuf[0] != SYNC_BYTE) {
      logHex(F("Reass: kein Sync: "), frameBuf[0]);
      frameLen = 0;
      return;
    }

    const int dataLenOffset = 1 + HEADER_LENGTH;
    if (frameLen < dataLenOffset + 1) return;

    int total = 149 + frameBuf[dataLenOffset];
    if (frameLen < total) return;

    if (frameBuf[total - 1] != SYNC_BYTE) {
      logHex(F("Reass: endSync: "), frameBuf[total - 1]);
      frameLen = 0;
      return;
    }

    bool pass = true;
    if (pipeNum == 1) {
      if (memcmp_P(&frameBuf[1 + CONNID_OFFSET_IN_HEADER], HANDSHAKE_ID, 36) != 0) {
        pass = false;
        logMsgF(F("Broadcast: ConnId falsch"));
      }
    }

    if (pass) {
      logInt(F("Frame komplett: "), total);
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
    logMsgF(F("Config: targetId fehlt"));
    return;
  }
  for (int i = 0; i < 5; i++) targetId |= (uint64_t)targetIdGot[i] << (8 * i);

  byte myIdGot[5];
  if (Serial.readBytes(myIdGot, sizeof(myIdGot)) != sizeof(myIdGot)) {
    logMsgF(F("Config: myId fehlt"));
    return;
  }
  for (int i = 0; i < 5; i++) myId |= (uint64_t)myIdGot[i] << (8 * i);

  radio.openReadingPipe(2, myId);
  radio.setAutoAck(2, true);

  configured = true;
  radio.startListening();

  logMsgF(F("Config OK"));
}