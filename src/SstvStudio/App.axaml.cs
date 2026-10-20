// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using SstvStudio.ViewModels;
using SstvStudio.Views;

namespace SstvStudio;

public partial class App : Application
{
    private static readonly TimeSpan MinimumSplashTime = TimeSpan.FromMilliseconds(1500);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Show();
            Dispatcher.UIThread.Post(() => _ = StartAsync(desktop, splash), DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Builds the main window behind the splash screen, then swaps them.</summary>
    private static async Task StartAsync(IClassicDesktopStyleApplicationLifetime desktop, SplashWindow splash)
    {
        var clock = Stopwatch.StartNew();
        // Let the splash paint before the (UI-thread) view model work starts.
        await Task.Delay(100);

        splash.Status = "Loading settings, fonts and overlays…";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        var viewModel = new MainWindowViewModel();

        splash.Status = "Opening audio and connecting the rig…";
        var main = new MainWindow { DataContext = viewModel };
        if (clock.Elapsed < MinimumSplashTime) await Task.Delay(MinimumSplashTime - clock.Elapsed);

        desktop.MainWindow = main;
        main.Show();
        splash.Close();
    }
}
