using System.Text;

namespace W.P2P;

public class Models
{
    public class ByteFrame
    {
        public List<byte> TargetId { get; set; }
        public List<byte> SourceId { get; set; }
        public byte Checksum { get; set; }
        public List<byte> Position { get; set; }
        public List<byte> Data { get; set; }
        public List<byte> Id { get; set; }
        
        public FrameType Type { get; set; }

        public void BuildFrame(string targetId, string sourceId, byte[] data, int position, string id, FrameType type)
        {
            Id = new List<byte>(Encoding.ASCII.GetBytes(id));
            
            var targetIdBytes = new List<byte>(Encoding.ASCII.GetBytes(targetId));
            var sourceIdBytes = new List<byte>(Encoding.ASCII.GetBytes(sourceId));
            var dataBytes = data.ToList();
            var positionBytes = new List<byte>(BitConverter.GetBytes(position));
            
            Type = type;
            TargetId = targetIdBytes;
            SourceId = sourceIdBytes;
            Position = positionBytes;
            Data = dataBytes;
            CalculateChecksum();
        }

        public void CalculateChecksum()
        {
            byte crc = 0x00;
            var allData = new List<byte>();
    
            allData.AddRange(TargetId);
            allData.AddRange(SourceId);
            allData.AddRange(Position);
            allData.AddRange(Data);
            allData.AddRange(Id);
    
            foreach (var b in allData)
            {
                crc ^= b;
                for (int i = 0; i < 8; i++)
                {
                    if ((crc & 0x80) != 0)
                        crc = (byte)((crc << 1) ^ 0x07);
                    else
                        crc = (byte)(crc << 1);
                }
            }
            Checksum = crc;
        }
        
        public List<byte> Serialize()
        {
            var frame = new List<byte> {0xAA};
        
            frame.AddRange((byte)Type);
            frame.AddRange(Id);
            frame.AddRange(TargetId);
            frame.AddRange(SourceId);
            frame.AddRange(Position);
            
            frame.Add((byte)Data.Count);
            frame.AddRange(Data);
            frame.Add(Checksum);
            
            return frame;
        }
        
        public static ByteFrame Deserialize(List<byte> data)
        {
            if (data[0] != 0xAA)
                throw new Exception("Ungültiges Frame!");

            var frame = new ByteFrame();
            int pos = 1;
            
            frame.Type = (FrameType)data[pos++];
            
            frame.Id = data.GetRange(pos, 36).ToList();
            pos += 36;

            frame.TargetId = data.GetRange(pos, 36).ToList();
            pos += 36;

            frame.SourceId = data.GetRange(pos, 36).ToList();
            pos += 36;

            frame.Position = data.GetRange(pos, 4).ToList();
            pos += 4;

            byte dataLen = data[pos++];
        
            if (pos + dataLen > data.Count)
                throw new Exception($"Nicht genug Daten! Erwartet {dataLen} bytes, aber nur {data.Count - pos} vorhanden");
        
            frame.Data = data.GetRange(pos, dataLen).ToList();
            pos += dataLen;
            

            byte oldChecksum = data[pos];
            frame.Checksum = oldChecksum;

            frame.CalculateChecksum();
            if (frame.Checksum != oldChecksum)
                throw new Exception("Checksum-Fehler!");

            return frame;
        }

        public StringFrame ToStringFrame()
        {
            return new StringFrame
            {
                TargetId = Encoding.UTF8.GetString(TargetId.ToArray()),
                SourceId = Encoding.UTF8.GetString(SourceId.ToArray()),
                Data = Encoding.UTF8.GetString(Data.ToArray()),
                Id = Encoding.UTF8.GetString(Id.ToArray()),
                Position = BitConverter.ToInt32(Position.ToArray(), 0),
                Type = Type
            };
        }
    }

    public class StringFrame
    {
        public string TargetId { get; set; }
        public string SourceId { get; set; }
        public string Data { get; set; }
        public string Id { get; set; }
        public int Position { get; set; }
        public FrameType Type { get; set; }
    }
    
    public enum FrameType : byte
    {
        Data = 0x01,
        HandshakeInit = 0x02,
        HandshakeReply = 0x03
    }

    public class Contact
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public byte[] Key  { get; set; }
    }
}