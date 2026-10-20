// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Net.Sockets;
using System.Text;

namespace SstvStudio.Hamlib;

/// <summary>
/// Minimal rigctld TCP client. Calls are serialized; the end of a response is detected either by an
/// <c>RPRT</c> line or by a short idle window, because some rigctld emulators omit <c>RPRT 0</c>.
/// </summary>
public sealed class RigctldClient : IAsyncDisposable
{
    private static readonly TimeSpan IdleWindow = TimeSpan.FromMilliseconds(150);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;

    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(3);
    public TimeSpan CommandTimeout { get; init; } = TimeSpan.FromSeconds(2);
    public bool IsConnected => _tcp?.Connected == true;

    public async Task ConnectAsync(string host, int port, CancellationToken ct = default)
    {
        Disconnect();
        _tcp = new TcpClient { NoDelay = true };
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ConnectTimeout);
        await _tcp.ConnectAsync(host, port, cts.Token);
        _stream = _tcp.GetStream();
    }

    public void Disconnect()
    {
        _stream?.Dispose();
        _tcp?.Dispose();
        _stream = null;
        _tcp = null;
    }

    public async Task<long> GetFrequencyAsync(CancellationToken ct = default)
    {
        var lines = await SendCommandAsync("f", ct, expectedLines: 1);
        return lines.Length > 0 && double.TryParse(lines[0], System.Globalization.CultureInfo.InvariantCulture, out var hz)
            ? (long)hz
            : 0;
    }

    public async Task<string> GetModeAsync(CancellationToken ct = default)
    {
        var lines = await SendCommandAsync("m", ct, expectedLines: 2);
        return lines.Length > 0 ? lines[0] : "";
    }

    public async Task<bool> GetPttAsync(CancellationToken ct = default)
    {
        var lines = await SendCommandAsync("t", ct, expectedLines: 1);
        return lines.Length > 0 && lines[0] != "0";
    }

    public Task SetPttAsync(bool on, CancellationToken ct = default) =>
        SendCommandAsync(on ? "T 1" : "T 0", ct);

    public Task SetFrequencyAsync(long hz, CancellationToken ct = default) =>
        SendCommandAsync(FormattableString.Invariant($"F {hz}"), ct);

    /// <summary>Reads a level (e.g. <c>SWR</c>, <c>ALC</c>); null when the rig or backend doesn't support it.</summary>
    public async Task<double?> TryGetLevelAsync(string level, CancellationToken ct = default)
    {
        try
        {
            var lines = await SendCommandAsync("l " + level, ct, expectedLines: 1);
            return lines.Length > 0 && double.TryParse(lines[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        }
        catch (RigctldException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sends a command and returns the response lines without the trailing RPRT line.
    /// With <paramref name="expectedLines"/> set, returns as soon as that many lines arrive
    /// instead of waiting out the idle window.
    /// </summary>
    public async Task<string[]> SendCommandAsync(string command, CancellationToken ct = default, int expectedLines = 0)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var stream = _stream ?? throw new InvalidOperationException("Not connected to rigctld.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(CommandTimeout);

            await stream.WriteAsync(Encoding.ASCII.GetBytes(command + "\n"), cts.Token);

            var buffer = new byte[1024];
            var sb = new StringBuilder();
            while (true)
            {
                int read;
                try
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    idle.CancelAfter(IdleWindow);
                    read = await stream.ReadAsync(buffer, idle.Token);
                }
                catch (OperationCanceledException) when (!cts.IsCancellationRequested)
                {
                    // Idle window elapsed: done if we already have data, otherwise keep waiting
                    // until the command timeout for a slow rig.
                    if (sb.Length > 0) break;
                    continue;
                }

                if (read == 0) throw new IOException("rigctld closed the connection.");
                sb.Append(Encoding.ASCII.GetString(buffer, 0, read));
                var text = sb.ToString();
                if (text.Contains("RPRT")) break;
                if (expectedLines > 0 && text.Count(c => c == '\n') >= expectedLines) break;
            }

            return ParseResponse(sb.ToString());
        }
        finally
        {
            _lock.Release();
        }
    }

    internal static string[] ParseResponse(string raw)
    {
        var lines = raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rprt = lines.FirstOrDefault(l => l.StartsWith("RPRT", StringComparison.Ordinal));
        if (rprt is not null && int.TryParse(rprt[4..].Trim(), out var code) && code != 0)
            throw new RigctldException(code);
        return lines.Where(l => !l.StartsWith("RPRT", StringComparison.Ordinal)).ToArray();
    }

    public ValueTask DisposeAsync()
    {
        Disconnect();
        _lock.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class RigctldException(int code) : Exception($"rigctld returned error RPRT {code}.")
{
    public int Code { get; } = code;
}
