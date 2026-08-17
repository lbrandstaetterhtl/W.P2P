int HEADER_LENGTH = 145;
byte SYNC_BYTE = 0xAA;
byte CONFIG_BYTE = 0xFF;
uint64_t targetId;
uint64_t myId;
byte handshakeId[36];

void setup() {
  Serial.begin(9600);
  Serial.setTimeout(2000);
}

void loop() {
  if (Serial.available() < 1) {
    return;
  }

  int totalLength = 0;

  byte openSync;
  if (Serial.readBytes(&openSync, 1) != 1) return;
  totalLength++;

  if (openSync == CONFIG_BYTE)
  {
    desirializeConfig();
    return;
  }

  if (openSync != SYNC_BYTE) return;

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

  // Frame zusammenbauen
  byte result[totalLength];
  int pos = 0;

  result[pos] = openSync;
  pos++;

  memcpy(&result[pos], header, HEADER_LENGTH);
  pos += HEADER_LENGTH;

  result[pos] = dataLength;
  pos++;

  if (dataLength > 0) {
    memcpy(&result[pos], data, dataLength);
    pos += dataLength;
  }

  result[pos] = crc;
  pos++;

  result[pos] = endSync;

  Serial.write(result, totalLength);
}

void desirializeConfig()
{
  targetId = 0;
  myId = 0;
  byte targetIdGot[5];
  if (Serial.readBytes(targetIdGot, sizeof(targetIdGot)) != 1) return;

  for (int i = 0; i < 5; i++) {
  targetId |= (uint64_t)targetIdGot[i] << (8 * i); 
  }

  byte myIdGot[5];
  if (Serial.readBytes(myIdGot, sizeof(myIdGot)) != 1) return;

  for (int i = 0; i < 5; i++) {
  myId |= (uint64_t)myIdGot[i] << (8 * i); 
  }

  if (Serial.readBytes(handshakeId, sizeof(handshakeId)) != 1) return;
}
