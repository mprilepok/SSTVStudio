// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using System.Text.RegularExpressions;
using SstvStudio.Wavelog;

namespace SstvStudio.Services;

/// <summary>Writes every QSO to a local ADIF backup first, then uploads it to Wavelog when configured.</summary>
public sealed class QsoLogger(WavelogClient wavelog)
{
    public static string AdifPath { get; } = Path.Combine(AppSettings.DataDirectory, "log.adi");

    public async Task<WavelogResult> LogAsync(QsoRecord qso, AppSettings settings, CancellationToken ct = default)
    {
        await AppendLocalAsync(qso, ct);

        if (!settings.WavelogConfigured)
            return new WavelogResult(true, $"Saved to {AdifPath} (Wavelog not configured).");

        var options = new WavelogOptions(settings.WavelogUrl, settings.WavelogApiKey, settings.WavelogStationId);
        try
        {
            return await wavelog.SubmitQsoAsync(options, qso, ct);
        }
        catch (HttpRequestException ex)
        {
            return new WavelogResult(false, $"Wavelog unreachable ({ex.Message}); QSO kept in local ADIF.");
        }
    }

    /// <summary>Number of QSOs in an ADIF file; 0 when it doesn't exist.</summary>
    public static int CountQsos(string logPath) =>
        File.Exists(logPath) ? Regex.Count(File.ReadAllText(logPath), "<EOR>", RegexOptions.IgnoreCase) : 0;

    /// <summary>Copies the log to <paramref name="destination"/>; returns how many QSOs it holds (0: nothing copied).</summary>
    public static int ExportLog(string logPath, string destination)
    {
        var count = CountQsos(logPath);
        if (count > 0) File.Copy(logPath, destination, overwrite: true);
        return count;
    }

    /// <summary>
    /// Starts a new, empty log. The old one is renamed to <c>log_yyyyMMdd_HHmmssZ.adi</c> next to it, never
    /// deleted. Returns its new name and QSO count, or null when the log was already empty.
    /// </summary>
    public static (string Name, int Qsos)? StartNewLog(string logPath)
    {
        var count = CountQsos(logPath);
        if (count == 0) return null;
        var archived = Path.Combine(Path.GetDirectoryName(logPath)!, $"log_{DateTime.UtcNow:yyyyMMdd_HHmmss}Z.adi");
        File.Move(logPath, archived);
        return (Path.GetFileName(archived), count);
    }

    private static async Task AppendLocalAsync(QsoRecord qso, CancellationToken ct)
    {
        Directory.CreateDirectory(AppSettings.DataDirectory);
        if (!File.Exists(AdifPath))
            await File.WriteAllTextAsync(AdifPath, Adif.FileHeader, ct);
        await File.AppendAllTextAsync(AdifPath, Adif.ToRecord(qso) + Environment.NewLine, ct);
    }
}
