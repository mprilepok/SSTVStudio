// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Audio;

/// <summary>A single constant-frequency segment. A frequency of 0 means silence.</summary>
public readonly record struct Tone(double FrequencyHz, double DurationMs)
{
    public static Tone Silence(double durationMs) => new(0, durationMs);
}
