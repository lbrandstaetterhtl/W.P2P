using System.Text.Json;

namespace W.P2P;

public class Config
{
    private static readonly string _configFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "config.json");
    public string Id  { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, string> IdMap { get; set; } = new Dictionary<string, string>();
    
    public void SaveIdInMap(string id, string name)
    {
        IdMap.Add(name, id);
    }

    public void PrintIdMap()
    {
        Console.WriteLine("----------------------------------");
        foreach (var pair in IdMap)
        {
            Console.WriteLine($"{pair.Key}: {pair.Value}");
        }
        Console.WriteLine("----------------------------------");
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
        if (File.Exists(_configFilePath))
        {
            var json = File.ReadAllText(_configFilePath);
            config = JsonSerializer.Deserialize<Config>(json);
            
            Id = config.Id;
            Name = config.Name;
            IdMap = config.IdMap;
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_configFilePath));
            File.Create(Path.Combine(_configFilePath)).Close();
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
        File.WriteAllText(_configFilePath, json);
    }
}