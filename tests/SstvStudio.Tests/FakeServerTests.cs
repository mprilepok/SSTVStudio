// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SstvStudio.Hamlib;
using SstvStudio.Wavelog;

namespace SstvStudio.Tests;

public class RigServiceTests
{
    /// <summary>
    /// Tiny rigctld emulator: answers f/F/m/t and meter levels (no RFPOWER_METER_WATTS), records commands.
    /// <c>F</c> rounds to <see cref="StepHz"/> and is ignored while <see cref="VfoLocked"/>.
    /// </summary>
    internal sealed class FakeRigctld : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        public ConcurrentQueue<string> Commands { get; } = new();
        public bool Ptt { get; private set; }
        public long Frequency { get; private set; } = 14_230_000;
        public long StepHz { get; init; } = 1;
        public bool VfoLocked { get; init; }
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public FakeRigctld()
        {
            _listener.Start();
            _ = Task.Run(AcceptAsync);
        }

        // Like rigctld, accepts again after a disconnect (RigService reconnects after a command timeout).
        private async Task AcceptAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try { await ServeAsync(await _listener.AcceptTcpClientAsync(_cts.Token)); }
                catch (Exception) when (!_cts.IsCancellationRequested) { /* client went away; wait for the next */ }
                catch (Exception) { return; }
            }
        }

        private async Task ServeAsync(TcpClient accepted)
        {
            using var client = accepted;
            using var reader = new StreamReader(client.GetStream(), Encoding.ASCII);
            await using var writer = new StreamWriter(client.GetStream(), Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };
            while (await reader.ReadLineAsync(_cts.Token) is { } line)
            {
                Commands.Enqueue(line);
                switch (line)
                {
                    case "f": await writer.WriteLineAsync(Frequency.ToString(CultureInfo.InvariantCulture)); break;
                    case var set when set.StartsWith("F ", StringComparison.Ordinal):
                        if (!VfoLocked) Frequency = (long)Math.Round(double.Parse(set[2..], CultureInfo.InvariantCulture) / StepHz) * StepHz;
                        await writer.WriteLineAsync("RPRT 0");
                        break;
                    case "m": await writer.WriteAsync("USB\n2400\n"); break;
                    case "t": await writer.WriteLineAsync(Ptt ? "1" : "0"); break;
                    case "T 1": Ptt = true; await writer.WriteLineAsync("RPRT 0"); break;
                    case "T 0": Ptt = false; await writer.WriteLineAsync("RPRT 0"); break;
                    case "l RFPOWER": await writer.WriteLineAsync("0.500000"); break;
                    case "l RFPOWER_METER": await writer.WriteLineAsync("0.480000"); break;
                    case "l SWR": await writer.WriteLineAsync("1.300000"); break;
                    case "l ALC": await writer.WriteLineAsync("0.050000"); break;
                    case "l ID_METER": await writer.WriteLineAsync("12.500000"); break;
                    case "l RFPOWER_METER_WATTS": await writer.WriteLineAsync("RPRT -11"); break;
                    default: await writer.WriteLineAsync("RPRT -1"); break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            _listener.Stop();
        }
    }

    [Fact]
    public async Task PollsStatusAndKeysPtt()
    {
        await using var fake = new FakeRigctld();
        await using var rig = new RigService { PollInterval = TimeSpan.FromMilliseconds(50) };
        var connected = new TaskCompletionSource<RigStatus>();
        rig.StatusChanged += s => { if (s.Connected) connected.TrySetResult(s); };

        rig.Start("127.0.0.1", fake.Port);
        var status = await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(14_230_000, status.FrequencyHz);
        Assert.Equal("USB", status.Mode);
        Assert.False(status.Ptt);
        Assert.Equal(0.5, status.PowerSetting);
        Assert.Equal(TxMeters.Empty, status.Meters);

        rig.Transmitting = true;
        await rig.SetPttAsync(true);
        Assert.True(fake.Ptt);
        await rig.SetPttAsync(false);
        Assert.False(fake.Ptt);
        Assert.Contains("T 1", fake.Commands);
    }

    [Theory]
    [InlineData(1, false, 14_230_047)]  // takes it exactly
    [InlineData(10, false, 14_230_050)] // rounds to its tuning step
    [InlineData(1, true, 14_230_000)]   // locked VFO ignores it
    public async Task SetFrequency_ReturnsWhatTheRigReportsBack(long stepHz, bool locked, long expected)
    {
        await using var fake = new FakeRigctld { StepHz = stepHz, VfoLocked = locked };
        await using var rig = new RigService { PollInterval = TimeSpan.FromSeconds(10) };
        var connected = new TaskCompletionSource();
        rig.StatusChanged += s => { if (s.Connected) connected.TrySetResult(); };
        rig.Start("127.0.0.1", fake.Port);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var actual = await rig.SetFrequencyAsync(14_230_047);

        Assert.Equal(expected, actual);
        Assert.Equal(expected, rig.Status.FrequencyHz);
        Assert.Contains("F 14230047", fake.Commands);
    }
}

