using System.Text;
using static W.P2P.Models;

namespace W.P2P;

class Program
{
    private static readonly string _myId = Guid.NewGuid().ToString();
    private static Config _config = new Config();
    
    static void Main(string[] args)
    {
        Console.Clear();

        List<string> commands = [ "saveId", "idMap", "send", "exit"];
        
        _config.LoadConfig();
        
        while (true)
        {
            var input = Console.ReadLine();
            
            var parts = input.Split(' ');

            if (parts[0].Equals(commands[0]))
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
            else if (parts[0].Equals(commands[1]))
            {
                _config.PrintIdMap();
            }
            else if (parts[0].Equals(commands[2]))
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
                    var targetId = _config.IdMap.FirstOrDefault(x => x.Key == targetIdName).Value;
                    
                    var frame = new ByteFrame();
                    frame.BuildFrame(targetId: targetId, sourceId: _myId, data: data, position: Convert.ToInt32(position), id: Guid.NewGuid().ToString());
                    
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
            else if (parts[0].Equals(commands[3]))
            {
                _config.SaveConfig();
                Environment.Exit(0);
            }
            
            Console.WriteLine();
        }
    }
}