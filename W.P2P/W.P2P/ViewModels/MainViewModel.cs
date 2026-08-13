using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using W.P2P.Models;
using static W.P2P.Models.DataModels;

namespace W.P2P.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly P2PClient Client = new();
    
    public ObservableCollection<string> TerminalOutput => AppData.TerminalOutput;
    
    public ObservableCollection<Contact> Contacts => AppData.Config.IdMap;

    public MainViewModel()
    {
        
    }

    public void Connect(Contact contact)
    {
        var result = Client.Connect(contact);
        
        if (!result)
        {
            AppData.TerminalOutput.Add("Failed to connect to the selected contact.");
            Client.Connection.TargetId = "";
            Client.Connection.ConnectionId = "";
            Client.Connection.IsConnected = false;
            Client.Connection.SharedKey = [];
        }
    }
    
    [RelayCommand]
    private void Disconnect()
    {
        var result = Client.Disconnect();

        if (!result)
        {
            AppData.TerminalOutput.Add("Failed to disconnect the selected contact.");
        }
    }
}