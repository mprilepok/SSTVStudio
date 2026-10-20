// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SstvStudio.Core.Modes;
using SstvStudio.Services;
using SstvStudio.Wavelog;

namespace SstvStudio.ViewModels;

/// <summary>
/// The QSO being worked. Shared by the Receive and Transmit tabs, so a callsign typed while their picture comes in
/// is the same one the transmit overlay (<c>{CALL}</c>) and the log use.
/// </summary>
public partial class QsoViewModel(QsoLogger logger, AppSettings settings, Func<SstvMode> currentMode) : ObservableObject
{
    private static readonly IBrush GreenBrush = new SolidColorBrush(Color.Parse("#A6E3A1"));
    private static readonly IBrush RedBrush = new SolidColorBrush(Color.Parse("#F38BA8"));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#6C7086"));

    [ObservableProperty] private string _call = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _grid = "";
    [ObservableProperty] private string _rstSent = "595";
    [ObservableProperty] private string _rstRcvd = "595";
    [ObservableProperty] private string _comment = "";
    [ObservableProperty] private string _frequencyMHz = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartText))]
    private DateTime? _startUtc;
    [ObservableProperty] private string _logStatus = "";
    [ObservableProperty] private IBrush _logStatusBrush = DimBrush;

    public ObservableCollection<string> Recent { get; } = [];

    public string StartText => StartUtc is { } t ? $"QSO started {t:HH:mm}Z" : "Starts when a callsign is entered";

    // The QSO starts when the other station's call is first entered (usually right after their picture), or at
    // the first transmission if that comes earlier.
    partial void OnCallChanged(string value)
    {
        // Callsigns are always upper case; same length, so the caret stays where it was.
        if (value != value.ToUpperInvariant())
        {
            Call = value.ToUpperInvariant();
            return;
        }
        if (StartUtc is null && !string.IsNullOrWhiteSpace(value)) StartUtc = DateTime.UtcNow;
    }

    /// <summary>Marks the QSO as started (if it wasn't already), e.g. when we transmit.</summary>
    public void EnsureStarted() => StartUtc ??= DateTime.UtcNow;

    [RelayCommand]
    private async Task LogAsync()
    {
        if (string.IsNullOrWhiteSpace(Call))
        {
            SetStatus(false, "Enter the other station's callsign first.");
            return;
        }

        var freqHz = double.TryParse(FrequencyMHz, NumberStyles.Float, CultureInfo.InvariantCulture, out var mhz)
            ? (long)Math.Round(mhz * 1_000_000)
            : 0;
        var qso = new QsoRecord
        {
            Call = Call.Trim(),
            StartUtc = StartUtc ?? DateTime.UtcNow,
            EndUtc = DateTime.UtcNow,
            FrequencyHz = freqHz,
            SstvMode = currentMode().Name,
            RstSent = RstSent.Trim(),
            RstRcvd = RstRcvd.Trim(),
            Name = Name,
            Gridsquare = Grid,
            Comment = Comment,
            StationCallsign = settings.MyCall,
            MyGridsquare = settings.MyGrid,
            TxPowerW = settings.TxPowerW,
        };

        SetStatus(true, "Logging…");
        var result = await logger.LogAsync(qso, settings);
        SetStatus(result.Success, result.Message);
        Recent.Insert(0, $"{qso.StartUtc:HH:mm}Z  {qso.Call.ToUpperInvariant(),-10} {BandPlan.FromFrequency(freqHz) ?? "?",-5} {qso.SstvMode,-14} {qso.RstSent}/{qso.RstRcvd}  {(result.Success ? "✓" : "✗ local only")}");
        if (result.Success) Clear();
    }

    [RelayCommand]
    private void Clear()
    {
        Call = "";
        Name = "";
        Grid = "";
        Comment = "";
        RstSent = "595";
        RstRcvd = "595";
        StartUtc = null;
    }

    private void SetStatus(bool ok, string message)
    {
        LogStatus = message;
        LogStatusBrush = ok ? GreenBrush : RedBrush;
    }
}
