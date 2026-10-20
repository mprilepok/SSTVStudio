// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Services;

/// <summary>
/// Unexpected errors, appended to <c>errors.log</c> in the data folder so they can be reported. The app keeps
/// running; <see cref="Logged"/> lets the UI say that something went wrong.
/// </summary>
public static class ErrorLog
{
    private const long MaxBytes = 1 << 20;                              // then start afresh, keeping one old file
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(10); // a fault in a loop logs once
    private static readonly Lock Gate = new();
    private static string? _lastKey;
    private static DateTime _lastUtc;
    private static int _repeats;

    public static string FilePath => Path.Combine(AppSettings.DataDirectory, "errors.log");

    /// <summary>Raised on the thread that hit the error, with where it happened, after the entry is written.</summary>
    public static event Action<string>? Logged;

    public static void Write(string where, Exception ex)
    {
        var key = $"{where}|{ex.GetType().FullName}|{ex.Message}";
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            if (key == _lastKey && now - _lastUtc < RepeatWindow)
            {
                _repeats++;
                _lastUtc = now;
                return;
            }
            var repeated = _repeats > 0 ? $"(The previous error repeated {_repeats} more time(s).)\n\n" : "";
            _lastKey = key;
            _lastUtc = now;
            _repeats = 0;
            try
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                if (new FileInfo(FilePath) is { Exists: true, Length: > MaxBytes })
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath,
                    $"{repeated}{now:yyyy-MM-dd HH:mm:ss}Z  {where}  ({AppInfo.Product} {AppInfo.Version}, {AppInfo.OperatingSystem})\n{ex}\n\n");
            }
            catch (Exception)
            {
                // Logging must never take the app down (disk full, folder locked…).
            }
        }
        Logged?.Invoke(where);
    }
}
