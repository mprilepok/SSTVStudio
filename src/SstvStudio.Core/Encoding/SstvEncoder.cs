// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Numerics;
using SstvStudio.Core.Audio;
using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;
using static SstvStudio.Core.Modes.SstvFrequencies;

namespace SstvStudio.Core.Encoding;

/// <summary>Builds the complete tone sequence for a transmission: preamble, VIS header, picture, CW ID.</summary>
public static class SstvEncoder
{
    private const double CwGapMs = 500;

    public static IEnumerable<Tone> Encode(SstvMode mode, RgbImage image, TransmitOptions options)
    {
        // Validate eagerly so a size mismatch fails before PTT is keyed.
        var picture = mode.EncodeImage(image);
        return Concat(options.SendPreamble ? Preamble() : [], VisHeader(mode.VisCode), picture, CwTail(options));
    }

    public static double DurationMs(SstvMode mode, TransmitOptions options) =>
        (options.SendPreamble ? Preamble().Sum(t => t.DurationMs) : 0)
        + VisHeader(mode.VisCode).Sum(t => t.DurationMs)
        + mode.ImageDurationMs
        + CwTail(options).Sum(t => t.DurationMs);

    public static IEnumerable<Tone> Preamble()
    {
        foreach (var f in new[] { 1900.0, 1500, 1900, 1500, 2300, 1500, 2300, 1500 })
            yield return new Tone(f, 100);
    }

    /// <summary>Leader, break, leader, start bit, 7 data bits LSB first, even parity bit, stop bit.</summary>
    public static IEnumerable<Tone> VisHeader(byte visCode)
    {
        if (visCode > 0x7F) throw new ArgumentOutOfRangeException(nameof(visCode), "VIS codes are 7 bits.");

        yield return new Tone(Leader, 300);
        yield return new Tone(Sync, 10);
        yield return new Tone(Leader, 300);
        yield return new Tone(Sync, 30);
        for (var bit = 0; bit < 7; bit++)
            yield return new Tone(((visCode >> bit) & 1) == 1 ? VisOne : VisZero, 30);
        var parityOdd = BitOperations.PopCount(visCode) % 2 == 1;
        yield return new Tone(parityOdd ? VisOne : VisZero, 30);
        yield return new Tone(Sync, 30);
    }

    private static IEnumerable<Tone> CwTail(TransmitOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CwId)) return [];
        return Concat([Tone.Silence(CwGapMs)], Morse.Encode(options.CwId, options.CwWpm, options.CwToneHz));
    }

    private static IEnumerable<Tone> Concat(params IEnumerable<Tone>[] parts) => parts.SelectMany(p => p);
}
