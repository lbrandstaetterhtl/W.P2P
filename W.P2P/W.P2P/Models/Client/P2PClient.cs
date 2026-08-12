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
        
        lastSentFrame = frame;
        
        AppData.TerminalOutput.Add($"Sending Handshake [id: {frame.Id}] request to {contact.Name}|{AppData.Config.Id}...\n");

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

            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: stringFrame.TargetId, data: new byte[0], id: Guid.NewGuid().ToString(), type: FrameType.OkReply);
            
            AppData.TerminalOutput.Add($"Sending Ok reply [id: {reply.Id}] to {contact.Name}|{stringFrame.SourceId}...\n");
            SendFrame(reply);
            lastSentFrame = reply;
            return reply; //TODO: SEND LOGIC
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}\n");

            return null;
        }
        catch (BrokenFrame e)
        {
            byte[] errorMessage = Encoding.UTF8.GetBytes(e.Message);
            
            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: frame.ToStringFrame().TargetId, data: errorMessage, id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply);
            
            AppData.TerminalOutput.Add($"Error: {e.Message}\n");
            
            lastSentFrame = reply;
            
            SendFrame(reply);
            
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
            AppData.TerminalOutput.Add($"Trying to send the frame [id: {lastSentFrame.Id}] again...\n");

            SendFrame(lastSentFrame);
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

            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: stringFrame.TargetId,
                data: myPublicKey, id: Guid.NewGuid().ToString(), type: FrameType.HandshakeReply);
            
            AppData.TerminalOutput.Add($"Handshake completed with {contact.Name}|{stringFrame.SourceId}.\n");
            
            AppData.TerminalOutput.Add($"Sending Handshake reply [id: {reply.Id}] to {contact.Name}|{stringFrame.SourceId}...\n");

            ecdh.Dispose();
            
            SendFrame(reply);
            lastSentFrame = reply;
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
            
            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: frame.ToStringFrame().TargetId, data: Encoding.UTF8.GetBytes(e.Message), id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply);
            lastSentFrame = reply;
            SendFrame(reply);
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
            AppData.ReceivedMessages.Add(stringFrame);

            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: stringFrame.TargetId,
                data: new byte[0], id: Connection.ConnectionId, type: FrameType.OkReply);

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
            var messageBytes = Encoding.UTF8.GetBytes(e.Message);
            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: frame.ToStringFrame().TargetId,
                data: messageBytes, id: Connection.ConnectionId, type: FrameType.ErrorReply);
            stringFrame = null;
            return reply;
        }
    }

    public void SendFrame(ByteFrame frame)
    {
        var stringFrame = frame.ToStringFrame();
        AppData.TerminalOutput.Add($"Sending frame [id: {frame.Id}] to {stringFrame.TargetId}...\n");
        
        //DEBUG
        AppData.TerminalOutput.Add($"DEBUG: Frame id: {stringFrame.Id}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame type: {stringFrame.Type}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame targetId: {stringFrame.TargetId}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame sourceId: {stringFrame.SourceId}\n");
        AppData.TerminalOutput.Add($"DEBUG: Frame data: {stringFrame.Data}\n");
        
        lastSentFrame = frame;
    }
    
    public ByteFrame SendMessage(byte[] data, byte[] key)
    {
        if (string.IsNullOrEmpty(Connection.TargetId) || string.IsNullOrEmpty(Connection.SourceId))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            AppData.TerminalOutput.Add("Error: No connection established. Please connect to a contact first.\n");
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

    public bool Connect(Contact contact, string sourceId)
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
        Connection.SourceId = sourceId;
        Connection.ConnectionId = Guid.NewGuid().ToString();
        return true;
    }

    public bool Disconnect()
    {
        try
        {
            var contact = AppData.Config.IdMap.FirstOrDefault(x => x.Id == Connection.TargetId) ??
                          throw new ContactNotFound($"No contact found for {Connection.TargetId}.");
            AppData.TerminalOutput.Add($"Disconnecting with {contact.Name}|{contact.Id}...\n");
            
            var frame = BuildByteFrame(targetId: Connection.TargetId, sourceId: Connection.SourceId, data: new byte[0], id: Connection.ConnectionId, type: FrameType.Disconnect);
            SendFrame(frame);
            
            var reply = GotDisconnectRequest(frame);

            if (reply.Type != FrameType.OkReply)
            {
                GotErrorReply(reply);
                return false;
            }
            else
            {
                GotOkReply(reply);
                Connection = new Connection();
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
        
        var contact = AppData.Config.IdMap.FirstOrDefault(x => x.Id == stringFrame.SourceId) ??
                      throw new ContactNotFound($"No contact found for {stringFrame.SourceId}.");
        
        AppData.TerminalOutput.Add($"Got Ok Reply from {contact.Name}|{contact.Id} for frame {lastSentFrame.Id}\n");
    }

    public ByteFrame GotDisconnectRequest(ByteFrame frame)
    {
        try
        {
            //DEBUG
            //throw new BrokenFrame("DEBUG");
            
            var stringFrame = frame.ToStringFrame();

            var contact = AppData.Config.IdMap.FirstOrDefault(x => x.Id == stringFrame.SourceId) ??
                          throw new ContactNotFound($"No contact found for {stringFrame.SourceId}.");

            AppData.TerminalOutput.Add(
                $"Got Disconnect Request from {contact.Name}|{contact.Id} for frame {lastSentFrame.Id}\n");

            AppData.TerminalOutput.Add($"Disconnecting with {contact.Name}|{contact.Id}...\n");

            Connection = new Connection();

            AppData.TerminalOutput.Add($"Sending Ok reply to {contact.Name}|{contact.Id}...\n");
            
            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: stringFrame.TargetId, data: new byte[0], id: Guid.NewGuid().ToString(), type: FrameType.OkReply);
            SendFrame(reply);
            lastSentFrame = reply;
            return reply;
        }
        catch (ContactNotFound c)
        {
            AppData.TerminalOutput.Add($"Error: {c.Message}\n");
            return null;
        }
        catch (BrokenFrame ex)
        {
            var messageBytes = Encoding.ASCII.GetBytes(ex.Message);
            
            var reply = BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: frame.ToStringFrame().TargetId,
                data: messageBytes, id: Guid.NewGuid().ToString(), type: FrameType.ErrorReply);
            
            SendFrame(reply);
            lastSentFrame = reply;
            return reply;
        }
    }
}