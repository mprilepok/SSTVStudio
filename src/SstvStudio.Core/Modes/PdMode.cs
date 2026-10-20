// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Imaging;
using static SstvStudio.Core.Modes.SstvFrequencies;

namespace SstvStudio.Core.Modes;

/// <summary>PD modes transmit two image lines per sync: Y(even), R-Y, B-Y (both averaged), Y(odd).</summary>
public sealed class PdMode(string name, byte visCode, int width, int height, double componentMs) : SstvMode
{
    private const double SyncMs = 20.0;
    private const double PorchMs = 2.08;

    public override string Name => name;
    public override SstvFamily Family => SstvFamily.Pd;
    public override byte VisCode => visCode;
    public override int Width => width;
    public override int Height => height;

    public override ScanLayout Layout { get; } = new(
        SyncMs + PorchMs + componentMs * 4, 2, 0, SyncMs,
        new ScanSegment(ScanChannel.Y, SyncMs + PorchMs, componentMs),
        new ScanSegment(ScanChannel.RY, SyncMs + PorchMs + componentMs, componentMs),
        new ScanSegment(ScanChannel.BY, SyncMs + PorchMs + componentMs * 2, componentMs),
        new ScanSegment(ScanChannel.Y2, SyncMs + PorchMs + componentMs * 3, componentMs));

    protected override IEnumerable<Tone> EncodeLines(RgbImage image)
    {
        for (var y = 0; y < Height; y += 2)
        {
            yield return new Tone(Sync, SyncMs);
            yield return new Tone(Black, PorchMs);
            foreach (var t in Scan(image, y, componentMs, ColorSpace.Y)) yield return t;
            foreach (var t in AveragedScan(image, y, y + 1, componentMs, ColorSpace.RY)) yield return t;
            foreach (var t in AveragedScan(image, y, y + 1, componentMs, ColorSpace.BY)) yield return t;
            foreach (var t in Scan(image, y + 1, componentMs, ColorSpace.Y)) yield return t;
        }
    }
}
