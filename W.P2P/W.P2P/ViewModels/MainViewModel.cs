using System;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using W.P2P.Models;
using static W.P2P.Models.DataModels;

namespace W.P2P.ViewModels;

public partial class MainViewModel : ObservableObject
{
 
    public P2PClient Client { get; set; }
    public ObservableCollection<string> TerminalOutput => AppData.TerminalOutput;
    
    public ObservableCollection<string> ReceivedMessages => AppData.ReceivedMessages;
    public ObservableCollection<string> SentMessages => AppData.SentMessages;
    
    public ObservableCollection<Contact> Contacts => AppData.Config.IdMap;

    [ObservableProperty] 
    private string _messageToSend = "";

    public MainViewModel()
    {
        Client = new P2PClient();
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

    [RelayCommand]
    public void SendMessage()
    {
        var bytes = Encoding.UTF8.GetBytes(MessageToSend);
        Client.SendMessage(bytes);
        
        AppData.SentMessages.Add($"\"{MessageToSend}\" - you");
    }
}