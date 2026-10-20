// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Imaging;
using static SstvStudio.Core.Modes.SstvFrequencies;

namespace SstvStudio.Core.Modes;

/// <summary>Wraase SC2-180: sync, porch, then R-G-B scans.</summary>
public sealed class WraaseMode : SstvMode
{
    private const double SyncMs = 5.5225;
    private const double PorchMs = 0.5;
    private const double ScanMs = 235.0;

    public override string Name => "Wraase SC2-180";
    public override SstvFamily Family => SstvFamily.Wraase;
    public override byte VisCode => 55;
    public override int Width => 320;
    public override int Height => 256;

    public override ScanLayout Layout { get; } = new(
        SyncMs + PorchMs + ScanMs * 3, 1, 0, SyncMs,
        new ScanSegment(ScanChannel.Red, SyncMs + PorchMs, ScanMs),
        new ScanSegment(ScanChannel.Green, SyncMs + PorchMs + ScanMs, ScanMs),
        new ScanSegment(ScanChannel.Blue, SyncMs + PorchMs + ScanMs * 2, ScanMs));

    protected override IEnumerable<Tone> EncodeLines(RgbImage image)
    {
        for (var y = 0; y < Height; y++)
        {
            yield return new Tone(Sync, SyncMs);
            yield return new Tone(Black, PorchMs);
            foreach (var t in Scan(image, y, ScanMs, Red)) yield return t;
            foreach (var t in Scan(image, y, ScanMs, Green)) yield return t;
            foreach (var t in Scan(image, y, ScanMs, Blue)) yield return t;
        }
    }
}
