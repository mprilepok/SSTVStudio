// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Modes;

public static class SstvModes
{
    public static SstvMode MartinM1 { get; } = new MartinMode("Martin M1", 44, 146.432);
    public static SstvMode MartinM2 { get; } = new MartinMode("Martin M2", 40, 73.216);
    public static SstvMode ScottieS1 { get; } = new ScottieMode("Scottie S1", 60, 138.240);
    public static SstvMode ScottieS2 { get; } = new ScottieMode("Scottie S2", 56, 88.064);
    public static SstvMode ScottieDx { get; } = new ScottieMode("Scottie DX", 76, 345.6);
    public static SstvMode Robot36 => RobotMode.Robot36;
    public static SstvMode Robot72 => RobotMode.Robot72;
    public static SstvMode Pd50 { get; } = new PdMode("PD 50", 93, 320, 256, 91.520);
    public static SstvMode Pd90 { get; } = new PdMode("PD 90", 99, 320, 256, 170.240);
    public static SstvMode Pd120 { get; } = new PdMode("PD 120", 95, 640, 496, 121.600);
    public static SstvMode Pd160 { get; } = new PdMode("PD 160", 98, 512, 400, 195.584);
    public static SstvMode Pd180 { get; } = new PdMode("PD 180", 96, 640, 496, 183.040);
    public static SstvMode Pd240 { get; } = new PdMode("PD 240", 97, 640, 496, 244.480);
    public static SstvMode Pd290 { get; } = new PdMode("PD 290", 94, 800, 616, 228.800);
    public static SstvMode WraaseSc2180 { get; } = new WraaseMode();

    public static IReadOnlyList<SstvMode> All { get; } =
    [
        MartinM1, MartinM2, ScottieS1, ScottieS2, ScottieDx, Robot36, Robot72,
        Pd50, Pd90, Pd120, Pd160, Pd180, Pd240, Pd290, WraaseSc2180,
    ];

    public static SstvMode? Find(string? name) =>
        All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
