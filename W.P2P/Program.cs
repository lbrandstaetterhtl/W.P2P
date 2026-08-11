using System.Security.Cryptography;
using System.Text;
using static W.P2P.Models;

namespace W.P2P;

class Program
{
    //ID o current machine
    public static string? MyId;
    
    //config object
    private static readonly Config Config = new();
    
    //
    public static readonly List<string> ValidCommands = ["saveid", "idmap", "send", "exit", "handshake", "config"];
    
    private static CommandProcessor _commandProcessor = new(Config);
    
    //Main method, runs infinite loop to read commands from console and process them
    static void Main(string[] args)
    {
        Setup();
        
        while (true)
        {
            var input = Console.ReadLine();

            if (string.IsNullOrEmpty(input))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("No valid command was give! Please try again.");
                Console.ResetColor();
                continue;
            }
            
            var parts = input.Split(' ');
            
            var command = parts[0].ToLower();

            if (command.Equals(ValidCommands[0]))
            {
                _commandProcessor.HandleSaveIdCommand(parts);
            }
            else if (command.Equals(ValidCommands[1]))
            {
                Config.PrintIdMap();
            }
            else if (command.Equals(ValidCommands[2]))
            {
                _commandProcessor.HandleSendCommand(parts);
            }
            else if (command.Equals(ValidCommands[3]))
            {
                Config.SaveConfig();
                Environment.Exit(0);
            }
            else if (command.Equals(ValidCommands[4]))
            {
                _commandProcessor.HandleHandshakeCommand(parts);
            }
            else if (command.Equals(ValidCommands[5]))
            {
                _commandProcessor.HandleConfigCommand(parts);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Invalid command: {command}. Please try again.");
                Console.ResetColor();
            }
            
            Console.WriteLine();
        }
    }

    //Sets the environment
    private static void Setup()
    {
        Config.LoadConfig();
        MyId = Config.Id;
        _commandProcessor = new CommandProcessor(Config);
    }
}