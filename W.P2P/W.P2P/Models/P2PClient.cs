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

    public ByteFrame lastSentFrame;
    
    public ByteFrame Handshake(Contact contact)
    {
        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        Handshakes[contact.Id] = ecdh;

        byte[] myPublicKey = ecdh.ExportSubjectPublicKeyInfo();

        var frame = BuildByteFrame(targetId: contact.Id, sourceId: AppData.Config.Id, data: myPublicKey,
            id: Guid.NewGuid().ToString(), type: FrameType.HandshakeInit);
        SendFrame(frame);
        
        AppData.TerminalOutput.Add($"Sending Handshake request to {contact.Name}|{AppData.Config.Id}...");

        return frame;
    }

    public ByteFrame GotHandshakeReply(ByteFrame frame)
    {
        try
        {
            //DEBUG LINE
            //throw new Exception("DEBUG");
            
            byte[] theirKey = frame.Data.ToArray();
            var stringFrame = frame.ToStringFrame();

            var ecdh = Handshakes[stringFrame.SourceId];
            var contact = AppData.Config.IdMap.FirstOrDefault(x => x.Id == stringFrame.SourceId) ??
                          throw new ContactNotFound($"Contact with ID {stringFrame.SourceId} not found.");
            contact.Key = SecurityManager.DeriveKey(ecdh, theirKey);

            Handshakes.Remove(stringFrame.SourceId);
            ecdh.Dispose();
            
            AppData.TerminalOutput.Add($"Handshake completed with {contact.Name}|{stringFrame.SourceId}. Sending OkReply....");

            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: stringFrame.TargetId, data: new byte[0], id: Guid.NewGuid().ToString(), type: FrameType.OkReply);
            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}");

            return null;
        }
        catch (Exception e)
        {
            byte[] errorMessage = Encoding.UTF8.GetBytes(e.Message);
            
            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: frame.ToStringFrame().TargetId, data: errorMessage, id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply);
            
            AppData.TerminalOutput.Add($"Error: {e.Message}");
            
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
                $"Error reply received from {contact.Name}|{stringFrame.SourceId}: {stringFrame.Data}");
            AppData.TerminalOutput.Add("Trying to sned the frame again...");

            SendFrame(lastSentFrame);
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}");
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

            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: stringFrame.TargetId,
                data: myPublicKey, id: Guid.NewGuid().ToString(), type: FrameType.HandshakeReply);
            
            AppData.TerminalOutput.Add($"Handshake completed with {contact.Name}|{stringFrame.SourceId}. Sending HandshakeReply....");

            ecdh.Dispose();
            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}");
            return null;
        }
        catch (Exception e)
        {
            AppData.TerminalOutput.Add($"Error: {e.Message}");
            
            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: frame.ToStringFrame().TargetId, data: Encoding.UTF8.GetBytes(e.Message), id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply);
            return reply;
        }
    }

    public ByteFrame GotMessage(ByteFrame frame, out StringFrame stringFrame)
    {
        try
        {
            //throw new Exception("DEBUG");

            stringFrame = frame.ToStringFrame();
            AppData.ReceivedMessages.Add(stringFrame);

            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: stringFrame.TargetId,
                data: new byte[0], id: Connection.ConnectionId, type: FrameType.OkReply);

            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}");
            stringFrame = null;
            return null;
        }
        catch (Exception e)
        {
            AppData.TerminalOutput.Add($"Error: {e.Message}");
            var messageBytes = Encoding.UTF8.GetBytes(e.Message);
            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: frame.ToStringFrame().TargetId,
                data: messageBytes, id: Connection.ConnectionId, type: FrameType.ErrorReply);
            stringFrame = null;
            return reply;
        }
    }

    public ByteFrame SendFrame(Models.ByteFrame frame)
    {
        return null;
    }
    
    public ByteFrame SendMessage(byte[] data, byte[] key)
    {
        if (string.IsNullOrEmpty(Connection.TargetId) || string.IsNullOrEmpty(Connection.SourceId))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            AppData.TerminalOutput.Add("Error: No connection established. Please connect to a contact first.");
            Console.ResetColor();
            return null;
        }
        
        var frame = BuildByteFrame(targetId: Connection.TargetId, sourceId: Connection.SourceId, data: data, id: Connection.ConnectionId, type: FrameType.Data);
        var stringFrame = frame.ToStringFrame();
        AppData.SentMessages.Add(stringFrame);
        SendFrame(frame);
        
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
        
        Connection = new Connection();
        Connection.TargetId = contact.Id;
        Connection.SourceId = AppData.Config.Id;
        Connection.ConnectionId = Guid.NewGuid().ToString();
        return true;
    }

    public void Disconnect()
    {
        try
        {
            var contact = AppData.Config.IdMap.FirstOrDefault(x => x.Id == Connection.TargetId) ??
                          throw new ContactNotFound($"No contact found for {Connection.TargetId}.");
            Console.WriteLine($"Disconnected with {contact.Name}:{contact.Id}.");
            Connection = new Connection();
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}");
        }
    }
}