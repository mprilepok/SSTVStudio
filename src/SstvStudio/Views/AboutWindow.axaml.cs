// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using SstvStudio.Services;

namespace SstvStudio.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        ProductText.Text = AppInfo.Product;
        VersionText.Text = $"Version {AppInfo.Version}";
        DescriptionText.Text = AppInfo.Description;
        FeaturesText.Text = AppInfo.Features;
        BuildText.Text = $"{AppInfo.Version}, built {AppInfo.BuildDate:yyyy-MM-dd HH:mm}";
        RuntimeText.Text = AppInfo.Runtime;
        AvaloniaText.Text = $"Avalonia {AppInfo.AvaloniaVersion}";
        OsText.Text = AppInfo.OperatingSystem;
        DataText.Text = AppInfo.DataFolder;
        CreditsList.ItemsSource = AppInfo.Credits.Select(c => $"{c.Name} — {c.Purpose} ({c.License})").ToList();
        CopyrightText.Text = $"{AppInfo.Copyright}\n{AppInfo.LicenseNotice}";
    }

    private async void CopyInfo_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(AppInfo.SystemInfo());
            CopiedText.Text = "Copied";
        }
    }

    private void OpenData_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.DataFolder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppInfo.DataFolder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CopiedText.Text = $"Could not open folder: {ex.Message}";
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
