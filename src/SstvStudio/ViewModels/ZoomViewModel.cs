// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.ComponentModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SstvStudio.Services;

namespace SstvStudio.ViewModels;

/// <summary>
/// Zoom for one picture viewer: fitted to the panel, or a fixed 25–400 % where 100 % is one picture pixel per
/// screen pixel. Stored in one setting (0 = fit), so each tab keeps its own zoom across restarts.
/// </summary>
public partial class ZoomViewModel : ObservableObject
{
    public const double MinPercent = 25;
    public const double MaxPercent = 400;

    private readonly AppSettings _settings;
    private readonly string _settingName;
    private readonly Func<double> _get;
    private readonly Action<double> _set;
    private double _lastPercent = 100; // where the slider sits while fitted

    public ZoomViewModel(AppSettings settings, string settingName, Func<double> get, Action<double> set)
    {
        _settings = settings;
        _settingName = settingName;
        _get = get;
        _set = set;
        if (get() > 0) _lastPercent = get();
        settings.PropertyChanged += OnSettingsChanged;
    }

    public bool IsFit
    {
        get => _get() <= 0;
        set => _set(value ? 0 : _lastPercent);
    }

    /// <summary>Slider value. Moving the slider leaves Fit.</summary>
    public double Percent
    {
        get => _get() <= 0 ? _lastPercent : _get();
        set
        {
            _lastPercent = Math.Clamp(value, MinPercent, MaxPercent);
            _set(_lastPercent);
        }
    }

    /// <summary>The zoom next to the slider; blank while fitted (the Fit box says so), keeping its space.</summary>
    public string Label => IsFit ? "" : $"{_get():0} %";

    /// <summary>Applied to the viewer; identity while fitted (the picture is then stretched to the panel).</summary>
    public ITransform Transform => IsFit ? new ScaleTransform(1, 1) : new ScaleTransform(_get() / 100, _get() / 100);

    [RelayCommand]
    private void ActualSize() => Percent = 100;

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != _settingName) return;
        if (_get() > 0) _lastPercent = _get();
        OnPropertyChanged(nameof(IsFit));
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Transform));
    }
}
