using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;

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
        public byte[] HardwareId { get; set; }
        
        public void SetHardwareId()
        {
            List<byte> hardwareId = new();
            for (int i = 0; i < 5; i++)
            {
                hardwareId.Add((byte)Id[i]);
            }
            
            HardwareId = hardwareId.ToArray();
        }
    }
    
    public class Connection
    {
        public byte[] SharedKey { get; set; }
        public string TargetName { get; set; }
        public string TargetId { get; set; }
        public string ConnectionId { get; set; }
        public bool IsConnected { get; set; }
    }

    public class ArduinoConfig
    {
        public byte[] TargetId { get; set; }
        public byte[] MyId { get; set; }
    }
    
    public static class SafeLog
    {
        private const int MaxLines = 500;

        public static void Add(string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                AppendInternal(message);
            }
            else
            {
                Dispatcher.UIThread.Invoke(() => AppendInternal(message));
            }
        }

        private static void AppendInternal(string message)
        {
            AppData.TerminalOutput.Add(message);
            if (AppData.TerminalOutput.Count > MaxLines)
                AppData.TerminalOutput.RemoveAt(0);
        }
    }
}