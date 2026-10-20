// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.ComponentModel;
using System.Globalization;
using Avalonia.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using SstvStudio.Services;

namespace SstvStudio.ViewModels;

/// <summary>
/// A number setting as TextBox text. Text that isn't a number in range is not applied: the setter throws, and
/// Avalonia shows the allowed range under the field ("Whole number, 1 to 65535"), so a typo is never silently
/// ignored. Accepts "1.5" and "1,5".
/// </summary>
public sealed class NumberField : ObservableObject
{
    private readonly string _settingName;
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private readonly double _min;
    private readonly double _max;
    private readonly bool _whole;

    public NumberField(AppSettings settings, string settingName, Func<double> get, Action<double> set,
        double min, double max, bool whole)
    {
        (_settingName, _get, _set, _min, _max, _whole) = (settingName, get, set, min, max, whole);
        settings.PropertyChanged += OnSettingsChanged;
    }

    public string Text
    {
        get => _get().ToString(CultureInfo.CurrentCulture);
        set
        {
            var text = value.Trim().Replace(',', '.');
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || number < _min || number > _max || (_whole && number != Math.Floor(number)))
                throw new DataValidationException(_whole
                    ? $"Whole number, {Format(_min)} to {Format(_max)}"
                    : $"{Format(_min)} to {Format(_max)}");
            _set(number);
        }
    }

    private static string Format(double v) => v.ToString(CultureInfo.CurrentCulture);

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == _settingName) OnPropertyChanged(nameof(Text));
    }
}

/// <summary>The number fields of the Settings tab, each with its allowed range.</summary>
public sealed class SettingsNumbers(AppSettings s)
{
    public NumberField TxPower { get; } = new(s, nameof(s.TxPowerW), () => s.TxPowerW, v => s.TxPowerW = v, 0, 2000, whole: false);
    public NumberField CwWpm { get; } = new(s, nameof(s.CwWpm), () => s.CwWpm, v => s.CwWpm = (int)v, 5, 60, whole: true);
    public NumberField RigPort { get; } = new(s, nameof(s.RigPort), () => s.RigPort, v => s.RigPort = (int)v, 1, 65535, whole: true);
    public NumberField PttLead { get; } = new(s, nameof(s.PttLeadMs), () => s.PttLeadMs, v => s.PttLeadMs = (int)v, 0, 2000, whole: true);
    public NumberField PttTail { get; } = new(s, nameof(s.PttTailMs), () => s.PttTailMs, v => s.PttTailMs = (int)v, 0, 2000, whole: true);
    public NumberField Watchdog { get; } = new(s, nameof(s.MaxTxSeconds), () => s.MaxTxSeconds, v => s.MaxTxSeconds = (int)v, 30, 1200, whole: true);
    public NumberField SwrTrip { get; } = new(s, nameof(s.SwrTripLevel), () => s.SwrTripLevel, v => s.SwrTripLevel = v, 0, 10, whole: false);
    public NumberField ClockPpm { get; } = new(s, nameof(s.ClockPpm), () => s.ClockPpm, v => s.ClockPpm = v, -5000, 5000, whole: false);
}
