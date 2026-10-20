// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SstvStudio.Core.Decoding;

namespace SstvStudio.Services;

/// <summary>
/// Scrolling waterfall of <see cref="SpectrumAnalyzer"/> rows (newest at the top) with marker lines at the SSTV
/// tones. Each row is scaled against its own median (the noise floor), so a signal stands out whatever the
/// input level.
/// </summary>
public sealed class WaterfallRenderer
{
    public const int Height = 100;
    private const int Width = SpectrumAnalyzer.Columns;
    private const float RangeDb = 35;

    // Marker tones: sync, black, VIS leader, white.
    private static readonly (double Hz, uint Bgra)[] Markers =
    [
        (1200, 0xFFF38BA8), (1500, 0xFF7F849C), (1900, 0xFF89B4FA), (2300, 0xFF7F849C),
    ];

    private readonly byte[] _pixels = new byte[Width * Height * 4];

    public WaterfallRenderer()
    {
        for (var i = 3; i < _pixels.Length; i += 4) _pixels[i] = 255;
    }

    public void AddRow(float[] db)
    {
        // Scroll down one row, then draw the new one at the top.
        Buffer.BlockCopy(_pixels, 0, _pixels, Width * 4, Width * 4 * (Height - 1));
        var floor = db.Order().ElementAt(db.Length / 2);
        for (var x = 0; x < Width; x++)
        {
            var level = Math.Clamp((db[x] - floor) / RangeDb, 0, 1);
            WritePixel(x, 0, Palette(level));
        }
    }

    /// <summary>A new bitmap of the current waterfall (a fresh instance makes the Image control redraw).</summary>
    public WriteableBitmap ToBitmap()
    {
        var bitmap = new WriteableBitmap(new PixelSize(Width, Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var frame = bitmap.Lock();
        var withMarkers = (byte[])_pixels.Clone();
        foreach (var (hz, color) in Markers)
        {
            var x = SpectrumAnalyzer.ColumnOf(hz);
            for (var y = 0; y < Height; y += 2) // dotted
                MemoryMarshal.Write(withMarkers.AsSpan((y * Width + x) * 4), color);
        }
        for (var y = 0; y < Height; y++)
            Marshal.Copy(withMarkers, y * Width * 4, frame.Address + y * frame.RowBytes, Width * 4);
        return bitmap;
    }

    private void WritePixel(int x, int y, uint bgra) => MemoryMarshal.Write(_pixels.AsSpan((y * Width + x) * 4), bgra);

    /// <summary>Dark → blue → yellow → white.</summary>
    private static uint Palette(double t)
    {
        (double R, double G, double B)[] stops = [(17, 17, 27), (30, 102, 245), (249, 226, 175), (255, 255, 255)];
        var p = t * (stops.Length - 1);
        var i = Math.Min((int)p, stops.Length - 2);
        var f = p - i;
        var (r0, g0, b0) = stops[i];
        var (r1, g1, b1) = stops[i + 1];
        var r = (uint)(r0 + (r1 - r0) * f);
        var g = (uint)(g0 + (g1 - g0) * f);
        var b = (uint)(b0 + (b1 - b0) * f);
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }
}
