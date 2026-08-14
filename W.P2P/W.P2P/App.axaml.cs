using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using W.P2P.Models;
using W.P2P.ViewModels;
using W.P2P.Views;

namespace W.P2P;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            desktop.Exit += OnExit;
        }
        
        AppData.Config.LoadConfig();

        base.OnFrameworkInitializationCompleted();
    }

    private void OnExit(object sender, EventArgs e)
    {
        AppData.Config.SaveConfig();
    }
}

