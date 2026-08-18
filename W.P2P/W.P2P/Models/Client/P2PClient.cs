using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static W.P2P.Models.DataModels;

namespace W.P2P.Models;

public class P2PClient
{
    //pending handshakes
    private static readonly Dictionary<string, ECDiffieHellman> Handshakes = new();
    
    public Connection Connection = new();

    private readonly List<ByteFrame> _lastSentFrames = new();

    private readonly Queue<ByteFrame> _frameQueue = new();
    private readonly SerialTransport _serialTransport = new("COM4", 9600);
    private readonly string _handshakeId = "00000000-0000-0000-0000-000000000000";
    public ArduinoConfig ArduinoConfig = new();
    
    
    public P2PClient()
    {
        
    }
    
    public ByteFrame Handshake(Contact contact)
    {
        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        Handshakes.Add(contact.Id, ecdh);

        byte[] myPublicKey = ecdh.ExportSubjectPublicKeyInfo();

        var frame = BuildByteFrame(targetId: contact.Id, sourceId: AppData.Config.Id, data: myPublicKey,
            id: Guid.NewGuid().ToString(), type: FrameType.HandshakeInit, connectionId: _handshakeId);
        
        AppData.TerminalOutput.Add($"Sending Handshake [id: {frame.Id}] request to {contact.Name}|{contact.Id}...\n");
        _frameQueue.Enqueue(frame);
        SendFrame();
        
        return frame;
    }

