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
            
            var sent = _client.SendMessage(targetId, config.Id, dataBytes, key);

            if (sent == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to send message to {targetId}.");
                Console.ResetColor();
            }
            else
            {
                var stringFrame = new Models.StringFrame();
                var reply = _client.GotMessage(sent, out stringFrame);
                _client.SendFrame(reply);
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Not enough arguments for {ValidCommands[2]}");
            Console.ResetColor();
        }
    }
    
    public void HandleSaveIdCommand(string[] parts)
    {
        if (parts.Length >= 3)
        {
            var id = parts[1];
            var name = parts[2];
                    
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
            Console.WriteLine($"Not enough arguments for {ValidCommands[4]}");
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
            Console.WriteLine($"No valid options for {ValidCommands[4]}");
            Console.ResetColor();
        }
    }

    public void HandleConnectCommand(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Not enough arguments for {ValidCommands[5]}");
            Console.ResetColor();
            return;
        }
        
        var name = parts[1];
        
        var contact = config.IdMap.FirstOrDefault(x => x.Name == name) ?? throw new Exception($"No contact found for {name}");
                
        var result = _client.Connect(contact);

        if (!result)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Failed to connect to contact with id {contact.Id}.");
            Console.ResetColor();
            
            _client.Connection = new Models.Connection();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Connected to contact with id {contact.Id}.");
            Console.ResetColor();
        }
    }

    public void HandleDisconnectCommand()
    {
        _client.Disconnect();
    }

    public void HandleConnectionCommand()
    {
        PrintConnection();
    }

    private void PrintConnection()
    {
        Console.ForegroundColor = ConsoleColor.Green;
        var targetId = _client.Connection.TargetId;
        var sourceId = _client.Connection.SourceId;
        var connectionId = _client.Connection.ConnectionId;
        
        Console.WriteLine($"TargetId: {targetId}, SourceId: {sourceId}, ConnectionId: {connectionId}");
        Console.ResetColor();
    }
}