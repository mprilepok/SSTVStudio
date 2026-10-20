// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Imaging;
using static SstvStudio.Core.Modes.SstvFrequencies;

namespace SstvStudio.Core.Modes;

/// <summary>
/// Robot 36 sends Y plus alternating R-Y (even lines) / B-Y (odd lines), chroma averaged over the line pair.
/// Robot 72 sends Y, R-Y and B-Y on every line.
/// </summary>
public sealed class RobotMode : SstvMode
{
    private const double SyncMs = 9.0;
    private const double SyncPorchMs = 3.0;
    private const double SeparatorMs = 4.5;
    private const double PorchMs = 1.5;

    private readonly bool _isRobot72;

    private RobotMode(string name, byte visCode, bool isRobot72)
    {
        Name = name;
        VisCode = visCode;
        _isRobot72 = isRobot72;
        const double lumaStart = SyncMs + SyncPorchMs;
        Layout = isRobot72
            ? new ScanLayout(300, 1, 0, SyncMs,
                new ScanSegment(ScanChannel.Y, lumaStart, 138),
                new ScanSegment(ScanChannel.RY, lumaStart + 138 + SeparatorMs + PorchMs, 69),
                new ScanSegment(ScanChannel.BY, lumaStart + 138 + 69 + (SeparatorMs + PorchMs) * 2, 69))
            : new ScanLayout(150, 1, 0, SyncMs,
                new ScanSegment(ScanChannel.Y, lumaStart, 88),
                new ScanSegment(ScanChannel.Chroma, lumaStart + 88 + SeparatorMs + PorchMs, 44))
            {
                ChromaSeparator = (lumaStart + 88 + 0.5, lumaStart + 88 + SeparatorMs - 0.5),
            };
    }

    public override ScanLayout Layout { get; }

    public static RobotMode Robot36 { get; } = new("Robot 36", 8, false);
    public static RobotMode Robot72 { get; } = new("Robot 72", 12, true);

    public override string Name { get; }
    public override SstvFamily Family => SstvFamily.Robot;
    public override byte VisCode { get; }
    public override int Width => 320;
    public override int Height => 240;

    private double LumaMs => _isRobot72 ? 138.0 : 88.0;
    private double ChromaMs => _isRobot72 ? 69.0 : 44.0;

    protected override IEnumerable<Tone> EncodeLines(RgbImage image)
    {
        for (var y = 0; y < Height; y++)
        {
            yield return new Tone(Sync, SyncMs);
            yield return new Tone(Black, SyncPorchMs);
            foreach (var t in Scan(image, y, LumaMs, ColorSpace.Y)) yield return t;

            if (_isRobot72)
            {
                yield return new Tone(Black, SeparatorMs);
                yield return new Tone(Leader, PorchMs);
                foreach (var t in Scan(image, y, ChromaMs, ColorSpace.RY)) yield return t;
                yield return new Tone(White, SeparatorMs);
                yield return new Tone(Leader, PorchMs);
                foreach (var t in Scan(image, y, ChromaMs, ColorSpace.BY)) yield return t;
            }
            else
            {
                // Even lines carry R-Y (1500 Hz separator), odd lines B-Y (2300 Hz separator).
                var even = y % 2 == 0;
                var pairY = even ? Math.Min(y + 1, Height - 1) : y - 1;
                yield return new Tone(even ? Black : White, SeparatorMs);
                yield return new Tone(Leader, PorchMs);
                foreach (var t in AveragedScan(image, y, pairY, ChromaMs, even ? ColorSpace.RY : ColorSpace.BY))
                    yield return t;
            }
        }
    }
}
