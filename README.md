# SSTV Studio

A modern slow-scan TV **transmitter and receiver** for Windows. Built with .NET 10 and Avalonia 12, it keys the rig through
**Hamlib rigctld** and logs QSOs to **Wavelog**.

**New users: start with the [User Manual](docs/USER_MANUAL.md).** Planned work and release tasks: [docs/TODO.md](docs/TODO.md). See [docs/ANALYSIS.md](docs/ANALYSIS.md) for the survey of existing SSTV software and the design rationale.

## Features
- **15 modes:** Martin M1/M2, Scottie S1/S2/DX, Robot 36/72, PD 50/90/120/160/180/240/290, and Wraase SC2-180,
  all with a standard VIS header. Each mode was checked by decoding its output with an independent decoder.
- Phase-continuous synthesis with cumulative fractional timing, so no slant comes from the encoder itself.
  A ±ppm sound-card clock correction is available.
- Optional MMSSTV-style calibration preamble and a CW ID (`DE <call>`) after the picture.
- Pictures can be filled/cropped, fitted or stretched to the mode's resolution. There is a built-in test pattern.
- **Overlay profiles** (**CQ**, **Reply**, **73**, plus your own) switch with one click above the preview.
  - A profile can have any number of text lines.
  - Each line has its own font (any installed font), size (% of the picture height), colour, bold, black outline,
    position (top, middle or bottom) and alignment.
  - Placeholders: `{CALL} {MYCALL} {RST} {NAME} {GRID} {MYGRID} {MODE} {DATE} {TIME} {FREQ}`.
    A line whose placeholders are all empty is hidden.
- Rig control through **rigctld**: live frequency, mode and PTT, with automatic reconnect.
  PTT can go through rigctld, serial RTS, serial DTR or VOX.
- **Live TX meters** read from rigctld: RF power (watts if the rig reports them, otherwise %), SWR, ALC and PA
  current (`ID_METER`), plus the rig's power setting. SWR and ALC are coloured green, yellow or red.
  After each transmission a summary shows average power and the highest SWR, ALC and current.
  Meters the rig doesn't support show `n/a`.
- **SWR trip:** TX stops when SWR reaches the trip level on two readings in a row (default 3.0; 0 turns it off).
- TX safety: configurable PTT lead and tail, a TX watchdog, meters-only polling during TX, un-key retried in `finally`,
  and window close waits until PTT is released.
- **Wavelog:** station-profile picker, QSO upload (`/api/qso`) and optional live CAT push (`/api/radio`).
  Every QSO is written to `%APPDATA%\SstvStudio\log.adi` **before** the upload, so nothing is lost if Wavelog
  is unreachable.
- WAV export for any mode.
- **Receive** in all 15 modes, from live audio input or a WAV recording:
  - The mode is detected automatically from the VIS header.
  - Each picture builds line by line on screen. Slant from clock errors is corrected using a robust fit
    over the sync pulses.
  - Input level and tone indicators help with setup.
  - Received pictures go to a gallery and are auto-saved as PNG. **Use as TX picture** lets you reply
    with one.
  - Reception is muted while the app is transmitting.
  - After each picture it goes straight back to waiting for the next header. If a transmission is cut short (about
    6 s without sync, or a new header mid-picture), the partial picture is kept and the next one is received.
- **Reception aids:**
  - **Waterfall** (1000–2500 Hz, with markers at 1200/1500/1900/2300 Hz) and a tuning readout. **Fix tuning**
    retunes the rig through Hamlib, with the correct direction on USB and LSB.
  - Mistuned signals up to ±250 Hz are detected and their pictures corrected.
  - **Manual start** in a chosen mode when the header was missed. In tests it recovered 5–6 of 6 weak pictures
    whose header was detected only 0–1 times out of 6.
  - **Re-decode** saved audio or WAV files with a forced mode, horizontal shift and slant trim.
  - **Noise filter** (Off / Low / High).
- **Saving received audio:**
  - Each picture's audio runs from about 3 s before its VIS header to the end of the picture. It can be auto-saved
    as a WAV next to the PNG, or saved later with **Save audio…**. The WAV decodes again with **Decode WAV…**.
  - **● Record** writes everything heard on the input to `rx_<time>Z.wav` until you stop it, skipping your own TX.

## Quick start
```powershell
# 1. Start rigctld for your rig (example: IC-7300 on COM5)
rigctld -m 3073 -r COM5 -s 115200

# 2. Run the app
dotnet run --project src/SstvStudio
```
In **Settings**:
1. Enter your callsign and locator.
2. Choose the rig's USB audio codec as the output device.
3. Click **Connect rig**.
4. Paste your Wavelog URL and API key (read/write), click **Load**, then pick a station profile.

## Layout
| Project | Purpose |
|---|---|
| `src/SstvStudio.Core` | Dependency-free encoder and decoder: modes and scan layouts, VIS, Morse, tone synthesizer, FM demodulator, `SstvDecoder`, WAV reader and writer |
| `src/SstvStudio.Hamlib` | rigctld client, `RigService` (polling/reconnect), PTT switches |
| `src/SstvStudio.Wavelog` | Wavelog REST client, ADIF writer, band plan |
| `src/SstvStudio` | Avalonia UI, SkiaSharp imaging, NAudio output, TX controller |
| `tests/SstvStudio.Tests` | xUnit tests: mode timing, VIS, synthesis, encode-to-decode round trips (every mode, clock error, noise), ADIF, and fake rigctld/Wavelog servers |

```powershell
dotnet build SstvStudio.slnx
dotnet test tests/SstvStudio.Tests
```

Settings are stored in `%APPDATA%\SstvStudio\settings.json`.

## License
SSTV Studio is free software: you can redistribute it and/or modify it under the terms of the
[GNU General Public License v3.0 or later](LICENSE). It comes with ABSOLUTELY NO WARRANTY.

Copyright © 2026 OM1PU. Third-party components and their licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Contributions are welcome under the terms in
[CONTRIBUTING.md](CONTRIBUTING.md).
