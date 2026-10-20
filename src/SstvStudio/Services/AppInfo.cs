// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Reflection;
using System.Runtime.InteropServices;
using SstvStudio.Core.Modes;

namespace SstvStudio.Services;

/// <summary>Product, version and environment details for the splash screen and About window.</summary>
public static class AppInfo
{
    private static readonly Assembly App = typeof(AppInfo).Assembly;

    public static string Product => App.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "SSTV Studio";
    public static string Description => App.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description ?? "";
    public static string Copyright => App.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    /// <summary>GPL notice shown in the About window, as the GPL asks of interactive programs.</summary>
    public const string LicenseNotice =
        "Free software under the GNU GPL v3.0 or later. It comes with ABSOLUTELY NO WARRANTY; see LICENSE for details.";

    /// <summary>Informational version without the "+commit" suffix the SDK may append.</summary>
    public static string Version =>
        (App.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? App.GetName().Version?.ToString() ?? "?").Split('+')[0];

    public static DateTime BuildDate => File.GetLastWriteTime(App.Location is { Length: > 0 } path ? path : AppContext.BaseDirectory);

    public static string Runtime => RuntimeInformation.FrameworkDescription;
    public static string OperatingSystem => $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
    public static string AvaloniaVersion => typeof(Avalonia.Application).Assembly.GetName().Version?.ToString(3) ?? "?";
    public static string DataFolder => AppSettings.DataDirectory;

    public static string Features =>
        $"{SstvModes.All.Count} SSTV modes for transmit and receive · Hamlib rigctld · Wavelog";

    public static IReadOnlyList<(string Name, string Purpose, string License)> Credits { get; } =
    [
        ("Avalonia UI", "Cross-platform user interface", "MIT"),
        ("SkiaSharp", "Picture scaling, overlay text, PNG", "MIT"),
        ("NAudio", "Sound card input and output", "MIT"),
        ("CommunityToolkit.Mvvm", "View-model plumbing", "MIT"),
        ("Inter", "User interface font", "OFL-1.1"),
        // Used over the network only (not bundled), so their own licences don't apply to this program.
        ("Hamlib rigctld", "Rig control, over its TCP protocol", "separate program"),
        ("Wavelog", "Logbook, over its REST API", "separate service"),
    ];

    /// <summary>Plain-text summary to paste into a bug report.</summary>
    public static string SystemInfo() =>
        $"""
        {Product} {Version} (built {BuildDate:yyyy-MM-dd HH:mm})
        Runtime: {Runtime}, Avalonia {AvaloniaVersion}
        OS: {OperatingSystem}
        Data folder: {DataFolder}
        """;
}
