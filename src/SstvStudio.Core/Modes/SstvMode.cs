// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Imaging;

namespace SstvStudio.Core.Modes;

public enum SstvFamily { Martin, Scottie, Robot, Pd, Wraase }

public abstract class SstvMode
{
    private double? _imageDurationMs;

    public abstract string Name { get; }
    public abstract SstvFamily Family { get; }
    /// <summary>7-bit VIS code (parity is added by the encoder).</summary>
    public abstract byte VisCode { get; }
    public abstract int Width { get; }
    public abstract int Height { get; }
    /// <summary>Timing of one sync period as seen by the decoder.</summary>
    public abstract ScanLayout Layout { get; }

    /// <summary>Duration of the image portion only (no VIS, preamble or CW ID).</summary>
    public double ImageDurationMs =>
        _imageDurationMs ??= EncodeImage(RgbImage.Blank(Width, Height)).Sum(t => t.DurationMs);

    public string Description => $"{Width}×{Height} · {ImageDurationMs / 1000:0} s";

    /// <summary>Encodes the picture. The image must already be exactly <see cref="Width"/>×<see cref="Height"/>.</summary>
    public IEnumerable<Tone> EncodeImage(RgbImage image)
    {
        if (image.Width != Width || image.Height != Height)
            throw new ArgumentException($"{Name} needs a {Width}×{Height} image, got {image.Width}×{image.Height}.");
        return EncodeLines(image);
    }

    protected abstract IEnumerable<Tone> EncodeLines(RgbImage image);

    protected static IEnumerable<Tone> Scan(RgbImage image, int y, double scanMs, Func<byte, byte, byte, byte> channel)
    {
        var pixelMs = scanMs / image.Width;
        for (var x = 0; x < image.Width; x++)
        {
            var (r, g, b) = image[x, y];
            yield return new Tone(SstvFrequencies.FromValue(channel(r, g, b)), pixelMs);
        }
    }

    /// <summary>Scans a chroma channel averaged over two image lines (Robot 36 and PD modes).</summary>
    protected static IEnumerable<Tone> AveragedScan(RgbImage image, int y0, int y1, double scanMs, Func<byte, byte, byte, byte> channel)
    {
        var pixelMs = scanMs / image.Width;
        for (var x = 0; x < image.Width; x++)
        {
            var (r0, g0, b0) = image[x, y0];
            var (r1, g1, b1) = image[x, y1];
            var v = (channel(r0, g0, b0) + channel(r1, g1, b1) + 1) / 2;
            yield return new Tone(SstvFrequencies.FromValue((byte)v), pixelMs);
        }
    }

    protected static byte Red(byte r, byte g, byte b) => r;
    protected static byte Green(byte r, byte g, byte b) => g;
    protected static byte Blue(byte r, byte g, byte b) => b;

    public override string ToString() => Name;
}
