// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Imaging;

/// <summary>Packed 24-bit RGB image (row-major, 3 bytes per pixel).</summary>
public sealed class RgbImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public RgbImage(int width, int height, byte[]? data = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        data ??= new byte[width * height * 3];
        if (data.Length != width * height * 3)
            throw new ArgumentException($"Expected {width * height * 3} bytes, got {data.Length}.", nameof(data));
        Width = width;
        Height = height;
        Data = data;
    }

    public static RgbImage Blank(int width, int height) => new(width, height);

    public (byte R, byte G, byte B) this[int x, int y]
    {
        get
        {
            var i = (y * Width + x) * 3;
            return (Data[i], Data[i + 1], Data[i + 2]);
        }
        set
        {
            var i = (y * Width + x) * 3;
            Data[i] = value.R;
            Data[i + 1] = value.G;
            Data[i + 2] = value.B;
        }
    }
}