public class RigMeterTests
{
    [Fact]
    public async Task ReadsMetersWhileTransmittingAndFallsBackToRelativePower()
    {
        await using var fake = new RigServiceTests.FakeRigctld();
        await using var rig = new RigService { PollInterval = TimeSpan.FromMilliseconds(50), MeterInterval = TimeSpan.FromMilliseconds(20) };
        var metered = new TaskCompletionSource<RigStatus>();
        var connected = new TaskCompletionSource();
        rig.StatusChanged += s =>
        {
            if (s.Connected) connected.TrySetResult();
            if (s.Meters.Swr is not null) metered.TrySetResult(s);
        };

        rig.Start("127.0.0.1", fake.Port);
        await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        rig.Transmitting = true;
        var status = await metered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // Several more meter cycles. Waited for, not timed: under a full test run 200 ms wasn't always 3 cycles.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (fake.Commands.Count(c => c == "l SWR") <= 2 && DateTime.UtcNow < deadline) await Task.Delay(20);

        Assert.Equal(new TxMeters(null, 0.48, 1.3, 0.05, 12.5), status.Meters);
        // The unsupported level is asked once per connection, not on every cycle.
        Assert.Single(fake.Commands, c => c == "l RFPOWER_METER_WATTS");
        Assert.True(fake.Commands.Count(c => c == "l SWR") > 2);
    }
}

public class WavelogClientTests
{
    private sealed class CaptureHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public List<(HttpMethod Method, Uri Uri, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method, request.RequestUri!, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(code) { Content = new StringContent(body) };
        }
    }

    private static readonly WavelogOptions Options = new("https://log.example.com/", "wl123", "7");

    [Fact]
    public async Task SubmitQso_PostsAdifPayload()
    {
        var handler = new CaptureHandler(HttpStatusCode.Created, """{"status":"created","type":"adif"}""");
        var client = new WavelogClient(new HttpClient(handler));

        var result = await client.SubmitQsoAsync(Options, new QsoRecord { Call = "OK1ABC", StartUtc = DateTime.UtcNow, FrequencyHz = 14_230_000 });

        Assert.True(result.Success);
        var (method, uri, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, method);
        Assert.Equal("https://log.example.com/index.php/api/qso", uri.ToString());
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("wl123", json.RootElement.GetProperty("key").GetString());
        Assert.Equal("7", json.RootElement.GetProperty("station_profile_id").GetString());
        Assert.Equal("adif", json.RootElement.GetProperty("type").GetString());
        Assert.Contains("<CALL:6>OK1ABC", json.RootElement.GetProperty("string").GetString());
    }

    [Fact]
    public async Task SubmitQso_ReportsFailureReason()
    {
        var handler = new CaptureHandler(HttpStatusCode.Unauthorized, """{"status":"failed","reason":"missing api key"}""");
        var result = await new WavelogClient(new HttpClient(handler))
            .SubmitQsoAsync(Options, new QsoRecord { Call = "OK1ABC", StartUtc = DateTime.UtcNow });

        Assert.False(result.Success);
        Assert.Contains("missing api key", result.Message);
    }

    [Fact]
    public async Task GetStations_ParsesProfiles()
    {
        var handler = new CaptureHandler(HttpStatusCode.OK,
            """[{"station_id":"7","station_profile_name":"Home","station_gridsquare":"JN88","station_callsign":"OM6XX","station_active":"1"}]""");
        var stations = await new WavelogClient(new HttpClient(handler)).GetStationsAsync(Options);

        var s = Assert.Single(stations);
        Assert.Equal(new WavelogStation("7", "Home", "OM6XX", "JN88", true), s);
        Assert.EndsWith("/index.php/api/station_info/wl123", handler.Requests[0].Uri.ToString());
    }
}
