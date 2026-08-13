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

    private List<ByteFrame> _lastSentFrames = new();

    private readonly Queue<ByteFrame> _frameQueue = new();
    
    public ByteFrame Handshake(Contact contact)
    {
        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        Handshakes[contact.Id] = ecdh;

        byte[] myPublicKey = ecdh.ExportSubjectPublicKeyInfo();

        var frame = BuildByteFrame(targetId: contact.Id, sourceId: AppData.Config.Id, data: myPublicKey,
            id: Guid.NewGuid().ToString(), type: FrameType.HandshakeInit);
        
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

            var ecdh = Handshakes[stringFrame.SourceId];
            var contact = AppData.Config.IdMap.FirstOrDefault(x => x.Id == stringFrame.SourceId) ??
                          throw new ContactNotFound($"Contact with ID {stringFrame.SourceId} not found.");
            contact.Key = SecurityManager.DeriveKey(ecdh, theirKey);

            Handshakes.Remove(stringFrame.SourceId);
            ecdh.Dispose();
            
            AppData.TerminalOutput.Add($"Handshake completed with {contact.Name}|{stringFrame.SourceId}.\n");

            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: AppData.Config.Id, data: new byte[0], id: Guid.NewGuid().ToString(), type: FrameType.OkReply);
            
            AppData.TerminalOutput.Add($"Sending Ok reply [id: {reply.Id}] to {contact.Name}|{stringFrame.SourceId}...\n");
            
            _frameQueue.Enqueue(reply);
            SendFrame();

            return reply; //TODO: SEND LOGIC
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}\n");

            return null;
        }
        catch (BrokenFrame e)
        {
            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: AppData.Config.Id, data: frame.Id.ToArray(), id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply);
            
            AppData.TerminalOutput.Add($"Error: {e.Message}\n");
            
            _frameQueue.Enqueue(reply);
            SendFrame();
            
            return reply;
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
            AppData.TerminalOutput.Add($"Error: {c.Message}\n");
        }
    }
    
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

            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: AppData.Config.Id,
                data: myPublicKey, id: Guid.NewGuid().ToString(), type: FrameType.HandshakeReply);
            
            AppData.TerminalOutput.Add($"Handshake completed with {contact.Name}|{stringFrame.SourceId}.\n");
            
            AppData.TerminalOutput.Add($"Sending Handshake reply [id: {reply.Id}] to {contact.Name}|{stringFrame.SourceId}...\n");

            ecdh.Dispose();
            
            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}\n");
            return null;
        }
        catch (Exception e)
        {
            AppData.TerminalOutput.Add($"Error: {e.Message}\n");
            
            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: AppData.Config.Id, data: frame.Id.ToArray(), id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply);
            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
    }

    public ByteFrame GotMessage(ByteFrame frame, out StringFrame stringFrame)
    {
        try
        {
            //DEBUG
            //throw new Exception("DEBUG");
            
            stringFrame = frame.ToStringFrame(); 
            var decrypted = SecurityManager.Decrypt(frame.Data.ToArray(), Connection.SharedKey);
            var message = Encoding.UTF8.GetString(decrypted);
            
            AppData.ReceivedMessages.Add($"Message from {Connection.TargetName}|{Connection.TargetId}: {message}");

            var reply = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id,
                data: new byte[0], id: Connection.ConnectionId, type: FrameType.OkReply);

            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}\n");
            stringFrame = null;
            return null;
        }
        catch (Exception e)
        {
            AppData.TerminalOutput.Add($"Error: {e.Message}\n");
            var reply = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id,
                data: frame.Id.ToArray(), id: Connection.ConnectionId, type: FrameType.ErrorReply);
            stringFrame = null;
            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
    }

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
        AppData.TerminalOutput.Add($"DEBUG: Frame id: {stringFrame.Id}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame type: {stringFrame.Type}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame targetId: {stringFrame.TargetId}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame sourceId: {stringFrame.SourceId}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame data: {stringFrame.Data}\n");
    }
    
    public ByteFrame SendMessage(byte[] data, byte[] key)
    {
        if (!Connection.IsConnected)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            AppData.TerminalOutput.Add("Error: No connection established. Please connect to a contact first.\n");
            Console.ResetColor();
            return null;
        }
        
        var encrypted = SecurityManager.Encrypt(data, key);
        var frame = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id, data: encrypted, id: Connection.ConnectionId, type: FrameType.Data);
        AppData.SentMessages.Add($"Message sent to {Connection.TargetName}|{Connection.TargetId}.");
        _frameQueue.Enqueue(frame);
        SendFrame();
        return frame;
    }
    
    public ByteFrame BuildByteFrame(string targetId, string sourceId, byte[] data, string id, FrameType type)
    {
        var frame = new ByteFrame();
        frame.Id = new List<byte>(Encoding.ASCII.GetBytes(id));
            
        var targetIdBytes = new List<byte>(Encoding.ASCII.GetBytes(targetId));
        var sourceIdBytes = new List<byte>(Encoding.ASCII.GetBytes(sourceId));
        var dataBytes = data.ToList();
            
        frame.Type = type;
        frame.TargetId = targetIdBytes;
        frame.SourceId = sourceIdBytes;
        frame.Data = dataBytes;
        frame.CalculateChecksum();
        return frame;
    }

    public bool Connect(Contact contact)
    {
        var reply = Handshake(contact);
        
        reply = GotHandshakeInitRequest(reply);
        
        reply = GotHandshakeReply(reply);

        if (reply.Type != FrameType.OkReply)
        {
            GotErrorReply(reply);
            return false;
        }
        else
        {
            GotOkReply(reply);
        }
        
        Connection = new Connection();
        Connection.TargetId = contact.Id;
        Connection.ConnectionId = Guid.NewGuid().ToString();
        Connection.IsConnected = true;
        Connection.TargetName = contact.Name;
        Connection.SharedKey = contact.Key;
        return true;
    }

    public bool Disconnect()
    {
        try
        {
            AppData.TerminalOutput.Add($"Disconnecting with {Connection.TargetName}|{Connection.TargetId}...\n");
            
            var frame = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id, data: new byte[0], id: Connection.ConnectionId, type: FrameType.Disconnect);
            _frameQueue.Enqueue(frame);
            SendFrame();
            
            var reply = GotDisconnectRequest(frame);

            if (reply.Type != FrameType.OkReply)
            {
                GotErrorReply(reply);
                return false;
            }
            else
            {
                GotOkReply(reply);
                Connection.TargetId = "";
                Connection.ConnectionId = "";
                Connection.IsConnected = false;
                return true;
            }
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}\n");
            return false;
        }
        catch (BrokenFrame e)
        {
            AppData.TerminalOutput.Add($"Error: {e.Message}\n");
            return false;
        }
    }

    public void GotOkReply(ByteFrame frame)
    {
        var stringFrame = frame.ToStringFrame();
        
        AppData.TerminalOutput.Add($"Got Ok Reply from {Connection.TargetName}|{Connection.TargetId} for frame {stringFrame.Id}\n");
    }

    public ByteFrame GotDisconnectRequest(ByteFrame frame)
    {
        try
        {
            //DEBUG
            //throw new BrokenFrame("DEBUG");
            
            var stringFrame = frame.ToStringFrame();

            AppData.TerminalOutput.Add(
                $"Got Disconnect Request from {Connection.TargetName}|{Connection.TargetId} for frame {stringFrame.Id}\n");

            AppData.TerminalOutput.Add($"Disconnecting with {Connection.TargetName}|{Connection.TargetId}...\n");

            Connection = new Connection();

            AppData.TerminalOutput.Add($"Sending Ok reply to {Connection.TargetName}|{Connection.TargetId}...\n");
            
            var reply = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id, data: new byte[0], id: Guid.NewGuid().ToString(), type: FrameType.OkReply);

            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}\n");
            return null;
        }
        catch (BrokenFrame ex)
        {
            var reply = BuildByteFrame(targetId: Connection.TargetId, sourceId: AppData.Config.Id,
                data: frame.Id.ToArray(), id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply);
            
            _frameQueue.Enqueue(reply);
            SendFrame();
            return reply;
        }
    }
}