// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Modes;

public enum ScanChannel
{
    Red, Green, Blue,
    /// <summary>Luma of the first line of the period.</summary>
    Y,
    /// <summary>Luma of the second line of the period (PD modes).</summary>
    Y2,
    RY, BY,
    /// <summary>R-Y or B-Y depending on the line (Robot 36); see <see cref="ScanLayout.ChromaSeparator"/>.</summary>
    Chroma,
}

/// <summary>A scanned channel; <paramref name="StartMs"/> is relative to the start of the period's sync pulse.</summary>
public sealed record ScanSegment(ScanChannel Channel, double StartMs, double DurationMs);

/// <summary>
/// Receiver's view of a mode: one sync pulse per period, each period carrying <see cref="LinesPerPeriod"/> lines.
/// </summary>
public sealed class ScanLayout(double periodMs, int linesPerPeriod, double firstSyncMs, double syncMs, params ScanSegment[] segments)
{
    public double PeriodMs { get; } = periodMs;
    public int LinesPerPeriod { get; } = linesPerPeriod;
    /// <summary>Offset from the end of the VIS header to the start of the first period's sync pulse.</summary>
    public double FirstSyncMs { get; } = firstSyncMs;
    public double SyncMs { get; } = syncMs;
    public IReadOnlyList<ScanSegment> Segments { get; } = segments;
    /// <summary>Robot 36 separator after the luma scan: ~1500 Hz before R-Y, ~2300 Hz before B-Y.</summary>
    public (double StartMs, double EndMs)? ChromaSeparator { get; init; }

    public bool IsYCrCb => Segments.Any(s => s.Channel is ScanChannel.Y or ScanChannel.Y2);
    public double MaxEndMs => Segments.Max(s => s.StartMs + s.DurationMs);
}
