// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Numerics;
using SstvStudio.Core.Modes;

namespace SstvStudio.Core.Decoding;

/// <summary>
/// Finds the VIS header in a frequency stream. Frequencies are averaged into 5 ms blocks, and every 30 ms
/// element is judged on its 4 inner blocks (20 ms), which lie inside the element whatever the alignment between the
/// block grid and the signal.
/// Decisions are soft, so weak headers aren't thrown out by one noisy block:
/// <list type="bullet">
/// <item>The leader is accepted when most of its blocks agree, and its median gives the tuning offset (up to
/// ±<see cref="MaxOffsetHz"/>).</item>
/// <item>Each data bit goes to the nearer of 1100 and 1300 Hz.</item>
/// <item>Parity, plus a code that names a known mode, guard against noise.</item>
/// </list>
/// The old hard-window detector found 53 % of headers at 3 dB SNR and none at 1 dB.
/// </summary>
internal sealed class VisDetector(double sampleRate, Func<byte, bool>? isKnownCode = null)
{
    private const int BlocksPerElement = 6;                 // 30 ms
    private const int LeaderBlocks = 60;                    // 300 ms
    private const int PatternBlocks = LeaderBlocks + 10 * BlocksPerElement;
    private const int HistoryBlocks = PatternBlocks + 4;
    public const double MaxOffsetHz = 250;
    // A leader block "agrees" within this of the leader's median; LeaderAgreement of them must.
    internal static double LeaderToleranceHz = 120;
    internal static double LeaderAgreement = 0.6;
    // Start and stop bits must average within this of 1200 Hz (data bits are 100 Hz either side of it).
    internal static double SyncToleranceHz = 90;

    private readonly int _blockLength = Math.Max(1, (int)Math.Round(sampleRate * 0.005));
    private readonly List<double> _blocks = [];
    private float[] _samples = [];
    private int _count;
    internal static bool UseMedian = false; // TEMP experiment switch: the median was far worse (36 % vs 96 % at 4 dB)

    /// <summary>Average frequency of the most recent 10 ms (a tuning indicator).</summary>
    public double LastBlockHz { get; private set; }

    public void Reset()
    {
        _blocks.Clear();
        _count = 0;
    }

    /// <summary>
    /// Feeds one frequency sample; returns the VIS code, the stream index just past the stop bit, and the
    /// measured tuning offset (signal frequency minus nominal).
    /// </summary>
    public (byte Code, long EndIndex, double OffsetHz)? Push(float hz, long index)
    {
        if (_samples.Length != _blockLength) _samples = new float[_blockLength];
        _samples[_count] = Math.Clamp(hz, 900f, 2700f);
        if (++_count < _blockLength) return null;

        double block;
        if (UseMedian)
        {
            Array.Sort(_samples);
            block = _samples[_count / 2];
        }
        else block = _samples.Average();
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

        // Cheap rejection first: start and stop bits are low tones, the leader is not.
        var startBit = Element(0);
        var stopBit = Element(9);
        if (startBit > SstvFrequencies.Sync + MaxOffsetHz + SyncToleranceHz) return null;

        var leader = new double[LeaderBlocks - 4];
        for (var i = 0; i < leader.Length; i++) leader[i] = blocks[first + 2 + i];
        Array.Sort(leader);
        var median = leader[leader.Length / 2];
        var offset = median - SstvFrequencies.Leader;
        if (Math.Abs(offset) > MaxOffsetHz) return null;
        var agreeing = leader.Count(f => Math.Abs(f - median) < LeaderToleranceHz);
        if (agreeing < LeaderAgreement * leader.Length) return null;

        if (Math.Abs(startBit - offset - SstvFrequencies.Sync) > SyncToleranceHz ||
            Math.Abs(stopBit - offset - SstvFrequencies.Sync) > SyncToleranceHz)
            return null;

        var bits = 0;
        for (var k = 0; k < 8; k++)
        {
            if (Element(1 + k) - offset < SstvFrequencies.Sync) bits |= 1 << k; // nearer 1100 (one) than 1300 (zero)
        }

        var code = (byte)(bits & 0x7F);
        var parity = (bits >> 7) & 1;
        if (BitOperations.PopCount(code) % 2 != parity) return null;
        if (isKnownCode is not null && !isKnownCode(code)) return null;
        return (code, offset);
    }
}
