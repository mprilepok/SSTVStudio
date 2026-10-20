// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Imaging;
using static SstvStudio.Core.Modes.SstvFrequencies;

namespace SstvStudio.Core.Modes;

/// <summary>Martin M1/M2: sync, porch, then G-B-R scans each followed by a separator.</summary>
public sealed class MartinMode(string name, byte visCode, double scanMs) : SstvMode
{
    private const double SyncMs = 4.862;
    private const double PorchMs = 0.572;

    public override string Name => name;
    public override SstvFamily Family => SstvFamily.Martin;
    public override byte VisCode => visCode;
    public override int Width => 320;
    public override int Height => 256;

    public override ScanLayout Layout { get; } = new(
        SyncMs + PorchMs * 4 + scanMs * 3, 1, 0, SyncMs,
        new ScanSegment(ScanChannel.Green, SyncMs + PorchMs, scanMs),
        new ScanSegment(ScanChannel.Blue, SyncMs + PorchMs * 2 + scanMs, scanMs),
        new ScanSegment(ScanChannel.Red, SyncMs + PorchMs * 3 + scanMs * 2, scanMs));

    protected override IEnumerable<Tone> EncodeLines(RgbImage image)
    {
        for (var y = 0; y < Height; y++)
        {
            yield return new Tone(Sync, SyncMs);
            yield return new Tone(Black, PorchMs);
            foreach (var t in Scan(image, y, scanMs, Green)) yield return t;
            yield return new Tone(Black, PorchMs);
            foreach (var t in Scan(image, y, scanMs, Blue)) yield return t;
            yield return new Tone(Black, PorchMs);
            foreach (var t in Scan(image, y, scanMs, Red)) yield return t;
            yield return new Tone(Black, PorchMs);
        }
    }
}
