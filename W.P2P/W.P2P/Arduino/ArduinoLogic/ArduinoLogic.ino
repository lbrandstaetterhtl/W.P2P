#include <SPI.h>
#include <RF24.h>

const int HEADER_LENGTH = 145;
const byte SYNC_BYTE = 0xAA;
const byte CONFIG_BYTE = 0xFF;
const int MAX_FRAME = 149 + 255;   // sync+header+len+maxData+crc+endSync = 404

uint64_t targetId;
uint64_t myId;
byte handshakeId[36];

RF24 radio(9, 10);   // CE, CSN

// Reassembly-Puffer fuer eingehende Funk-Frames
byte frameBuf[MAX_FRAME];
int frameLen = 0;

void setup() {
  Serial.begin(9600);
  Serial.setTimeout(2000);

  if (!radio.begin()) {
    Serial.println("Radio nicht erreichbar - SPI/Verdrahtung pruefen");
    while (true) {}
  }

  radio.setPALevel(RF24_PA_LOW);
  radio.setDataRate(RF24_1MBPS);
  radio.enableDynamicPayloads();   // Chunks sind variabel lang -> Pflicht
  // Pipes erst nach Config setzen (Adressen noch unbekannt)
}

void loop() {
  // Richtung 1: PC -> Funk
  if (Serial.available() >= 1) {
    handleSerial();
  }

  // Richtung 2: Funk -> PC
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

  sendOverRadio(result, totalLength);
}

void sendOverRadio(byte* frame, int length) {
  radio.stopListening();
  radio.openWritingPipe(targetId);

  int offset = 0;
  bool allOk = true;
  while (offset < length) {
    int chunkSize = min(32, length - offset);
    if (!radio.write(&frame[offset], chunkSize)) allOk = false;
    offset += chunkSize;
  }

  radio.startListening();   // sofort zurueck in Empfangsmodus
  Serial.println(allOk ? "Frame gesendet" : "Frame - Chunk ohne ack");
}

// ---- Funk -> PC ----

void receiveFromRadio() {
  while (radio.available()) {
    uint8_t len = radio.getDynamicPayloadSize();

    if (len == 0 || len > 32) {          // korruptes Paket
      byte dump[32];
      radio.read(dump, 32);              // aus FIFO entfernen
      continue;
    }

    if (frameLen + len > MAX_FRAME) {    // Ueberlauf -> Puffer desynct
      frameLen = 0;
    }

    radio.read(&frameBuf[frameLen], len);
    frameLen += len;

    flushCompleteFrames();
  }
}

void flushCompleteFrames() {
  while (true) {
    if (frameLen < 1) return;

    if (frameBuf[0] != SYNC_BYTE) {      // Anfang stimmt nicht -> verwerfen
      frameLen = 0;
      return;
    }

    const int dataLenOffset = 1 + HEADER_LENGTH;   // 146
    if (frameLen < dataLenOffset + 1) return;      // dataLength-Byte noch nicht da

    int total = 149 + frameBuf[dataLenOffset];     // Gesamtlaenge des Frames
    if (frameLen < total) return;                  // Frame noch nicht komplett

    if (frameBuf[total - 1] != SYNC_BYTE) {        // endSync falsch -> desynct
      frameLen = 0;
      return;
    }

    Serial.write(frameBuf, total);                 // ganzer Frame an PC

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

  if (Serial.readBytes(handshakeId, sizeof(handshakeId)) != sizeof(handshakeId)) return;

  // Adressen jetzt bekannt -> Pipes setzen und lauschen
  radio.openReadingPipe(1, myId);
  radio.openWritingPipe(targetId);
  radio.startListening();
}