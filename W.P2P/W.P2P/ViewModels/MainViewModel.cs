using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using W.P2P.Models;
using static W.P2P.Models.DataModels;

namespace W.P2P.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly P2PClient Client = new();
    
    private ObservableCollection<string> TerminalOutput => AppData.TerminalOutput;
    
    public ObservableCollection<Contact> Contacts => AppData.Config.IdMap;
    
    public string TerminalOutputText => string.Join(Environment.NewLine, TerminalOutput);

    public MainViewModel()
    {
        AppData.TerminalOutput.CollectionChanged += (_, _) =>
            OnPropertyChanged(nameof(TerminalOutputText));
    }

    public void Connect(Contact contact)
    {
        var result = Client.Connect(contact, AppData.Config.Id);
        
        if (!result)
        {
            AppData.TerminalOutput.Add("Failed to connect to the selected contact.");
            Client.Connection = new Connection();
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