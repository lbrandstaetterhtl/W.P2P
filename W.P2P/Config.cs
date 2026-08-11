using System.Text.Json;
using static W.P2P.Models;

namespace W.P2P;

public class Config
{
    private static readonly string ConfigFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "W.P2P", "config.json");

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    public List<Contact> IdMap { get; set; } = new List<Contact>();

    public void SaveIdInMap(string id, string name)
    {
        var contact = new Contact();
        contact.Id = id;
        contact.Name = name;
        contact.Key = null;
        IdMap.Add(contact);
    }

    public void PrintIdMap()
    {
        Console.WriteLine("----------------------------------");
        foreach (var contact in IdMap)
        {
            Console.WriteLine($"ID: {contact.Id}");
            Console.WriteLine($"Name: {contact.Name}");
            Console.WriteLine($"Key: {contact.Key}");

            Console.WriteLine("----------------------------------");
        }
    }

    public void BuildDefault()
    {
        Id = Guid.NewGuid().ToString();
        Name = Environment.MachineName;

        SaveIdInMap(Id, Name);
    }

    public void LoadConfig()
    {
        var config = new Config();
        config.BuildDefault();
        if (File.Exists(ConfigFilePath))
        {
            var json = File.ReadAllText(ConfigFilePath);
            config = JsonSerializer.Deserialize<Config>(json);

            Id = config!.Id;
            Name = config.Name;
            IdMap = config.IdMap;
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFilePath));
            File.Create(Path.Combine(ConfigFilePath)).Close();
            config.SaveConfig();
            LoadConfig();
        }
    }

    public void SaveConfig()
    {
        var config = new Config();
        config.IdMap = IdMap;
        config.Id = Id;
        config.Name = Name;
        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(ConfigFilePath, json);
    }

    public void PrintConfig()
    {
        Console.WriteLine();
        Console.WriteLine($"Id: {Id}");
        Console.WriteLine($"Name: {Name}");
        Console.WriteLine();
        Console.WriteLine("----------------------------------");
        Console.WriteLine();
        Console.WriteLine($"IdMap: {IdMap.Count}");
        PrintIdMap();
    }

    public void PrintContact(string id)
    {
        var contact = IdMap.FirstOrDefault(x => x.Id == id);

        if (contact != null)
        {
            Console.WriteLine("----------------------------------");
            Console.WriteLine($"Id: {contact.Id}");
            Console.WriteLine($"Name: {contact.Name}");
            Console.WriteLine($"Key: {contact.Key}");
            Console.WriteLine("----------------------------------");
        }
    }
}