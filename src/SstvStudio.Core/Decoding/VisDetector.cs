// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Numerics;
using SstvStudio.Core.Modes;

namespace SstvStudio.Core.Decoding;

/// <summary>
/// Finds the VIS header in a frequency stream. Frequencies are averaged into 10 ms blocks. Every 30 ms
/// element is checked at the middle block of its 3-block window, which lies fully inside the element
/// whatever the alignment between the block grid and the signal.
/// The receiver may be mistuned: the offset is taken from the 1900 Hz leader and every other element is
/// checked relative to it, so headers up to ±<see cref="MaxOffsetHz"/> off are still found.
/// </summary>
internal sealed class VisDetector(double sampleRate)
{
    private const int HistoryBlocks = 64;
    public const double MaxOffsetHz = 250;
    private const double ToleranceHz = 100; // per element, relative to the offset (as before offsets existed)

    private readonly int _blockLength = Math.Max(1, (int)Math.Round(sampleRate * 0.010));
    private readonly List<double> _blocks = [];
    private double _sum;
    private int _count;

    /// <summary>Average frequency of the most recent 10 ms block (a tuning indicator).</summary>
    public double LastBlockHz { get; private set; }

    public void Reset()
    {
        _blocks.Clear();
        _sum = 0;
        _count = 0;
    }

    /// <summary>
    /// Feeds one frequency sample; returns the VIS code, the stream index just past the stop bit, and the
    /// measured tuning offset (signal frequency minus nominal).
    /// </summary>
    public (byte Code, long EndIndex, double OffsetHz)? Push(float hz, long index)
    {
        // Clamp so discriminator spikes in noise can't drag a block average far.
        _sum += Math.Clamp(hz, 900f, 2700f);
        if (++_count < _blockLength) return null;

        LastBlockHz = _sum / _count;
        _sum = 0;
        _count = 0;
        _blocks.Add(LastBlockHz);
        if (_blocks.Count > HistoryBlocks) _blocks.RemoveAt(0);

        if (Match(_blocks) is not { } hit) return null;
        _blocks.Clear();
        return (hit.Code, index + 1, hit.OffsetHz);
    }

    /// <summary>
    /// Pattern ending at the newest block E: leader 1900 Hz (E-59…E-30), start bit 1200 Hz (E-29…E-27),
    /// 7 data bits LSB first plus even parity (E-26…E-3, 1100 Hz = 1, 1300 Hz = 0), stop bit 1200 Hz (E-2…E).
    /// </summary>
    internal static (byte Code, double OffsetHz)? Match(IReadOnlyList<double> blocks)
    {
        if (blocks.Count < 60) return null;
        var e = blocks.Count - 1;

        // The leader fixes the offset (median of four blocks, robust to one noisy block).
        var leader = new[] { 33, 40, 47, 54 }.Select(o => blocks[e - o]).Order().ToArray();
        var offset = (leader[1] + leader[2]) / 2 - SstvFrequencies.Leader;
        if (Math.Abs(offset) > MaxOffsetHz) return null;

        bool Near(double hz, double target) => Math.Abs(hz - offset - target) < ToleranceHz;

        if (leader.Any(f => !Near(f, SstvFrequencies.Leader))) return null;

        if (!Near(blocks[e - 1], SstvFrequencies.Sync) || !Near(blocks[e - 28], SstvFrequencies.Sync))
            return null;

        var bits = 0;
        for (var k = 0; k < 8; k++)
        {
            var f = blocks[e - 25 + 3 * k];
            if (Near(f, SstvFrequencies.VisOne)) bits |= 1 << k;
            else if (!Near(f, SstvFrequencies.VisZero)) return null;
        }

        var code = (byte)(bits & 0x7F);
        var parity = (bits >> 7) & 1;
        if (BitOperations.PopCount(code) % 2 != parity) return null;
        return (code, offset);
    }
}
