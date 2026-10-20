// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Modes;

/// <summary>RGB → Y / R-Y / B-Y as used by MMSSTV and QSSTV for Robot and PD modes.</summary>
public static class ColorSpace
{
    public static byte Y(byte r, byte g, byte b) =>
        Clamp(16.0 + (65.738 * r + 129.057 * g + 25.064 * b) / 256.0);

    public static byte RY(byte r, byte g, byte b) =>
        Clamp(128.0 + (112.439 * r - 94.154 * g - 18.285 * b) / 256.0);

    public static byte BY(byte r, byte g, byte b) =>
        Clamp(128.0 + (-37.945 * r - 74.494 * g + 112.439 * b) / 256.0);

    private static byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
}
