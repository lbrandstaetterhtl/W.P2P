#include <SPI.h>
#include <RF24.h>

const int  HEADER_LENGTH = 145;
const byte SYNC_BYTE     = 0xAA;
const byte CONFIG_BYTE   = 0xFF;
const byte LOG_BYTE      = 0xEE;
const int  MAX_FRAME     = 260;
const int  MAX_DATA_LENGTH = 128;

// Chunk-Header: 2 Byte (chunkNum + totalChunks), dann 30 Byte Nutzdaten
const int  CHUNK_HEADER_SIZE = 2;
const int  CHUNK_PAYLOAD     = 30;
const int  MAX_CHUNKS        = (MAX_FRAME + CHUNK_PAYLOAD - 1) / CHUNK_PAYLOAD;   // ~14

const uint64_t ADDR_PREFIX    = 0xC5E8F0F0C5LL;
const uint64_t BROADCAST_ADDR = ADDR_PREFIX;

const byte FT_HANDSHAKE_INIT  = 0x02;
const byte FT_HANDSHAKE_REPLY = 0x03;

const int TYPE_OFFSET_IN_HEADER   = 36;
const int CONNID_OFFSET_IN_HEADER = 0;

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
int  expectedTotalChunks = 0;
byte receivedMask[MAX_CHUNKS];   // 1 = empfangen, 0 = fehlt
int  lastChunkLen = 0;           // Groesse des letzten Chunks (kann < 30 sein)

byte header[HEADER_LENGTH];
byte data[MAX_DATA_LENGTH];

// ---- Logging ----

void logMsgF(const __FlashStringHelper* msg) {
  const char* p = (const char*)msg;
  int len = strlen_P(p);
  if (len > 255) len = 255;
  Serial.write(LOG_BYTE);
  Serial.write((byte)len);
  for (int i = 0; i < len; i++) Serial.write(pgm_read_byte(p + i));
}

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

// ---- Reassembly-Status zuruecksetzen ----

