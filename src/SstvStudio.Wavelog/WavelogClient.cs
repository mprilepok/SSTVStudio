// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Net.Http.Json;
using System.Text.Json;

namespace SstvStudio.Wavelog;

public sealed record WavelogOptions(string BaseUrl, string ApiKey, string StationProfileId);

public sealed record WavelogStation(string Id, string Name, string Callsign, string Gridsquare, bool Active)
{
    public override string ToString() => $"{Name} ({Callsign}, {Gridsquare})";
}

public sealed record WavelogResult(bool Success, string Message);

/// <summary>Client for the Wavelog REST API (<c>/index.php/api/…</c>).</summary>
public sealed class WavelogClient(HttpClient http)
{
    public async Task<WavelogResult> SubmitQsoAsync(WavelogOptions options, QsoRecord qso, CancellationToken ct = default)
    {
        var payload = new
        {
            key = options.ApiKey,
            station_profile_id = options.StationProfileId,
            type = "adif",
            @string = Adif.ToRecord(qso),
        };

        using var response = await http.PostAsJsonAsync(Url(options, "api/qso"), payload, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode && !body.Contains("\"failed\"", StringComparison.OrdinalIgnoreCase))
            return new WavelogResult(true, "QSO logged to Wavelog.");
        return new WavelogResult(false, $"Wavelog {(int)response.StatusCode}: {ExtractReason(body)}");
    }

    public async Task<IReadOnlyList<WavelogStation>> GetStationsAsync(WavelogOptions options, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(Url(options, $"api/station_info/{Uri.EscapeDataString(options.ApiKey)}"), ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Wavelog {(int)response.StatusCode}: {ExtractReason(body)}");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            throw new HttpRequestException($"Unexpected Wavelog response: {ExtractReason(body)}");

        return doc.RootElement.EnumerateArray().Select(e => new WavelogStation(
            Str(e, "station_id"),
            Str(e, "station_profile_name"),
            Str(e, "station_callsign"),
            Str(e, "station_gridsquare"),
            Str(e, "station_active") is "1" or "true")).ToList();
    }

    /// <summary>Pushes live CAT state, the same way WaveLogGate does, so Wavelog's QSO form follows the rig.</summary>
    public async Task SendRadioStatusAsync(WavelogOptions options, string radioName, long frequencyHz, string mode, CancellationToken ct = default)
    {
        var payload = new { key = options.ApiKey, radio = radioName, frequency = frequencyHz, mode };
        using var response = await http.PostAsJsonAsync(Url(options, "api/radio"), payload, ct);
        response.EnsureSuccessStatusCode();
    }

    internal static Uri Url(WavelogOptions options, string path)
    {
        var baseUrl = options.BaseUrl.Trim().TrimEnd('/');
        if (baseUrl.EndsWith("/index.php", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^"/index.php".Length];
        return new Uri($"{baseUrl}/index.php/{path}");
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString() : "";

    private static string ExtractReason(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                foreach (var key in new[] { "reason", "messages", "message", "status" })
                    if (doc.RootElement.TryGetProperty(key, out var v))
                        return v.ToString();
        }
        catch (JsonException) { }
        return body.Length > 200 ? body[..200] : body;
    }
}
