// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Numerics;
using SstvStudio.Core.Modes;

namespace SstvStudio.Core.Decoding;

/// <summary>
/// Finds the VIS header in a frequency stream. Frequencies are averaged into 5 ms blocks, and every 30 ms
/// element is judged on its 4 inner blocks (20 ms), which lie inside the element whatever the alignment between the
/// block grid and the signal.
/// In noise the measured tones are squeezed towards the middle of the band, so the header is judged against itself
/// rather than against fixed frequencies:
/// <list type="bullet">
/// <item>The leader is accepted when most of its blocks agree.</item>
/// <item>The start and stop bits must agree and lie well below the leader.</item>
/// <item>The data bits are split into ones and zeros at their largest gap.</item>
/// <item>The tuning offset (up to ±<see cref="MaxOffsetHz"/>) averages the leader's and the sync bits' offsets,
/// whose squeeze errors cancel.</item>
/// <item>Parity, plus a code that names a known mode, guard against noise.</item>
/// </list>
/// The old fixed-window detector found 53 % of headers at 3 dB SNR, 10 % at 2 dB and none at 1 dB; this one finds
/// 100 %, 87 % and 70 %, with no false starts in 40 minutes of noise. A real weak Robot 36 it missed is pinned by
/// <c>DecodesRealWeakHeader</c>.
/// </summary>
internal sealed class VisDetector(double sampleRate, Func<byte, bool>? isKnownCode = null)
{
    private const int BlocksPerElement = 6;                 // 30 ms
    private const int LeaderBlocks = 60;                    // 300 ms
    private const int PatternBlocks = LeaderBlocks + 10 * BlocksPerElement;
    private const int HistoryBlocks = PatternBlocks + 4;
    public const double MaxOffsetHz = 250;
    // A leader block "agrees" within this of the leader's median; LeaderAgreement of them must.
    private const double LeaderToleranceHz = 120;
    private const double LeaderAgreement = 0.6;
    // Start and stop bits sit MinSpanHz…MaxSpanHz ("span") below the leader (nominally 700 Hz), and agree within
    // this share of the span.
    private const double SyncAgreementFraction = 0.3;
    // The bits' threshold may lie at most this share of the span below the sync level.
    private const double ThresholdBelowSyncFraction = 0.05;
    private const double MinSpanHz = 350;
    private const double MaxSpanHz = 900;
    // The data bits' two groups (1100 / 1300 Hz, nominally 200 Hz apart) must be split by at least this share of the span.
    private const double MinBitGapFraction = 0.08;

    private readonly int _blockLength = Math.Max(1, (int)Math.Round(sampleRate * 0.005));
    private readonly List<double> _blocks = [];
    private double _sum;
    private int _count;

    /// <summary>Average frequency of the most recent 10 ms (a tuning indicator).</summary>
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
        // Clamp so discriminator spikes in noise can't drag a block average far. (A median per block was tried and
        // found far worse: 36 % of headers at 4 dB SNR instead of 100 %.)
        _sum += Math.Clamp(hz, 900f, 2700f);
        if (++_count < _blockLength) return null;

        var block = _sum / _count;
        _sum = 0;
        _count = 0;
        LastBlockHz = _blocks.Count > 0 ? (_blocks[^1] + block) / 2 : block;
        _blocks.Add(block);
        if (_blocks.Count > HistoryBlocks) _blocks.RemoveAt(0);

        if (Match(_blocks, isKnownCode) is not { } hit) return null;
        _blocks.Clear();
        return (hit.Code, index + 1, hit.OffsetHz);
    }

    /// <summary>
    /// Pattern ending at the newest block E (5 ms blocks): leader 1900 Hz (60 blocks), start bit 1200 Hz, 7 data bits
    /// LSB first plus even parity (1100 Hz = 1, 1300 Hz = 0), stop bit 1200 Hz (6 blocks each).
    /// </summary>
    internal static (byte Code, double OffsetHz)? Match(IReadOnlyList<double> blocks, Func<byte, bool>? isKnownCode = null)
    {
        if (blocks.Count < PatternBlocks) return null;
        var first = blocks.Count - PatternBlocks; // first leader block

        // Element k (0 = start bit … 9 = stop bit): the mean of its 4 inner blocks.
        double Element(int k)
        {
            var start = first + LeaderBlocks + k * BlocksPerElement + 1;
            return (blocks[start] + blocks[start + 1] + blocks[start + 2] + blocks[start + 3]) / 4;
        }

        // In noise the measured tones are squeezed towards the middle of the band (a 1900 Hz leader read 1781 Hz and
        // 1200 Hz start/stop bits 1244–1263 Hz on a real weak Robot 36), so the header is judged against itself, not
        // against fixed frequencies.
        var startBit = Element(0);
        var stopBit = Element(9);
        var sync = (startBit + stopBit) / 2;

        var leader = new double[LeaderBlocks - 4];
        for (var i = 0; i < leader.Length; i++) leader[i] = blocks[first + 2 + i];
        Array.Sort(leader);
        var median = leader[leader.Length / 2];
        var span = median - sync; // nominally 700 Hz (1900 − 1200); less when squeezed
        if (span < MinSpanHz || span > MaxSpanHz) return null;
        // Start and stop bits must agree. In a fade the squeeze changes between them, so allow a share of the span.
        if (Math.Abs(startBit - stopBit) > SyncAgreementFraction * span) return null;
        // The squeeze moves the leader down and the sync up by similar amounts, so their average cancels it.
        var offset = ((median - SstvFrequencies.Leader) + (sync - SstvFrequencies.Sync)) / 2;
        if (Math.Abs(offset) > MaxOffsetHz) return null;
        var agreeing = leader.Count(f => Math.Abs(f - median) < LeaderToleranceHz);
        if (agreeing < LeaderAgreement * leader.Length) return null;

        // Data bits: split the 8 values at their largest gap; the low group is 1 (1100 Hz), the high group 0 (1300
        // Hz). Every known mode's code has both ones and zeros, so the gap is always between the two tones.
        var values = new double[8];
        for (var k = 0; k < 8; k++) values[k] = Element(1 + k);
        var sorted = values.Order().ToArray();
        var gap = 0.0;
        var threshold = 0.0;
        for (var i = 0; i < 7; i++)
        {
            if (sorted[i + 1] - sorted[i] <= gap) continue;
            gap = sorted[i + 1] - sorted[i];
            threshold = (sorted[i] + sorted[i + 1]) / 2;
        }
        if (gap < MinBitGapFraction * span) return null;
        // The 1100 and 1300 Hz groups sit either side of the 1200 Hz sync, so the threshold is at or above the sync
        // level (squeezed real headers: +16 and +82 Hz). Below it means the "start/stop bits" were really zero bits
        // of a misaligned match: a faded Scottie S2 was misread as PD 120 that way (threshold 53 Hz below).
        if (threshold < sync - ThresholdBelowSyncFraction * span || threshold > sync + 0.45 * span) return null;

        var bits = 0;
        for (var k = 0; k < 8; k++)
        {
            if (values[k] < threshold) bits |= 1 << k;
        }

        var code = (byte)(bits & 0x7F);
        var parity = (bits >> 7) & 1;
        if (BitOperations.PopCount(code) % 2 != parity) return null;
        if (isKnownCode is not null && !isKnownCode(code)) return null;
        return (code, offset);
    }
}
