// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Wavelog;

public sealed record QsoRecord
{
    public required string Call { get; init; }
    public required DateTime StartUtc { get; init; }
    public DateTime? EndUtc { get; init; }
    public long FrequencyHz { get; init; }
    public string Mode { get; init; } = "SSTV";
    /// <summary>SSTV sub-mode such as "Martin M1"; ADIF has no SSTV submodes so it goes into the comment.</summary>
    public string? SstvMode { get; init; }
    public string RstSent { get; init; } = "595";
    public string RstRcvd { get; init; } = "595";
    public string? Name { get; init; }
    public string? Qth { get; init; }
    public string? Gridsquare { get; init; }
    public string? Comment { get; init; }
    public string? StationCallsign { get; init; }
    public string? MyGridsquare { get; init; }
    public double? TxPowerW { get; init; }
}
