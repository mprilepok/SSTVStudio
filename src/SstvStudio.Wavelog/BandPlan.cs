// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Wavelog;

/// <summary>Maps a frequency to its ADIF band name (IARU edges, generous so regional variants still match).</summary>
public static class BandPlan
{
    private static readonly (string Band, long LowHz, long HighHz)[] Bands =
    [
        ("2190m", 135_700, 137_800),
        ("630m", 472_000, 479_000),
        ("160m", 1_800_000, 2_000_000),
        ("80m", 3_500_000, 4_000_000),
        ("60m", 5_060_000, 5_450_000),
        ("40m", 7_000_000, 7_300_000),
        ("30m", 10_100_000, 10_150_000),
        ("20m", 14_000_000, 14_350_000),
        ("17m", 18_068_000, 18_168_000),
        ("15m", 21_000_000, 21_450_000),
        ("12m", 24_890_000, 24_990_000),
        ("10m", 28_000_000, 29_700_000),
        ("6m", 50_000_000, 54_000_000),
        ("4m", 70_000_000, 71_000_000),
        ("2m", 144_000_000, 148_000_000),
        ("1.25m", 222_000_000, 225_000_000),
        ("70cm", 420_000_000, 450_000_000),
        ("23cm", 1_240_000_000, 1_300_000_000),
        ("13cm", 2_300_000_000, 2_450_000_000),
        ("3cm", 10_000_000_000, 10_500_000_000),
    ];

    public static string? FromFrequency(long hz) =>
        Bands.FirstOrDefault(b => hz >= b.LowHz && hz <= b.HighHz).Band;
}