void resetReassembly() {
  expectedTotalChunks = 0;
  lastChunkLen = 0;
  memset(receivedMask, 0, sizeof(receivedMask));
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

  resetReassembly();
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

  // Wie viele Chunks brauchen wir?
  int totalChunks = (totalLength + CHUNK_PAYLOAD - 1) / CHUNK_PAYLOAD;

  logInt(F("Sende Bytes: "), totalLength);
  logInt(F("Chunks total: "), totalChunks);

  byte chunk[32];   // 2 Byte Header + 30 Byte Payload
  int chunkNum = 0;
  int failCount = 0;

  // Wir muessen die Bytes in den Frame in einer virtuellen Sequenz behandeln.
  // Statt einen 260-Byte-Buffer zu bauen, iterieren wir Byte fuer Byte
  // und fuellen jeden Chunk-Payload einzeln.

  int frameIdx = 0;   // wo im virtuellen Frame sind wir

  while (chunkNum < totalChunks) {
    chunk[0] = chunkNum;
    chunk[1] = totalChunks;

    int payloadBytes = 0;
    while (payloadBytes < CHUNK_PAYLOAD && frameIdx < totalLength) {
      byte b;
      // Bestimme Byte an frameIdx-Position im virtuellen Frame
      if (frameIdx == 0) b = sync;
      else if (frameIdx <= HEADER_LENGTH) b = hdr[frameIdx - 1];
      else if (frameIdx == 1 + HEADER_LENGTH) b = dataLen;
      else if (frameIdx <= 1 + HEADER_LENGTH + dataLen) b = dat[frameIdx - HEADER_LENGTH - 2];
      else if (frameIdx == 1 + HEADER_LENGTH + dataLen + 1) b = crc;
      else b = endSync;

      chunk[CHUNK_HEADER_SIZE + payloadBytes] = b;
      payloadBytes++;
      frameIdx++;
    }

    int chunkSize = CHUNK_HEADER_SIZE + payloadBytes;
    if (!radio.write(chunk, chunkSize, broadcast)) failCount++;
    chunkNum++;

    // kleine Pause zwischen Chunks - hilft dem Empfaenger, alle einzusammeln
    delayMicroseconds(500);
  }

  radio.startListening();

  char buf[48];
  if (broadcast) {
    snprintf_P(buf, sizeof(buf), PSTR("%d Chunks broadcast"), totalChunks);
  } else {
    snprintf_P(buf, sizeof(buf), PSTR("%d Chunks, %d ohne ack"), totalChunks, failCount);
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
  logMsgF(broadcast ? F("-> Broadcast") : F("-> privat"));

  sendFromParts(openSync, header, dataLength, data, crc, endSync, totalLength, broadcast);
}

// ---- Funk -> PC ----

void receiveFromRadio() {
  uint8_t pipeNum;
  if (!radio.available(&pipeNum)) return;

  uint8_t len = radio.getDynamicPayloadSize();

  if (len < CHUNK_HEADER_SIZE || len > 32) {
    byte dump[32];
    radio.read(dump, 32);
    return;   // Rauschen still verwerfen
  }

  byte chunk[32];
  radio.read(chunk, len);

  byte chunkNum = chunk[0];
  byte totalChunks = chunk[1];
  int payloadLen = len - CHUNK_HEADER_SIZE;

  char buf[64];
  snprintf_P(buf, sizeof(buf), PSTR("Chunk %d/%d, %d B"), chunkNum, totalChunks, payloadLen);
  logMsgBuf(buf);

  // Sanity checks
  if (totalChunks == 0 || totalChunks > MAX_CHUNKS) {
    logMsgF(F("Chunk ignoriert: totalChunks unplausibel"));
    return;
  }
  if (chunkNum >= totalChunks) {
    logMsgF(F("Chunk ignoriert: chunkNum >= total"));
    return;
  }

  // Wenn wir gerade einen anderen Frame sammeln, resetten
  if (expectedTotalChunks != 0 && expectedTotalChunks != totalChunks) {
    logMsgF(F("Neuer Frame - Reassembly-Reset"));
    resetReassembly();
  }
  expectedTotalChunks = totalChunks;

  // Chunk an richtige Position im Buffer schreiben
  int offset = chunkNum * CHUNK_PAYLOAD;
  if (offset + payloadLen > MAX_FRAME) {
    logMsgF(F("Chunk wuerde Puffer sprengen"));
    resetReassembly();
    return;
  }

  memcpy(&frameBuf[offset], &chunk[CHUNK_HEADER_SIZE], payloadLen);
  receivedMask[chunkNum] = 1;

  // Letzter Chunk merkt sich seine Groesse (fuer Frame-Ende)
  if (chunkNum == totalChunks - 1) {
    lastChunkLen = payloadLen;
  }

  // Alle Chunks da?
  bool complete = true;
  for (int i = 0; i < expectedTotalChunks; i++) {
    if (!receivedMask[i]) { complete = false; break; }
  }

  if (!complete) return;

  // Kompletter Frame - Laenge berechnen
  int totalLength = (expectedTotalChunks - 1) * CHUNK_PAYLOAD + lastChunkLen;

  // Sanity: Frame muss mit SYNC anfangen und aufhoeren
  if (frameBuf[0] != SYNC_BYTE) {
    logHex(F("Frame: Byte 0 kein Sync: "), frameBuf[0]);
    resetReassembly();
    return;
  }
  if (frameBuf[totalLength - 1] != SYNC_BYTE) {
    logHex(F("Frame: endSync falsch: "), frameBuf[totalLength - 1]);
    resetReassembly();
    return;
  }

  // Bei Broadcast: HandshakeId pruefen
  bool pass = true;
  if (pipeNum == 1) {
    if (memcmp_P(&frameBuf[1 + CONNID_OFFSET_IN_HEADER], HANDSHAKE_ID, 36) != 0) {
      pass = false;
      logMsgF(F("Broadcast: ConnId falsch"));
    }
  }

  if (pass) {
    logInt(F("Frame komplett: "), totalLength);
    Serial.write(frameBuf, totalLength);
  }

  resetReassembly();
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

  byte configuredByte;
  if (Serial.readBytes(&configuredByte, 1) != 1) {
    logMsgF(F("Config: configuredByte fehlt"));
    return;
  }

  configured = (configuredByte == 0x001);

  if (configured) {
    radio.openReadingPipe(2, myId);
    radio.setAutoAck(2, true);
    logMsgF(F("Private connected"));
  }
  else
  {
    radio.openReadingPipe(1, BROADCAST_ADDR);
    radio.setAutoAck(1, false);
    logMsgF(F("Lobby connected"));
  }

  radio.startListening();

  logMsgF(F("Config OK"));
}