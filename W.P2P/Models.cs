using System.Text;

namespace W.P2P;

public class Models
{
    public class ByteFrame
    {
        public List<byte> TargetId { get; set; }
        public List<byte> SourceId { get; set; }
        public byte Checksum { get; set; }
        public List<byte> Data { get; set; }
        public List<byte> Id { get; set; }
        public FrameType Type { get; set; }

        public void CalculateChecksum()
        {
            byte crc = 0x00;
            var allData = new List<byte>();
    
            allData.AddRange(TargetId);
            allData.AddRange(SourceId);
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

            byte dataLen = data[pos++];
        
            if (pos + dataLen > data.Count)
                throw new Exception($"Data got lost! Only {data.Count} bytes arrived of {dataLen}");
        
            frame.Data = data.GetRange(pos, dataLen).ToList();
            pos += dataLen;
            

            byte oldChecksum = data[pos];
            frame.Checksum = oldChecksum;

            frame.CalculateChecksum();
            if (frame.Checksum != oldChecksum)
                throw new Exception("Checksum-Failed!");

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
        public FrameType Type { get; set; }
    }
    
    public enum FrameType : byte
    {
        Data = 0x01,
        HandshakeInit = 0x02,
        HandshakeReply = 0x03,
        OkReply = 0x04,
        ErrorReply = 0x05,
    }

    public class Contact
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public byte[] Key  { get; set; }
    }
    
    public class Connection
    {
        public string TargetId { get; set; }
        public string SourceId { get; set; }
        public string ConnectionId { get; set; }
    }
}