// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Encoding;

/// <param name="SendPreamble">MMSSTV-style 800 ms calibration tones before the VIS header.</param>
/// <param name="CwId">Callsign sent in Morse after the picture; null or empty disables it.</param>
public sealed record TransmitOptions(
    bool SendPreamble = true,
    string? CwId = null,
    int CwWpm = 20,
    double CwToneHz = 1900);
