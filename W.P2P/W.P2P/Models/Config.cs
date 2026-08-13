using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using W.P2P.Models;

namespace W.P2P;

public class Config
{
    private static readonly string ConfigFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "W.P2P", "config.json");

    public string Id { get; set; } = "";
    public string Name { get; set; } = "";

    public ObservableCollection<DataModels.Contact> IdMap { get; set; } = new ();

    public void SaveIdInMap(string id, string name)
    {
        var contact = new DataModels.Contact();
        contact.Id = id;
        contact.Name = name;
        contact.Key = null;
        IdMap.Add(contact);
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
            try
            {
                var json = File.ReadAllText(ConfigFilePath);
                config = JsonSerializer.Deserialize<Config>(json);
            }
            catch (Exception)
            {
                config = new Config();
                config.BuildDefault();
                config.SaveConfig();
            }

            Id = config!.Id;
            Name = config.Name;
            
            IdMap.Clear();
            foreach (var contact in config.IdMap)
                IdMap.Add(contact);
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
}