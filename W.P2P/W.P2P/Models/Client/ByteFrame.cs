using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace W.P2P.Models;

public class ByteFrame
{
        public List<byte> TargetId { get; set; }
        public List<byte> SourceId { get; set; }
        public byte Checksum { get; set; }
        public List<byte> Data { get; set; }
        public List<byte> Id { get; set; }
        public DataModels.FrameType Type { get; set; }

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
            frame.Add(0xAA);
            
            return frame;
        }
        
        public static ByteFrame Deserialize(List<byte> data)
        {
            if (data[0] != 0xAA)
                throw new BrokenFrame("Error: Frame is not valid!");
            
            if (data[^1] != 0xAA)
                throw new BrokenFrame("Error: Frame is not valid!");

            var frame = new ByteFrame();
            int pos = 1;
            
            frame.Type = (DataModels.FrameType)data[pos++];
            
            frame.Id = data.GetRange(pos, 36).ToList();
            pos += 36;

            frame.TargetId = data.GetRange(pos, 36).ToList();
            pos += 36;

            frame.SourceId = data.GetRange(pos, 36).ToList();
            pos += 36;

            byte dataLen = data[pos++];
        
            if (pos + dataLen > data.Count)
                throw new BrokenFrame($"Data got lost! Only {data.Count} bytes arrived of {dataLen}");
        
            frame.Data = data.GetRange(pos, dataLen).ToList();
            pos += dataLen;
            
            frame.Validate(frame, data[pos]);
            
            return frame;
        }

        public DataModels.StringFrame ToStringFrame()
        {
            return new DataModels.StringFrame
            {
                TargetId = Encoding.UTF8.GetString(TargetId.ToArray()),
                SourceId = Encoding.UTF8.GetString(SourceId.ToArray()),
                Data = Encoding.UTF8.GetString(Data.ToArray()),
                Id = Encoding.UTF8.GetString(Id.ToArray()),
                Type = Type
            };
        }

        public void Validate(ByteFrame byteFrame, byte oldChecksum)
        {
            if (TargetId.Count != 36) throw new BrokenFrame("Target Id is not valid!");
            if (SourceId.Count != 36) throw new BrokenFrame("Source Id is not valid!");
            if (Id.Count != 36) throw new BrokenFrame("Id is not valid!");
            
            byteFrame.Checksum = oldChecksum;

            byteFrame.CalculateChecksum();
            if (byteFrame.Checksum != oldChecksum)
                throw new BrokenFrame("Checksum-Failed!");
        }
}