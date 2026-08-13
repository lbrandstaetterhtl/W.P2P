namespace W.P2P.Models;

public class DataModels
{
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
        Disconnect = 0x06,
    }

    public class Contact
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public byte[] Key  { get; set; }
    }
    
    public class Connection
    {
        public byte[] SharedKey { get; set; }
        public string TargetName { get; set; }
        public string TargetId { get; set; }
        public string ConnectionId { get; set; }
        public bool IsConnected { get; set; }
    }
}