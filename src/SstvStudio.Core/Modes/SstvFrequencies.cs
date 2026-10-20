// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Modes;

public static class SstvFrequencies
{
    public const double Sync = 1200;
    public const double Black = 1500;
    public const double White = 2300;
    public const double Leader = 1900;
    public const double VisOne = 1100;
    public const double VisZero = 1300;

    /// <summary>Maps a 0–255 luminance/colour value onto the 1500–2300 Hz video band.</summary>
    public static double FromValue(byte value) => Black + (White - Black) * value / 255.0;
}
