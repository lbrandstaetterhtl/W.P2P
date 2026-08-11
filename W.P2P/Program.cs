using System.Security.Cryptography;
using System.Text;
using static W.P2P.Models;

namespace W.P2P;

class Program
{
    private static string _myId;
    private static Config _config = new Config();
    
    static void Main(string[] args)
    {
        Console.Clear();

        List<string> commands = [ "saveid", "idmap", "send", "exit", "handshake"];
        
        _config.LoadConfig();
        _myId = _config.Id;
        
        while (true)
        {
            var input = Console.ReadLine();
            
            var parts = input.Split(' ');

            if (parts[0].ToLower().Equals(commands[0]))
            {
                if (parts.Length >= 3)
                {
                    var id = parts[1];
                    var name = parts[2];

                    if (name.Equals("MyId"))
                    {
                        Console.WriteLine("Can't use 'MyId' as a name, because it's a constant.");
                        Console.WriteLine();
                        continue;
                    }
                    
                    _config.SaveIdInMap(id, name);
                    
                    Console.WriteLine($"{parts[2]} has been saved with id {parts[1]}.");
                }
                else
                {
                    Console.WriteLine($"Not enough arguments for {commands[0]}");
                }
            }
            else if (parts[0].ToLower().Equals(commands[1]))
            {
                _config.PrintIdMap();
            }
            else if (parts[0].ToLower().Equals(commands[2]))
            {
                if (parts.Length >= 4)
                {
                    var targetIdName = parts[1];
                    var position = parts[2];
                    var data = "";

                    if (parts[3].StartsWith("\""))
                    {
                        data += parts[3] + " ";

                        for (int i = 4; i < parts.Length; i++)
                        {
                            data += parts[i];
                            
                            if (parts[i].EndsWith("\""))
                            {
                                break;
                            }
                            
                            data += " ";
                        }
                    }
                    
                    data = data.Replace("\"", "");
                    var contact = _config.IdMap.FirstOrDefault(x => x.Name == targetIdName) ?? throw new Exception($"Target ID {targetIdName} not found.");
                    var targetId = contact.Id;
                    var key = contact.Key ?? throw new Exception($"No key found for {contact.Name}:{targetId}");
                    
                    var dataBytes = Convert.FromBase64String(data);
                    
                    var frame = new ByteFrame();
                    frame.BuildFrame(targetId: targetId, sourceId: _myId, data: dataBytes, position: Convert.ToInt32(position), id: Guid.NewGuid().ToString(), type: FrameType.Data);
                    Send(frame);
                }
                else
                {
                    Console.WriteLine($"Not enough arguments for {commands[2]}");
                }
            }
            else if (parts[0].ToLower().Equals(commands[3]))
            {
                _config.SaveConfig();
                Environment.Exit(0);
            }
            else if (parts[0].ToLower().Equals(commands[4]))
            {
                if (parts.Length < 2)
                {
                    throw new Exception($"Not enough arguments for {commands[4]}");
                }
                
                var contact = _config.IdMap.FirstOrDefault(x => x.Name == parts[1]) ?? throw new Exception($"No contact found for {parts[1]}");
                
                var sentFrame = Handshake(contact);
                
                var reply = HandleHandshakeInit(sentFrame);
                
                GotHandshake(reply);
            }
            
            Console.WriteLine();
        }
    }

    private static Dictionary<string, ECDiffieHellman> _handshakes = new Dictionary<string, ECDiffieHellman>();
    
    private static ByteFrame Handshake(Contact contact)
    {
        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        _handshakes[contact.Id] = ecdh;

        byte[] myPublicKey = ecdh.ExportSubjectPublicKeyInfo();
        
        var frame = new ByteFrame();
        frame.BuildFrame(targetId: contact.Id, sourceId: _myId, data: myPublicKey, position: 0, id: Guid.NewGuid().ToString(), type: FrameType.HandshakeInit);
        Send(frame);
        return frame;
    }

    private static void GotHandshake(ByteFrame frame)
    {
        byte[] theirKey = frame.Data.ToArray();
        var stringFrame = frame.ToStringFrame();
        
        var ecdh = _handshakes[stringFrame.SourceId];
        var contact = _config.IdMap.FirstOrDefault(x => x.Id == stringFrame.SourceId) ?? throw new Exception($"Contact with ID {stringFrame.SourceId} not found.");
        contact.Key = DeriveKey(ecdh, theirKey);
        
        _handshakes.Remove(stringFrame.SourceId);
        ecdh.Dispose();
        
        Console.WriteLine($"Handshake finished with {contact.Name}:{contact.Id}.");
    }
    
    private static ByteFrame HandleHandshakeInit(ByteFrame frame)
    {
        byte[] theirPublicKey = frame.Data.ToArray();
        var stringFrame = frame.ToStringFrame();

        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        var contact = _config.IdMap.FirstOrDefault(x => x.Id == stringFrame.SourceId) ?? throw new Exception($"No contact found for {stringFrame.SourceId}.");
        contact.Key = DeriveKey(ecdh, theirPublicKey);

        byte[] myPublicKey = ecdh.ExportSubjectPublicKeyInfo();
        var reply = new ByteFrame();
        reply.BuildFrame(targetId: stringFrame.SourceId, sourceId: stringFrame.TargetId, data: myPublicKey,  position: 0, id: Guid.NewGuid().ToString(), type: FrameType.HandshakeReply);
        Send(reply);

        Console.WriteLine($"Handshake finished with {contact.Name}:{contact.Id}.");
        
        return reply;
    }
    
    private static byte[] DeriveKey(ECDiffieHellman myEcdh, byte[] theirPublicKeyBytes)
    {
        using var theirPub = ECDiffieHellman.Create();
        theirPub.ImportSubjectPublicKeyInfo(theirPublicKeyBytes, out _);
        return myEcdh.DeriveKeyFromHash(theirPub.PublicKey, HashAlgorithmName.SHA256);
    }

    private static void Send(ByteFrame frame)
    {
        var serialized = frame.Serialize();
                    
        Console.WriteLine(BitConverter.ToString(serialized.ToArray()));
        Console.WriteLine("------------------------------------------------------");
                    
        var deserialized = ByteFrame.Deserialize(serialized);
                    
        var stringFrame = deserialized.ToStringFrame();
                    
        Console.WriteLine(stringFrame.Id);
        Console.WriteLine(stringFrame.SourceId);
        Console.WriteLine(stringFrame.TargetId);
        Console.WriteLine(stringFrame.Data);
        Console.WriteLine(stringFrame.Position);
    }
}