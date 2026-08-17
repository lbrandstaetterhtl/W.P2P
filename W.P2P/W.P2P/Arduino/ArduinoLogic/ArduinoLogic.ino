int HEADER_LENGTH = 145;
byte SYNC_BYTE = 0xAA;

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