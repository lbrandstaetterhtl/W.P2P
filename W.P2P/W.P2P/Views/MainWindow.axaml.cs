using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using W.P2P.Models;
using W.P2P.ViewModels;

namespace W.P2P.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    public MainWindow()
    {
        InitializeComponent();
        _vm = DataContext as MainViewModel;

        KeyBinding keyBinding = new();
        keyBinding.Gesture = new KeyGesture(Key.Enter);
        keyBinding.Command = _vm.SendMessageCommand;
        ChatInput.KeyBindings.Add(keyBinding);
    }

    public void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: DataModels.Contact contact})
        {
            _vm.Connect(contact);
        }
    }
}