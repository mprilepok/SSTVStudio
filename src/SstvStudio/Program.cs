// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using Avalonia;
using SstvStudio.Services;

namespace SstvStudio;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Last-chance hooks; UI-thread errors are caught (and survived) in App.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            ErrorLog.Write("unhandled", e.ExceptionObject as Exception ?? new Exception($"{e.ExceptionObject}"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            ErrorLog.Write("background task", e.Exception);
            e.SetObserved();
        };
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
