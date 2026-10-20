// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Globalization;

namespace SstvStudio.Hamlib;

/// <summary>
/// Transmit meters as reported by rigctld. Null means the rig/backend doesn't support that level.
/// <see cref="PowerRelative"/> and <see cref="Alc"/> are normalized 0–1 (Hamlib convention).
/// </summary>
public sealed record TxMeters(double? PowerWatts, double? PowerRelative, double? Swr, double? Alc, double? CurrentAmps)
{
    public static TxMeters Empty { get; } = new(null, null, null, null, null);
}

public sealed record RigStatus(bool Connected, long FrequencyHz, string Mode, bool Ptt, string? Error)
{
    /// <summary>RF power setting (<c>l RFPOWER</c>), 0–1.</summary>
    public double? PowerSetting { get; init; }
    public TxMeters Meters { get; init; } = TxMeters.Empty;

    public static RigStatus Disconnected(string? error = null) => new(false, 0, "", false, error);
}

/// <summary>
/// Owns the rigctld connection: polls frequency/mode/PTT/power setting, reconnects after faults, and keys PTT.
/// While <see cref="Transmitting"/> is set (or the rig reports PTT), it polls only the TX meters, fast.
/// </summary>
public sealed class RigService : IAsyncDisposable
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly RigctldClient _client = new();
    // Levels that answered RPRT error on this connection; not asked again until reconnect.
    private readonly HashSet<string> _unsupported = [];
    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private bool _transmitting;

    public event Action<RigStatus>? StatusChanged;

    public RigStatus Status { get; private set; } = RigStatus.Disconnected();
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan MeterInterval { get; set; } = TimeSpan.FromMilliseconds(200);
    /// <summary>Set by the TX controller: skip frequency/mode polls and read meters only.</summary>
    public bool Transmitting
    {
        get => Volatile.Read(ref _transmitting);
        set => Volatile.Write(ref _transmitting, value);
    }
    public bool IsRunning => _loop is { IsCompleted: false };

    public void Start(string host, int port)
    {
        if (IsRunning) return;
        _loopCts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(host, port, _loopCts.Token));
    }

    public async Task StopAsync()
    {
        if (_loopCts is null) return;
        await _loopCts.CancelAsync();
        try { if (_loop is not null) await _loop; }
        catch (OperationCanceledException) { }
        _loopCts.Dispose();
        _loopCts = null;
        _client.Disconnect();
        Publish(RigStatus.Disconnected());
    }

    public async Task SetPttAsync(bool on, CancellationToken ct = default)
    {
        await _client.SetPttAsync(on, ct);
        Publish(Status with { Ptt = on, Meters = on ? Status.Meters : TxMeters.Empty });
    }

    /// <summary>
    /// Sets the dial and returns the frequency the rig reports afterwards. That may differ from
    /// <paramref name="hz"/>: rigs round to their tuning step, and a locked VFO ignores the change.
    /// </summary>
    public async Task<long> SetFrequencyAsync(long hz, CancellationToken ct = default)
    {
        await _client.SetFrequencyAsync(hz, ct);
        var actual = await _client.GetFrequencyAsync(ct);
        if (actual <= 0) actual = hz; // unreadable answer: assume the rig took it
        Publish(Status with { FrequencyHz = actual });
        return actual;
    }

    /// <summary>Dial frequency as MHz.kHz.Hz, e.g. 14.230.000.</summary>
    public static string FormatFrequency(long hz)
    {
        var s = hz.ToString("000000000", CultureInfo.InvariantCulture);
        return $"{s[..^6].TrimStart('0').PadLeft(1, '0')}.{s[^6..^3]}.{s[^3..]}";
    }

    /// <summary>
    /// Dial frequency that moves a received signal's audio by −<paramref name="audioOffsetHz"/>, i.e. centres a
    /// signal heard <paramref name="audioOffsetHz"/> too high. On LSB the audio is mirrored, so the dial moves
    /// the other way. Returns null for modes where audio offset isn't a tuning error (FM, AM, CW…).
    /// </summary>
    public static long? RetunedFrequency(long dialHz, string mode, double audioOffsetHz)
    {
        var m = mode.ToUpperInvariant();
        var offset = (long)Math.Round(audioOffsetHz);
        if (m.Contains("LSB") || m is "DIGL" or "PKTLSB") return dialHz - offset;
        if (m.Contains("USB") || m is "DIGU" or "PKTUSB") return dialHz + offset;
        return null;
    }

    private async Task RunAsync(string host, int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    await _client.ConnectAsync(host, port, ct);
                    _unsupported.Clear();
                }

                if (Transmitting)
                {
                    var meters = await ReadMetersAsync(ct);
                    if (Transmitting) Publish(Status with { Meters = meters });
                    await Task.Delay(MeterInterval, ct);
                    continue;
                }

                var freq = await _client.GetFrequencyAsync(ct);
                var mode = await _client.GetModeAsync(ct);
                var ptt = await _client.GetPttAsync(ct);
                var setting = await LevelAsync("RFPOWER", ct);
                // Keyed by hand or another app: show meters too.
                var rxMeters = ptt ? await ReadMetersAsync(ct) : TxMeters.Empty;
                Publish(new RigStatus(true, freq, mode, ptt, null) { PowerSetting = setting, Meters = rxMeters });
                await Task.Delay(ptt ? MeterInterval : PollInterval, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _client.Disconnect();
                Publish(RigStatus.Disconnected(ex.Message));
                try { await Task.Delay(ReconnectDelay, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task<TxMeters> ReadMetersAsync(CancellationToken ct)
    {
        // Prefer real watts (Hamlib 4.1+); fall back to the normalized power meter.
        var watts = await LevelAsync("RFPOWER_METER_WATTS", ct);
        var relative = watts is null ? await LevelAsync("RFPOWER_METER", ct) : null;
        var swr = await LevelAsync("SWR", ct);
        var alc = await LevelAsync("ALC", ct);
        var current = await LevelAsync("ID_METER", ct);
        return new TxMeters(watts, relative, swr, alc, current);
    }

    private async Task<double?> LevelAsync(string level, CancellationToken ct)
    {
        if (_unsupported.Contains(level)) return null;
        var value = await _client.TryGetLevelAsync(level, ct);
        if (value is null) _unsupported.Add(level);
        return value;
    }

    private void Publish(RigStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _client.DisposeAsync();
    }
}
