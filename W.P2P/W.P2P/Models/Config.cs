using System;
using System.Collections.Generic;
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
    public byte[] HardwareId { get; set; }

    public ObservableCollection<DataModels.Contact> IdMap { get; set; } = new ();

    public void SaveIdInMap(string id, string name)
    {
        var contact = new DataModels.Contact();
        contact.Id = id;
        contact.Name = name;
        contact.Key = null;
        contact.SetHardwareId();
        IdMap.Add(contact);
    }

    public void BuildDefault()
    {
        Id = Guid.NewGuid().ToString();
        Name = Environment.MachineName;
        
        HardwareId = new byte[5];
        HardwareId[0] = (byte)Id[0];
        HardwareId[1] = 0xF0;
        HardwareId[2] = 0xF0;
        HardwareId[3] = 0xE8;
        HardwareId[4] = 0xC5;
        
        //DEBUG TEST ID
        //var id = Guid.NewGuid().ToString();
        //var name = "flip";

        //SaveIdInMap(Id, Name);
        //SaveIdInMap(id, name);
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
            HardwareId = config.HardwareId;
            
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
        config.HardwareId = HardwareId;
        var json = JsonSerializer.Serialize(config);
        File.WriteAllText(ConfigFilePath, json);
    }
}