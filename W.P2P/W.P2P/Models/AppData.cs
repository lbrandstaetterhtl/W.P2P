using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using static W.P2P.Models.DataModels;

namespace W.P2P.Models;

public class AppData : ObservableObject
{
    public static readonly Config Config = new();
    
    public static ObservableCollection<string> TerminalOutput = new();

    public static ObservableCollection<string> SentMessages = new();
    public static ObservableCollection<string> ReceivedMessages = new();
}