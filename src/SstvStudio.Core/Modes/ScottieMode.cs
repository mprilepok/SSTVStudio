// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Imaging;
using static SstvStudio.Core.Modes.SstvFrequencies;

namespace SstvStudio.Core.Modes;

/// <summary>Scottie S1/S2/DX: sync sits between the blue and red scans; one extra sync starts the frame.</summary>
public sealed class ScottieMode(string name, byte visCode, double scanMs) : SstvMode
{
    private const double SyncMs = 9.0;
    private const double SeparatorMs = 1.5;

    public override string Name => name;
    public override SstvFamily Family => SstvFamily.Scottie;
    public override byte VisCode => visCode;
    public override int Width => 320;
    public override int Height => 256;

    // Green and blue of a line precede its sync pulse, hence the negative offsets.
    public override ScanLayout Layout { get; } = new(
        SyncMs + SeparatorMs * 3 + scanMs * 3, 1, SyncMs + SeparatorMs * 2 + scanMs * 2, SyncMs,
        new ScanSegment(ScanChannel.Green, -(SeparatorMs + scanMs * 2), scanMs),
        new ScanSegment(ScanChannel.Blue, -scanMs, scanMs),
        new ScanSegment(ScanChannel.Red, SyncMs + SeparatorMs, scanMs));

    protected override IEnumerable<Tone> EncodeLines(RgbImage image)
    {
        yield return new Tone(Sync, SyncMs);
        for (var y = 0; y < Height; y++)
        {
            yield return new Tone(Black, SeparatorMs);
            foreach (var t in Scan(image, y, scanMs, Green)) yield return t;
            yield return new Tone(Black, SeparatorMs);
            foreach (var t in Scan(image, y, scanMs, Blue)) yield return t;
            yield return new Tone(Sync, SyncMs);
            yield return new Tone(Black, SeparatorMs);
            foreach (var t in Scan(image, y, scanMs, Red)) yield return t;
        }
    }
}
