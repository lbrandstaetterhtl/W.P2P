using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        
        _vm.TerminalOutput.CollectionChanged += (sender, args) =>
        {
            var offset = TerminalScroller.Offset;
            var extent = TerminalScroller.Extent;
            var viewport = TerminalScroller.Viewport;
    
            bool isAtBottom = offset.Y + viewport.Height >= extent.Height - 10;
    
            if (isAtBottom)
            {
                Dispatcher.UIThread.Post(() => TerminalScroller.ScrollToEnd(), 
                    DispatcherPriority.Background);
            }
        };
    }

    public void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: DataModels.Contact contact})
        {
            _vm.Connect(contact);
        }
    }
}