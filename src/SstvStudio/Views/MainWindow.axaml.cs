// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using SstvStudio.ViewModels;

namespace SstvStudio.Views;

public partial class MainWindow : Window
{
    private bool _shutdownComplete;

    public MainWindow()
    {
        InitializeComponent();
        Closing += OnClosing;
        AddHandler(TextBox.TextChangedEvent, UpperCaseCallsign);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            vm.TxZoom.PropertyChanged += RemeasureZoomViewers;
            vm.Receive.Zoom.PropertyChanged += RemeasureZoomViewers;
        };
    }

    // LayoutTransformControl keeps its old size when its picture switches between fitted and zoomed (the
    // picture's own measure changes, but the control isn't re-measured), so do it by hand on every zoom change.
    private void RemeasureZoomViewers(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ZoomViewModel.Transform)) return;
        foreach (var viewer in this.GetVisualDescendants().OfType<LayoutTransformControl>())
            viewer.InvalidateMeasure();
    }

    // Text boxes with the "callsign" class show what's typed or pasted in upper case, keeping the caret in place.
    // (The view model upper-cases too, but Avalonia ignores a value pushed back while the binding is writing.)
    private static void UpperCaseCallsign(object? sender, TextChangedEventArgs e)
    {
        if (e.Source is not TextBox { Text: { } text } box || !box.Classes.Contains("callsign")) return;
        var upper = text.ToUpperInvariant();
        if (upper == text) return;
        var caret = box.CaretIndex;
        box.Text = upper;
        box.CaretIndex = caret;
    }

    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    private async void OpenImage_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose picture",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"] },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            Vm.LoadImage(path);
    }

    private async void ExportWav_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export SSTV audio",
            SuggestedFileName = $"sstv-{Vm.SelectedMode.Name.Replace(' ', '-').ToLowerInvariant()}.wav",
            DefaultExtension = "wav",
            FileTypeChoices = [new FilePickerFileType("WAV audio") { Patterns = ["*.wav"] }],
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await Vm.ExportWavAsync(stream);
    }

    private async void DecodeWav_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Decode SSTV recording",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("WAV audio") { Patterns = ["*.wav"] }, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            await Vm.Receive.DecodeFileAsync(path);
    }

    private async void SaveReceived_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm.Receive.Selected is not { } picture) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save received picture",
            SuggestedFileName = $"{picture.StartedUtc:yyyyMMdd_HHmmss}Z_{picture.Mode.Name.Replace(' ', '-')}.png",
            DefaultExtension = "png",
            FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }],
        });
        if (file?.TryGetLocalPath() is { } path)
            Vm.Receive.SavePicture(picture, path);
    }

    private async void SaveReceivedAudio_Click(object? sender, RoutedEventArgs e)
    {
        if (Vm.Receive.Selected is not { } picture) return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save received audio",
            SuggestedFileName = $"{picture.StartedUtc:yyyyMMdd_HHmmss}Z_{picture.Mode.Name.Replace(' ', '-')}.wav",
            DefaultExtension = "wav",
            FileTypeChoices = [new FilePickerFileType("WAV audio") { Patterns = ["*.wav"] }],
        });
        if (file?.TryGetLocalPath() is { } path)
            Vm.Receive.SaveAudio(picture, path);
    }

    private static readonly FilePickerFileType SettingsFileType = new("SSTV Studio settings") { Patterns = ["*.json"] };
    private static readonly FilePickerFileType AdifFileType = new("ADIF log") { Patterns = ["*.adi"] };

    private async void ExportSettings_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export settings",
            SuggestedFileName = $"sstvstudio-settings-{DateTime.Now:yyyyMMdd}.json",
            DefaultExtension = "json",
            FileTypeChoices = [SettingsFileType],
        });
        if (file?.TryGetLocalPath() is { } path)
            Vm.ExportSettings(path);
    }

    private async void ImportSettings_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import settings",
            AllowMultiple = false,
            FileTypeFilter = [SettingsFileType, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            Vm.ImportSettings(path);
    }

    private async void ExportLog_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export log",
            SuggestedFileName = $"sstvstudio-log-{DateTime.Now:yyyyMMdd}.adi",
            DefaultExtension = "adi",
            FileTypeChoices = [AdifFileType],
        });
        if (file?.TryGetLocalPath() is { } path)
            Vm.ExportLog(path);
    }

    private async void About_Click(object? sender, RoutedEventArgs e) => await new AboutWindow().ShowDialog(this);

    // Defer closing until any transmission is stopped and PTT is released.
    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdownComplete) return;
        e.Cancel = true;
        await Vm.DisposeAsync();
        _shutdownComplete = true;
        Close();
    }
}
