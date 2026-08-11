using System.Text;
using static W.P2P.Program;

namespace W.P2P;

//NOTE! parts[0] is the command itself
public class CommandProcessor(Config config)
{
    private readonly P2PClient _client = new(config);
    
    public void HandleSendCommand(string[] parts)
    {
        if (parts.Length >= 3)
        {
            var targetIdName = parts[1];
            var data = string.Join(" ", parts.Skip(2)).Trim('"');
            var contact = config.IdMap.FirstOrDefault(x => x.Name == targetIdName) ?? throw new Exception($"Target ID {targetIdName} not found.");
            var targetId = contact.Id;
            var key = contact.Key ?? throw new Exception($"No key found for {contact.Name}:{targetId}");
                    
            var dataBytes = Encoding.UTF8.GetBytes(data);
                    
            var frame = _client.BuildByteFrame(targetId: targetId, sourceId: config.Id, data: dataBytes, id: Guid.NewGuid().ToString(), type: Models.FrameType.Data);
            _client.Send(frame);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Not enough arguments for {ValidCommands[2]}");
            Console.ResetColor();
        }
    }

    public void HandleHandshakeCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Not enough arguments for {ValidCommands[4]}");
            Console.ResetColor();
        }
                
        var contact = config.IdMap.FirstOrDefault(x => x.Name == parts[1]) ?? throw new Exception($"No contact found for {parts[1]}");
                
        var sentFrame = _client.Handshake(contact);
                
        var reply = _client.GotHandshakeInitRequest(sentFrame);
                
        reply = _client.GotHandshakeReply(reply);
        
        if (reply.Type == Models.FrameType.ErrorReply)
        {
            _client.GotErrorReply(reply);
        }
    }
    
    public void HandleSaveIdCommand(string[] parts)
    {
        if (parts.Length >= 3)
        {
            var id = parts[1];
            var name = parts[2];

            if (name.Equals("MyId"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Can't use 'MyId' as a name, because it's a constant.");
                Console.WriteLine();
                Console.ResetColor();
                return;
            }
                    
            config.SaveIdInMap(id, name);
                    
            Console.WriteLine($"{parts[2]} has been saved with id {parts[1]}.");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Not enough arguments for {ValidCommands[0]}");
            Console.ResetColor();
        }
    }
    
    public void HandleConfigCommand(string[] parts)
    {
        List<string> validOptions = ["renameid", "viewid", "delete", "editid", "viewall"];
        
        if (parts.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Not enough arguments for {ValidCommands[5]}");
            Console.ResetColor();
            return;
        }
        
        var option = parts[1].ToLower();
        if (option.Equals(validOptions[0]) && parts.Length >= 4)
        {
            var id = parts[2];
            var newName = parts[3];
            
            var contact = config.IdMap.FirstOrDefault(x => x.Id == id) ?? throw new Exception($"No contact found for {id}");
            contact.Name = newName;
            
            Console.WriteLine($"Contact with id {id} has been renamed to {newName}.");
        }
        else if (option.Equals(validOptions[1]))
        {
            var id = parts[2];
            config.PrintContact(id);
        }
        else if (option.Equals(validOptions[2]) && parts.Length >= 3)
        {
            var id = parts[2];
            
            var contact = config.IdMap.FirstOrDefault(x => x.Id == id) ?? throw new Exception($"No contact found for {id}");
            config.IdMap.Remove(contact);
            
            Console.WriteLine($"Contact with id {id} has been deleted.");
        }
        else if (option.Equals(validOptions[3]) && parts.Length >= 3)
        {
            var oldId = parts[2];
            var newId = parts[3];
            
            var contact = config.IdMap.FirstOrDefault(x => x.Id == oldId) ?? throw new Exception($"No contact found for {oldId}");
            contact.Id = newId;
            
            Console.WriteLine($"Contact with id {oldId} has been edited to {newId}.");
        }
        else if (option.Equals(validOptions[4]) && parts.Length >= 2)
        {
            config.PrintConfig();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Not no valid options for {ValidCommands[5]}");
            Console.ResetColor();
        }
    }
}