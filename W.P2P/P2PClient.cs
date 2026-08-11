using System.Security.Cryptography;
using System.Text;
using static W.P2P.Program;

namespace W.P2P;

public class P2PClient(Config config)
{
    //pending handshakes
    private static readonly Dictionary<string, ECDiffieHellman> Handshakes = new Dictionary<string, ECDiffieHellman>();
    
    public Models.ByteFrame Handshake(Models.Contact contact)
    {
        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        Handshakes[contact.Id] = ecdh;

        byte[] myPublicKey = ecdh.ExportSubjectPublicKeyInfo();
        
        var frame = BuildByteFrame(targetId: contact.Id, sourceId: config.Id, data: myPublicKey, id: Guid.NewGuid().ToString(), type: Models.FrameType.HandshakeInit);
        Send(frame);
        return frame;
    }

    public Models.ByteFrame GotHandshakeReply(Models.ByteFrame frame)
    {
        try
        {
            //DEBUG LINE
            //throw new Exception("DEBUG");
            
            byte[] theirKey = frame.Data.ToArray();
            var stringFrame = frame.ToStringFrame();

            var ecdh = Handshakes[stringFrame.SourceId];
            var contact = config.IdMap.FirstOrDefault(x => x.Id == stringFrame.SourceId) ??
                          throw new Exception($"Contact with ID {stringFrame.SourceId} not found.");
            contact.Key = SecurityManager.DeriveKey(ecdh, theirKey);

            Handshakes.Remove(stringFrame.SourceId);
            ecdh.Dispose();

            var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: stringFrame.TargetId, data: new byte[0], id: Guid.NewGuid().ToString(), type: Models.FrameType.OkReply);

            Console.WriteLine($"Handshake finished with {contact.Name}:{contact.Id}.");
            return reply;
        }
        catch (Exception e)
        {
            byte[] errorMessage = System.Text.Encoding.UTF8.GetBytes(e.Message);
            
            var reply = new Models.ByteFrame();
            BuildByteFrame(targetId: frame.ToStringFrame().SourceId, sourceId: frame.ToStringFrame().TargetId, data: errorMessage, id: Guid.NewGuid().ToString(), type: Models.FrameType.ErrorReply);
            return reply;
        }
    }

    public void GotErrorReply(Models.ByteFrame frame)
    {
        var stringFrame = frame.ToStringFrame();
        var messageBytes = frame.Data.ToArray();
        var message = System.Text.Encoding.UTF8.GetString(messageBytes);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Error from {stringFrame.SourceId}: {message}");
        Console.ResetColor();
    }
    
    public Models.ByteFrame GotHandshakeInitRequest(Models.ByteFrame frame)
    {
        byte[] theirPublicKey = frame.Data.ToArray();
        var stringFrame = frame.ToStringFrame();

        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        var contact = config.IdMap.FirstOrDefault(x => x.Id == stringFrame.SourceId) ?? throw new Exception($"No contact found for {stringFrame.SourceId}.");
        contact.Key = SecurityManager.DeriveKey(ecdh, theirPublicKey);

        byte[] myPublicKey = ecdh.ExportSubjectPublicKeyInfo();
        var reply = BuildByteFrame(targetId: stringFrame.SourceId, sourceId: stringFrame.TargetId, data: myPublicKey, id: Guid.NewGuid().ToString(), type: Models.FrameType.HandshakeReply);
        Send(reply);

        Console.WriteLine($"Handshake finished with {contact.Name}:{contact.Id}.");
        
        ecdh.Dispose();
        return reply;
    }
    
    public void Send(Models.ByteFrame frame)
    {
        var serialized = frame.Serialize();
                    
        Console.WriteLine(BitConverter.ToString(serialized.ToArray()));
        Console.WriteLine("------------------------------------------------------");
                    
        var deserialized = Models.ByteFrame.Deserialize(serialized);
                    
        var stringFrame = deserialized.ToStringFrame();
                    
        Console.WriteLine(stringFrame.Id);
        Console.WriteLine(stringFrame.SourceId);
        Console.WriteLine(stringFrame.TargetId);
        Console.WriteLine(stringFrame.Data);
    }
    
    public Models.ByteFrame BuildByteFrame(string targetId, string sourceId, byte[] data, string id, Models.FrameType type)
    {
        var frame = new Models.ByteFrame();
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
}