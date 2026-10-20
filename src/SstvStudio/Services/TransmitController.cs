// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SstvStudio.Core.Audio;
using SstvStudio.Core.Encoding;
using SstvStudio.Core.Imaging;
using SstvStudio.Core.Modes;
using SstvStudio.Hamlib;

namespace SstvStudio.Services;

public sealed record TransmitRequest(SstvMode Mode, RgbImage Image, TransmitOptions Options, AppSettings Settings);

/// <summary>
/// Runs one transmission: key PTT, lead delay, play audio, tail delay, un-key.
/// Un-keying happens in <c>finally</c> and is retried; a watchdog aborts runaway transmissions.
/// </summary>
public sealed class TransmitController(RigService rig)
{
    public ToneSynthesizer? Current { get; private set; }

    public static ToneSynthesizer CreateSynthesizer(TransmitRequest request) => new(
        SstvEncoder.Encode(request.Mode, request.Image, request.Options),
        SstvEncoder.DurationMs(request.Mode, request.Options),
        request.Settings.SampleRate,
        request.Settings.TxLevelPercent / 100.0,
        request.Settings.ClockPpm);

    public async Task TransmitAsync(TransmitRequest request, CancellationToken ct)
    {
        var settings = request.Settings;
        var synth = CreateSynthesizer(request);
        var durationMs = SstvEncoder.DurationMs(request.Mode, request.Options);
        if (durationMs / 1000 > settings.MaxTxSeconds)
            throw new InvalidOperationException(
                $"{request.Mode.Name} takes {durationMs / 1000:0} s, longer than the TX watchdog ({settings.MaxTxSeconds} s). Raise it in Settings.");

        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(ct);
        watchdog.CancelAfter(TimeSpan.FromSeconds(settings.MaxTxSeconds));

        await using var ptt = PttSwitch.Create(settings.PttMethod, rig, settings.SerialPort);
        Current = synth;
        rig.Transmitting = true;
        var keyed = false;
        try
        {
            await ptt.SetAsync(true, ct);
            keyed = true;
            await Task.Delay(settings.PttLeadMs, watchdog.Token);
            await AudioOutput.PlayAsync(synth, settings.AudioDevice, watchdog.Token);
            await Task.Delay(settings.PttTailMs, CancellationToken.None);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"TX watchdog stopped the transmission after {settings.MaxTxSeconds} s.");
        }
        finally
        {
            try
            {
                if (keyed) await UnkeyAsync(ptt);
            }
            finally
            {
                rig.Transmitting = false;
                Current = null;
            }
        }
    }

    private static async Task UnkeyAsync(IPttSwitch ptt)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ptt.SetAsync(false, CancellationToken.None);
                return;
            }
            catch (Exception) when (attempt < 3)
            {
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"PTT UN-KEY FAILED – check the transceiver! ({ex.Message})", ex);
            }
        }
    }
}
