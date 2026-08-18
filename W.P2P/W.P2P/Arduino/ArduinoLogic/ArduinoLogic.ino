#include <SPI.h>
#include <RF24.h>

const int  HEADER_LENGTH = 145;
const byte SYNC_BYTE     = 0xAA;
const byte CONFIG_BYTE   = 0xFF;
const int  MAX_FRAME     = 149 + 255;   // 404

const uint64_t ADDR_PREFIX    = 0xE8E8F0F000LL;
const uint64_t BROADCAST_ADDR = ADDR_PREFIX | 0x00;

const byte FT_HANDSHAKE_INIT = 0x02;

// Offset des Type-Bytes im Header. ANPASSEN an dein echtes Frame-Layout!
const int TYPE_OFFSET_IN_HEADER = 0;

// Feste Handshake-Connection-ID (Null-GUID als ASCII), identisch auf allen Geraeten.
// Muss C#-seitig _handshakeId == "00000000-0000-0000-0000-000000000000" entsprechen.
const byte HANDSHAKE_ID[36] = {
  '0','0','0','0','0','0','0','0','-',
  '0','0','0','0','-',
  '0','0','0','0','-',
  '0','0','0','0','-',
  '0','0','0','0','0','0','0','0','0','0','0','0'
};

// Offset der ConnectionId im Frame. ANPASSEN an dein echtes Layout!
// ConnectionId liegt bei Byte (1 + CONNID_OFFSET_IN_HEADER), gemessen ab Frame-Start.
const int CONNID_OFFSET_IN_HEADER = 0;   // <-- pruefen, siehe Hinweis unten

uint64_t targetId;
uint64_t myId;
bool configured = false;

RF24 radio(9, 10);   // CE, CSN

byte frameBuf[MAX_FRAME];
int  frameLen = 0;

void setup() {
  Serial.begin(9600);
  Serial.setTimeout(2000);

  if (!radio.begin()) {
    Serial.println("Radio nicht erreichbar - SPI/Verdrahtung pruefen");
    while (true) {}
  }

  radio.setPALevel(RF24_PA_LOW);
  radio.setDataRate(RF24_1MBPS);
  radio.enableDynamicPayloads();

  radio.openReadingPipe(1, BROADCAST_ADDR);
  radio.setAutoAck(1, false);

  radio.startListening();
}

void loop() {
  if (Serial.available() >= 1) {
    handleSerial();
  }
  if (radio.available()) {
    receiveFromRadio();
  }
}

// ---- PC -> Funk ----

void handleSerial() {
  byte openSync;
  if (Serial.readBytes(&openSync, 1) != 1) return;

  if (openSync == CONFIG_BYTE) {
    desirializeConfig();
    return;
  }
  if (openSync != SYNC_BYTE) return;

  int totalLength = 1;

  byte header[HEADER_LENGTH];
  if (Serial.readBytes(header, HEADER_LENGTH) != HEADER_LENGTH) return;
  totalLength += HEADER_LENGTH;

  byte dataLength;
  if (Serial.readBytes(&dataLength, 1) != 1) return;
  totalLength++;

  byte data[255];
  if (dataLength > 0) {
    if (Serial.readBytes(data, dataLength) != dataLength) return;
  }
  totalLength += dataLength;

  byte crc;
  if (Serial.readBytes(&crc, 1) != 1) return;
  totalLength++;

  byte endSync;
  if (Serial.readBytes(&endSync, 1) != 1) return;
  totalLength++;
  if (endSync != SYNC_BYTE) return;

  byte result[totalLength];
  int pos = 0;
  result[pos++] = openSync;
  memcpy(&result[pos], header, HEADER_LENGTH); pos += HEADER_LENGTH;
  result[pos++] = dataLength;
  if (dataLength > 0) { memcpy(&result[pos], data, dataLength); pos += dataLength; }
  result[pos++] = crc;
  result[pos] = endSync;

  byte frameType = result[1 + TYPE_OFFSET_IN_HEADER];
  bool broadcast = (frameType == FT_HANDSHAKE_INIT);

  sendOverRadio(result, totalLength, broadcast);
}

void sendOverRadio(byte* frame, int length, bool broadcast) {
  radio.stopListening();
  radio.openWritingPipe(broadcast ? BROADCAST_ADDR : targetId);

  int offset = 0;
  bool allOk = true;
  while (offset < length) {
    int chunkSize = min(32, length - offset);
    bool ok = radio.write(&frame[offset], chunkSize, broadcast);
    if (!ok) allOk = false;
    offset += chunkSize;
  }

  radio.startListening();

  if (broadcast) {
    Serial.println("Handshake per Broadcast gesendet (kein ack moeglich)");
  } else {
    Serial.println(allOk ? "Frame gesendet" : "Frame - Chunk ohne ack");
  }
}

// ---- Funk -> PC ----

void receiveFromRadio() {
  uint8_t pipeNum;
  while (radio.available(&pipeNum)) {
    uint8_t len = radio.getDynamicPayloadSize();

    if (len == 0 || len > 32) {
      byte dump[32];
      radio.read(dump, 32);
      continue;
    }

    if (frameLen + len > MAX_FRAME) {
      frameLen = 0;
    }

    radio.read(&frameBuf[frameLen], len);
    frameLen += len;

    flushCompleteFrames(pipeNum);
  }
}

void flushCompleteFrames(uint8_t pipeNum) {
  while (true) {
    if (frameLen < 1) return;

    if (frameBuf[0] != SYNC_BYTE) {
      frameLen = 0;
      return;
    }

    const int dataLenOffset = 1 + HEADER_LENGTH;   // 146
    if (frameLen < dataLenOffset + 1) return;

    int total = 149 + frameBuf[dataLenOffset];
    if (frameLen < total) return;

    if (frameBuf[total - 1] != SYNC_BYTE) {
      frameLen = 0;
      return;
    }

    // Broadcast-Frames (Pipe 1) muessen die feste Handshake-ID tragen, sonst verwerfen.
    bool pass = true;
    if (pipeNum == 1) {
      if (memcmp(&frameBuf[1 + CONNID_OFFSET_IN_HEADER], HANDSHAKE_ID, sizeof(HANDSHAKE_ID)) != 0) {
        pass = false;
      }
    }

    if (pass) {
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
  if (Serial.readBytes(targetIdGot, sizeof(targetIdGot)) != sizeof(targetIdGot)) return;
  for (int i = 0; i < 5; i++) targetId |= (uint64_t)targetIdGot[i] << (8 * i);

  byte myIdGot[5];
  if (Serial.readBytes(myIdGot, sizeof(myIdGot)) != sizeof(myIdGot)) return;
  for (int i = 0; i < 5; i++) myId |= (uint64_t)myIdGot[i] << (8 * i);

  radio.openReadingPipe(2, myId);
  radio.setAutoAck(2, true);

  configured = true;
  radio.startListening();

  Serial.println("Config erhalten, private Adresse aktiv");
}