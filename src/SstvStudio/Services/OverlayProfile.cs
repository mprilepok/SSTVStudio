// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SstvStudio.Services;

public enum OverlayPosition { Top, Middle, Bottom }

public enum OverlayAlign { Left, Center, Right }

/// <summary>One text line drawn on the outgoing picture. <see cref="Text"/> may contain placeholders.</summary>
public partial class OverlayLine : ObservableObject
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _fontFamily = "Segoe UI";
    /// <summary>Text height as a percentage of the picture height.</summary>
    [ObservableProperty] private decimal _sizePercent = 10;
    /// <summary>Colour as #RRGGBB or #AARRGGBB.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ColorValue))]
    private string _color = "#FFFFFF";
    [ObservableProperty] private bool _bold = true;
    [ObservableProperty] private bool _outline = true;
    [ObservableProperty] private OverlayPosition _position = OverlayPosition.Bottom;
    [ObservableProperty] private OverlayAlign _align = OverlayAlign.Center;

    /// <summary>Avalonia view of <see cref="Color"/> for the colour picker.</summary>
    [JsonIgnore]
    public Avalonia.Media.Color ColorValue
    {
        get => Avalonia.Media.Color.TryParse(Color, out var c) ? c : Avalonia.Media.Colors.White;
        set => Color = value.ToString();
    }

    // Not MemberwiseClone: that would copy the PropertyChanged subscribers too.
    public OverlayLine Clone() => new()
    {
        Text = Text, FontFamily = FontFamily, SizePercent = SizePercent, Color = Color,
        Bold = Bold, Outline = Outline, Position = Position, Align = Align,
    };

    public static OverlayLine Create(string text, OverlayPosition position, string color, decimal size = 10) =>
        new() { Text = text, Position = position, Color = color, SizePercent = size };
}

/// <summary>A named set of overlay lines, e.g. "CQ", "Reply", "73".</summary>
public partial class OverlayProfile : ObservableObject
{
    [ObservableProperty] private string _name = "";

    public ObservableCollection<OverlayLine> Lines { get; set; } = [];

    public OverlayProfile Clone(string name) =>
        new() { Name = name, Lines = new ObservableCollection<OverlayLine>(Lines.Select(l => l.Clone())) };

    public override string ToString() => Name;

    public static IEnumerable<OverlayProfile> Defaults()
    {
        yield return new OverlayProfile
        {
            Name = "CQ",
            Lines =
            [
                OverlayLine.Create("CQ CQ SSTV", OverlayPosition.Top, "#FFFF00", 14),
                OverlayLine.Create("de {MYCALL}", OverlayPosition.Bottom, "#FFFFFF", 12),
                OverlayLine.Create("{MYGRID}", OverlayPosition.Bottom, "#89DCEB", 7),
            ],
        };
        yield return new OverlayProfile
        {
            Name = "Reply",
            Lines =
            [
                OverlayLine.Create("{CALL} de {MYCALL}", OverlayPosition.Top, "#FFFF00", 11),
                OverlayLine.Create("UR {RST}", OverlayPosition.Bottom, "#FFFFFF", 12),
                OverlayLine.Create("{NAME}", OverlayPosition.Bottom, "#A6E3A1", 8),
            ],
        };
        yield return new OverlayProfile
        {
            Name = "73",
            Lines =
            [
                OverlayLine.Create("{CALL} de {MYCALL}", OverlayPosition.Top, "#FFFF00", 10),
                OverlayLine.Create("TNX QSO 73!", OverlayPosition.Middle, "#F38BA8", 16),
                OverlayLine.Create("{DATE} {TIME}", OverlayPosition.Bottom, "#FFFFFF", 7),
            ],
        };
    }
}