    public ByteFrame GotHandshakeReply(ByteFrame frame)
    {
        try
        {
            //DEBUG LINE
            //throw new BrokenFrame("DEBUG");

            byte[] theirKey = frame.Data.ToArray();
            var stringFrame = frame.ToStringFrame();

            if (!Handshakes.TryGetValue(stringFrame.SourceId, out var ecdh))
                throw new HandshakeNotFound($"No handshake found for {stringFrame.SourceId}.");

            var contact = AppData.Config.IdMap.FirstOrDefault(x => x.Id == stringFrame.SourceId) ??
                          throw new ContactNotFound($"Contact with ID {stringFrame.SourceId} not found.");

            contact.Key = SecurityManager.DeriveKey(ecdh, theirKey);

            Handshakes.Remove(stringFrame.SourceId);
            ecdh.Dispose();

            AppData.TerminalOutput.Add($"Handshake completed with {contact.Name}|{stringFrame.SourceId}.\n");

            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: AppData.Config.Id, data: new byte[0],
                id: Guid.NewGuid().ToString(), type: FrameType.OkReply, connectionId: _handshakeId);

            AppData.TerminalOutput.Add(
                $"Sending Ok reply [id: {reply.Id}] to {contact.Name}|{stringFrame.SourceId}...\n");

            _frameQueue.Enqueue(reply);
            SendFrame();

            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"ContactNotFound Exception at GotHandshakeReply: {c.Message}\n");
            return null;
        }
        catch (BrokenFrame b)
        {
            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: AppData.Config.Id,
                data: frame.Id.ToArray(), id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply, connectionId: _handshakeId);

            AppData.TerminalOutput.Add($"BrokenFrame Exception at GotHandshakeReply: {b.Message}\n");

            _frameQueue.Enqueue(reply);
            SendFrame();

            return reply;
        }
        catch (HandshakeNotFound h)
        {
            AppData.TerminalOutput.Add($"HandshakeNotFound Exception at GotHandshakeReply: {h.Message}\n");
            return null;
        }
        catch (Exception e)
        {
            AppData.TerminalOutput.Add($"Unexpected Exception at GotHandshakeReply: \n {e.Message}\n");
            return null;
        }
    }

    public void GotErrorReply(ByteFrame frame)
    {
        try
        {
            var stringFrame = frame.ToStringFrame();

            var contact = AppData.Config.IdMap.FirstOrDefault(c => c.Id == stringFrame.SourceId) ??
                          throw new ContactNotFound($"No contact found for {stringFrame.SourceId}.");

            AppData.TerminalOutput.Add(
                $"Error reply received from {contact.Name}|{stringFrame.SourceId}: {stringFrame.Data}\n");
            AppData.TerminalOutput.Add($"Trying to send the frame [id: {stringFrame.Id}] again...\n");
            
            SendFrame(true, stringFrame.Data);
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"ContactNotFound Exception at GotErrorReply: {c.Message}\n");
        }
        catch (Exception e)
        {
            AppData.TerminalOutput.Add($"Unexpected Exception at GotErrorReply: \n {e.Message}\n");
        }
    }
    
    //DEBUG sourceId impersonates target not own machine
    public ByteFrame GotHandshakeInitRequest(ByteFrame frame)
    {
        try
        {
            byte[] theirPublicKey = frame.Data.ToArray();
            var stringFrame = frame.ToStringFrame();

            var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

            var contact = AppData.Config.IdMap.FirstOrDefault(x => x.Id == stringFrame.SourceId) ??
                          throw new ContactNotFound($"No contact found for {stringFrame.SourceId}.");
            contact.Key = SecurityManager.DeriveKey(ecdh, theirPublicKey);

            byte[] myPublicKey = ecdh.ExportSubjectPublicKeyInfo();

            //DEBUG
            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: AppData.Config.Id, data: myPublicKey, id: Guid.NewGuid().ToString(), type: FrameType.HandshakeReply, connectionId: _handshakeId);

            AppData.TerminalOutput.Add($"Handshake completed with {contact.Name}|{stringFrame.SourceId}.\n");

            AppData.TerminalOutput.Add(
                $"Sending Handshake reply [id: {reply.Id}] to {contact.Name}|{stringFrame.SourceId}...\n");

            ecdh.Dispose();

            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"ContactNotFound Exception at GotHandshakeInitRequest: \n {c.Message}\n");
            return null;
        }
        catch (BrokenFrame b)
        {
            AppData.TerminalOutput.Add($"BrokenFrame Exception at GotHandshakeInitRequest: \n {b.Message}\n");

            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: AppData.Config.Id,
                data: frame.Id.ToArray(), id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply, connectionId: _handshakeId);
            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
        catch (Exception e)
        {
            AppData.TerminalOutput.Add($"Unexpected Exception at GotHandshakeInitRequest: \n {e.Message}\n");
            return null;
        }
    }

    public ByteFrame GotMessage(ByteFrame frame, out StringFrame stringFrame)
    {
        try
        {
            //DEBUG LINE
            //throw new Exception("DEBUG");

            stringFrame = frame.ToStringFrame();
            var decrypted = SecurityManager.Decrypt(frame.Data.ToArray(), Connection.SharedKey);
            var message = Encoding.UTF8.GetString(decrypted);

            AppData.ReceivedMessages.Add($"\"{Connection.TargetName}\" - {message}");

            var reply = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id,
                data: new byte[0], id: Connection.ConnectionId, type: FrameType.OkReply, connectionId: Connection.ConnectionId);

            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"ContactNotFound Exception: {c.Message}\n");
            stringFrame = null;
            return null;
        }
        catch (BrokenFrame b)
        {
            AppData.TerminalOutput.Add($"BrokenFrame Exception: {b.Message}\n");
            var reply = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id,
                data: frame.Id.ToArray(), id: Connection.ConnectionId, type: FrameType.ErrorReply, connectionId: Connection.ConnectionId);
            stringFrame = null;
            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
        catch (Exception e)
        {
            AppData.TerminalOutput.Add($"Unexpected Exception: {e.Message}\n");
            stringFrame = null;
            return null;
        }
    }

    //TODO:REAL SEND LOGIC VIA USB SERIAL PORT TO ARDUINO
    public void SendFrame(bool errorReply = false, string id = "")
    {
        var frame = new ByteFrame();
        if (!errorReply)
        {
            frame = _frameQueue.Dequeue();
            _lastSentFrames.Add(frame);
        }
        else
        {
            frame = _lastSentFrames.FirstOrDefault(x => x.Id.SequenceEqual(Encoding.ASCII.GetBytes(id))) ?? throw new Exception($"No frame found with id {id}");
        }
        
        var stringFrame = frame.ToStringFrame();
        AppData.TerminalOutput.Add($"Sending frame [id: {frame.Id}] to {stringFrame.TargetId}...\n");
        
        //DEBUG
        var serialized = frame.Serialize();
        AppData.TerminalOutput.Add($"DEBUG: Serialized frame: {BitConverter.ToString(serialized.ToArray())}\n");
        
        _serialTransport.SendFrame(serialized.ToArray());
        
        var deserialized = ByteFrame.Deserialize(serialized);
        AppData.TerminalOutput.Add($"DEBUG: Deserialized frame: {BitConverter.ToString(deserialized.Serialize().ToArray())}\n");
        
        
        AppData.TerminalOutput.Add($"DEBUG: Frame id: {stringFrame.Id}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame type: {stringFrame.Type}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame targetId: {stringFrame.TargetId}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame sourceId: {stringFrame.SourceId}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame data: {stringFrame.Data}\n");
    }
    
    public void SendMessage(byte[] data)
    {
        if (!Connection.IsConnected)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            AppData.TerminalOutput.Add("Error: No connection established. Please connect to a contact first.\n");
            Console.ResetColor();
            return;
        }
        
        var encrypted = SecurityManager.Encrypt(data, Connection.SharedKey);
        var frame = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id, data: encrypted, id: Connection.ConnectionId, type: FrameType.Data, connectionId: Connection.ConnectionId);
        AppData.TerminalOutput.Add($"Message sent to {Connection.TargetName}|{Connection.TargetId}.");
        _frameQueue.Enqueue(frame);
        SendFrame();
    }
    
    public ByteFrame BuildByteFrame(string targetId, string sourceId, byte[] data, string id, FrameType type, string connectionId)
    {
        var frame = new ByteFrame();
        frame.Id = new List<byte>(Encoding.ASCII.GetBytes(id));
            
        var targetIdBytes = new List<byte>(Encoding.ASCII.GetBytes(targetId));
        var sourceIdBytes = new List<byte>(Encoding.ASCII.GetBytes(sourceId));
        var dataBytes = data.ToList();
            
        frame.ConnectionId = new List<byte>(Encoding.ASCII.GetBytes(connectionId));
        frame.Type = type;
        frame.TargetId = targetIdBytes;
        frame.SourceId = sourceIdBytes;
        frame.Data = dataBytes;
        frame.CalculateChecksum();
        return frame;
    }

    //DEBUG SIMULATES
    public bool Connect(Contact contact)
    {
        if (Connection.IsConnected)
        {
            AppData.TerminalOutput.Add($"Already connected to {Connection.TargetName}|{Connection.TargetId}. Disconnect first.\n");
            return true;
        }
        
        AppData.TerminalOutput.Add($"Connecting to Arduino...");
        _serialTransport.ArduinoConfig = new ArduinoConfig();
        _serialTransport.ArduinoConfig.TargetId = contact.HardwareId;
        _serialTransport.ArduinoConfig.MyId = AppData.Config.HardwareId;
        ArduinoConfig = _serialTransport.ArduinoConfig;
        _serialTransport.Connect();
        _serialTransport.OnFrameReceived += GotFrame;
        _serialTransport.StartReading();
        
        Handshake(contact);
        
        Connection = new Connection();
        Connection.TargetId = contact.Id;
        Connection.ConnectionId = Guid.NewGuid().ToString();
        Connection.IsConnected = true;
        Connection.TargetName = contact.Name;
        Connection.SharedKey = contact.Key;
        return true;
    }

    //DEBUG SIMULATES
    public bool Disconnect()
    {
        try
        {
            AppData.TerminalOutput.Add($"Disconnecting with {Connection.TargetName}|{Connection.TargetId}...\n");

            var frame = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id, data: new byte[0],
                id: Connection.ConnectionId, type: FrameType.Disconnect, connectionId: Connection.ConnectionId);
            _frameQueue.Enqueue(frame);
            SendFrame();

            return true;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"ContactNotFound Exception at Disconnect: {c.Message}\n");
            return false;
        }
        catch (BrokenFrame b)
        {
            AppData.TerminalOutput.Add($"BrokenFrame Exception at Disconnect: {b.Message}\n");
            return false;
        }
        catch (Exception e)
        {
            AppData.TerminalOutput.Add($"Unexpected Exception at Disconnect: {e.Message}\n");
            return false;
        }
    }

    public void GotOkReply(ByteFrame frame)
    {
        var stringFrame = frame.ToStringFrame();
        
        AppData.TerminalOutput.Add($"Got Ok Reply from {Connection.TargetName}|{Connection.TargetId} for frame {stringFrame.Id}\n");
    }

    //DEBUG impersonates target not own machine
    public ByteFrame GotDisconnectRequest(ByteFrame frame)
    {
        try
        {
            //DEBUG LINE
            //throw new BrokenFrame("DEBUG");
            
            var stringFrame = frame.ToStringFrame();

            AppData.TerminalOutput.Add(
                $"Got Disconnect Request from {Connection.TargetName}|{Connection.TargetId} for frame {stringFrame.Id}\n");

            AppData.TerminalOutput.Add($"Disconnecting with {Connection.TargetName}|{Connection.TargetId}...\n");

            Connection.TargetId = "";
            Connection.ConnectionId = "";
            Connection.IsConnected = false;
            Connection.TargetName = "";
            Connection.SharedKey = [];

            AppData.TerminalOutput.Add($"Sending Ok reply to {Connection.TargetName}|{Connection.TargetId}...\n");
            
            var reply = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id, data: new byte[0], id: Guid.NewGuid().ToString(), type: FrameType.OkReply, connectionId: Connection.ConnectionId);

            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}\n");
            return null;
        }
        catch (BrokenFrame b)
        {
            AppData.TerminalOutput.Add($"BrokenFrame Exception: {b.Message}\n");
            
            var reply = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id,
                data: frame.Id.ToArray(), id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply, connectionId: Connection.ConnectionId);
            
            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
    }
    
    //DEBUG Doesn't call got methode
    public void GotFrame(byte[] data)
    {
        var frame = ByteFrame.Deserialize(data.ToList());
        //var reply = new ByteFrame();
        
        switch (frame.Type)
        {
            case FrameType.HandshakeInit:
                GotHandshakeInitRequest(frame);
                //AppData.TerminalOutput.Add($"DEBUG HandshakeInit: type: {frame.Type}, targetId: {Encoding.UTF8.GetString(frame.TargetId.ToArray())}, sourceId: {Encoding.UTF8.GetString(frame.SourceId.ToArray())}");
                break;
            case FrameType.HandshakeReply:
                GotHandshakeReply(frame);
                //AppData.TerminalOutput.Add($"DEBUG HandshakeReply: type: {frame.Type}, targetId: {Encoding.UTF8.GetString(frame.TargetId.ToArray())}, sourceId: {Encoding.UTF8.GetString(frame.SourceId.ToArray())}");
                break;
            case FrameType.Data:
                GotMessage(frame, out _);
                //AppData.TerminalOutput.Add($"DEBUG Message: type: {frame.Type}, targetId: {Encoding.UTF8.GetString(frame.TargetId.ToArray())}, sourceId: {Encoding.UTF8.GetString(frame.SourceId.ToArray())}");
                break;
            case FrameType.OkReply:
                GotOkReply(frame);
                break;
            case FrameType.ErrorReply:
                GotErrorReply(frame);
                break;
            case FrameType.Disconnect:
                GotDisconnectRequest(frame);
                //AppData.TerminalOutput.Add($"DEBUG Disconnect: type: {frame.Type}, targetId: {Encoding.UTF8.GetString(frame.TargetId.ToArray())}, sourceId: {Encoding.UTF8.GetString(frame.SourceId.ToArray())}");
                break;
            default:
                AppData.TerminalOutput.Add($"Error: Unknown frame type {frame.Type} received.\n");
                break;
        }
    }
}