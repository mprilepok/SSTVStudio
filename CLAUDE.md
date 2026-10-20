# CLAUDE.md

## Commands

```powershell
dotnet build SstvStudio.slnx
dotnet run --project src/SstvStudio
dotnet test tests/SstvStudio.Tests
dotnet test tests/SstvStudio.Tests --filter "FullyQualifiedName~EncoderTests.ImageDuration_MatchesSpecification"
```

## Architecture

- **`SstvStudio.Core`**: has no dependencies. `SstvMode` subclasses (`MartinMode`, `ScottieMode`, `RobotMode`, `PdMode`,
  `WraaseMode`) lazily yield `Tone(freq, ms)` per pixel. `SstvEncoder` adds the preamble, VIS and CW ID.
  `ToneSynthesizer` streams the tones as float samples. It tracks segment ends as *cumulative* fractional sample
  positions, so never round per pixel; doing so would cause slant. The registry is `SstvModes.All`.
- **Decoding (`SstvStudio.Core/Decoding`)**: each mode's `Layout` (`ScanLayout`) describes one sync period for the
  receiver. Segment offsets are relative to the start of the sync pulse; Scottie's are negative because G and B come
  before the sync. The decoder works in four stages:
  1. `FmDemodulator` mixes 1700 Hz down to baseband, filters to ±800 Hz, decimates to ~12 kHz and runs a
     phase-difference discriminator.
  2. `VisDetector` averages 5 ms blocks and judges each 30 ms element on its 4 inner blocks. In noise the tones are
     **squeezed toward mid-band** (a real weak Robot 36 read 1781 Hz for the 1900 Hz leader and ~1250 Hz for the
     1200 Hz sync bits), so the header is judged against itself, never against fixed frequencies:
     - start and stop bits must agree and lie 350–900 Hz below the leader median;
     - data bits split at their largest gap;
     - the bit threshold may not lie below the sync level (that rejects misaligned matches);
     - parity, and only codes of known modes (any code when `ForcedMode` is set).

     Measured: 100 / 86 / 68 / 46 % of headers found at 3 / 2 / 1 / 0 dB SNR, versus 53 / 10 / 0 / 0 % for the old
     detector; no false starts in 40 minutes of noise, and about 1 wrong-mode picture per 300 weak headers (others
     are abandoned). Tests: `DecodesRealWeakHeader` (real off-air fixture in `tests/.../Data`), `FindsWeakHeaders`,
     `NoFalseStartsInFiveMinutesOfNoise`. A median per block was tried and was far worse.
  3. `SstvDecoder` finds each sync-pulse *end* edge near its predicted position (±25 ms until two pulses are
     locked, then ±8 ms). It fits `syncEnd = a + b·period`, using least squares with outlier rejection once there
     are 6 or more points and capping slant at ±2 %.
  4. Lines are drawn as they arrive, and `Finish` redraws everything with the final fit. A header followed by fewer
     than 3 syncs in the first 8 periods is treated as a false trigger.
  Reception ends early in two cases, so the next picture is never missed:
  - Signal lost: for each period the decoder computes a *presence* value, the sync score minus the score of a
    control window half a period later. If the median presence over about 6 s (`SignalLostMs`) falls below
    `PresenceMargin` (0.15), the signal is gone. Noise has a median of about 0; any signal strong enough for its VIS
    to be detected has 0.59 or more. Don't go back to "N periods without a locked sync": weak or fading HF pictures
    rarely lock, and that rule cut real pictures short. The `KeepsReceivingWeakFadingPicture` test guards this.
    The same presence median is used for the false-trigger check. Pulses with a score of `LockScore` (0.7) or more
    are used for timing.
  - A new VIS heard mid-picture means a new transmission. A VIS can't appear in picture data, because its bits need
    tones below 1500 Hz.
  Either way `EndInterrupted` delivers the picture as partial, keeping lines up to the last sync and blanking the rest.
  Tracking rules (each one fixed a real off-air failure; tests: `TracksLargeSenderClockError`,
  `KeepsReceivingWeakFadingPicture`, `FirstSyncIsNotFooledByNoiseInTheVisStopBit`):
  - **A lone pulse never moves the timing.** Until two locked pulses agree (`Confirm()`: whole periods apart within
    2 ms + `MaxSlant` × period × gap), timing stays on the VIS prediction with the wide window, and
    non-fitting pulses are dropped once a pair agrees. A noise blip in Robot's VIS stop bit, which runs into the
    first sync, used to re-anchor everything ~30 ms early and drop ~2 % of pictures. Manual start is the
    exception: its first pulse sets the phase.
  - The line period is fitted from the confirming pair on. Never assume nominal timing until 6 pulses:
    a real Martin M1 sender was 1 % slow (450.8 ms lines), and nominal timing lost it by line 2.
  - The sync search half-width is `SearchWindowMs()` = 10 ms + `MaxSlant` (3 %) × period × periods since the last
    lock (25 ms base until confirmed), capped at 0.4 × period.
  - Presence uses its own window (`LockedWindowMs` once confirmed, `InitialWindowMs` before), the same width for
    sync and control. If the wide search window is used here, noise looks present; if a narrow control is paired with a wide
    search, the trailing noise is kept.
  - `Corrected(hz)` = hz × `Scale` − `_tuningOffsetHz`. A clock error scales tones by the same factor as time,
    and the residual tuning offset is the median sync tone minus 1200 Hz.
  Reception aids:
  - `VisDetector` takes the tuning offset (up to ±250 Hz) as the average of the leader's and the sync bits'
    offsets, whose squeeze errors cancel. `Begin` seeds `_tuningOffsetHz` with that offset, and the sync threshold
    follows it (`SyncThreshold`).
  - `StartManual(mode)` starts with the phase unknown: it searches whole periods without counting them until the
    first pulse locks, with no false-trigger check.
  - `ForcedMode` decodes every header as the given mode. `DetectVis = false` ignores headers.
  - `ShiftMs` and `SlantPpm` affect drawing only (`RenderAt`), never tracking.
  - `SpectrumAnalyzer` feeds the waterfall. `ImageFilters.Denoise` is the median-plus-smooth noise filter.
  Robot 36 tells R-Y from B-Y lines by its separator tone. When changing timing, run `DecoderTests`; they round-trip
  every mode and take about a minute. Every decoder change must also pass `ReceptionBenchmark`: fixed-seed HF
  conditions scored by PSNR, with per-condition floors. Measure first: a plausible idea (power-weighted pixels)
  gained nothing, and a sync-edge fix that helped one case hurt others. Edit decoder files with the Edit tool,
  not PowerShell `Get-Content`/`Set-Content`, which can mangle UTF-8 in Windows PowerShell 5.1.
- **`SstvStudio.Hamlib`**: `RigctldClient` uses the same 150 ms idle-window heuristic as `../hamlibui`
  (some rigctld emulators omit `RPRT 0`). `RigService` polls f/m/t and `l RFPOWER` every second and reconnects after 5 s.
  While `Transmitting` is set (or the rig reports PTT) it reads only the TX meters (`TxMeters`) every 200 ms:
  `RFPOWER_METER_WATTS`, falling back to `RFPOWER_METER`, then `SWR`, `ALC` and `ID_METER`. A level that answers
  `RPRT` with an error is cached as unsupported until the next reconnect. Get commands pass `expectedLines`, so
  they return without waiting out the idle window.
  `PttSwitch.Create` returns a rigctld, serial RTS/DTR or VOX switch.
- **`SstvStudio.Wavelog`**: `WavelogClient` always calls `{base}/index.php/api/...`. `Adif.ToRecord` writes
  `MODE=SSTV` and puts the SSTV sub-mode in `COMMENT`.
- **`SstvStudio`** (Avalonia 12, CommunityToolkit.Mvvm, compiled bindings): `MainWindowViewModel` binds settings
  directly through `Settings.*`; `AppSettings` is an `ObservableObject` stored as JSON. `TransmitController` owns
  the PTT → lead → play → tail → un-key sequence and the watchdog. `AudioOutput` uses NAudio `WaveOutEvent` and
  P/Invokes `waveOutGetDevCaps`, because NAudio.WinMM 2.x does not expose output-device enumeration.
  `ReceiveService` runs the decoder on one worker fed through a `Channel<Action>`. That worker is the only
  thread that touches the decoder; the UI polls `ActiveDecoder` (`Snapshot`, `LineCount`, `LevelDb`) with a 200 ms
  timer. `ReceiveService.Feed` also appends every chunk to an `AudioClipBuffer`. While idle it keeps only 3 s of pre-roll;
  from `ImageStarted` to `ImageCompleted` it keeps everything, and that clip is handed out as `ReceivedAudio`. The
  `● Record` recorder (`WavFileWriter`, which patches the header on dispose) is opened and closed through the worker
  queue as well. `ReceiveViewModel` drives the Receive tab, and `MainWindowViewModel.OnIsTransmittingChanged` mutes it.
  Overlay text comes from `AppSettings.OverlayProfiles`: a list of `OverlayProfile`, each holding `OverlayLine`s
  (`Services/OverlayProfile.cs`). `MainWindowViewModel` watches the selected profile and every one of its lines, and
  re-renders on any change. `AppSettings.Migrate()` seeds the CQ / Reply / 73 defaults and turns a customised
  legacy `OverlayTop`/`OverlayBottom` into a "My overlay" profile. `ImageComposer` stacks the lines for each position
  and shrinks any line too wide to fit.
  The Transmit tab picks the profile in a ComboBox (`OverlayProfileBox`), and Settings → OVERLAY PROFILES reorders
  `OverlayProfiles` in place (`MoveProfileUp/DownCommand`, `ObservableCollection.Move`). Notes on the ComboBox:
  - Bind it by **item** (`SelectedProfileChoice`, which ignores null), never by index. When the list changes, the
    ComboBox shifts its index after we've corrected it, and a stale index selected the wrong profile; this was
    also seen on settings import.
  - It drops its selection when the selected item moves, and the binding won't resend the same value, so
    `MainWindow` puts the selection back after every collection change. Test:
    `OverlayDropdownFollowsTheOrderSetInSettings`.
  Live audio comes in through `IAudioInput`: `AudioInput` (WinMM) or, when the environment variable
  `SSTVSTUDIO_SIMULATED_INPUT` points to a WAV, `SimulatedAudioInput`. The simulated input plays that file in real
  time as if it were the sound card, which is the way to test live reception without a radio. Both report
  `Shortfall` (lost audio), shown as a ⚠ warning in the RECEIVER card together with decoder backlog.
  Start-up: `App` shows `SplashWindow` first, builds `MainWindowViewModel`/`MainWindow` behind it (at least
  1.5 s), then swaps them. `AboutWindow` and the splash read `Services/AppInfo.cs`, which takes product, version,
  description and copyright from the assembly attributes. **Bump `<Version>` in `src/SstvStudio/SstvStudio.csproj`.**
  `Assets/logo.png` and `Assets/app.ico` (16–256 px) are generated artwork: colour bars over an SSTV signal.
  "Fix tuning": `ReceiveViewModel.RetuneRequested` is handled by `MainWindowViewModel.RetuneAsync`, which uses
  `Retune.Plan` (`Services/Retune.cs`) and `RigService.RetunedFrequency` (USB: dial + offset; LSB: dial − offset;
  other modes refused), then `SetFrequencyAsync` (`F <hz>`, then reads `f` back to catch rounding or a locked VFO).
  The target is computed from the dial the picture was received on (`ReceivedPicture.Dial`, captured at
  `ImageStarted` and dropped if it changed by the end, or for WAV files), never from the current dial, so the
  correction can't be applied twice. The button is disabled while a picture is being received. Re-decoding goes through `ReceiveService.ReDecodeAsync` (off the worker; with a
  forced mode it falls back to a manual start when there's no header). `DecodeFileAsync(path, DecodeOptions)` does
  the same for WAV files.
  Backup (Settings tab): `AppSettings.ExportTo` / `ReadExport` (rejects JSON without `MyCall`) / `CopyFrom`
  (reflection over public settable non-`[JsonIgnore]` properties, except `OverlayProfiles`, which
  `MainWindowViewModel.ReplaceProfiles` swaps while keeping one selected). Import first writes
  `settings-before-import.json`. `QsoLogger.ExportLog` / `StartNewLog` take the log path. A new log renames
  `log.adi` to `log_yyyyMMdd_HHmmssZ.adi` and never deletes it. Tests that touch `AppSettings` or `QsoLogger`
  belong in `UiTests`: its static `DataDir` sets `SSTVSTUDIO_DATA_DIR` before `AppSettings.DataDirectory` is
  first read.
  Reliability:
  - `ErrorLog` (`Services/ErrorLog.cs`) appends unexpected errors to `errors.log` in the data folder. It starts
    afresh at 1 MB, keeping `.old`, and writes the same error repeated within 10 s only once. Hooks:
    `Dispatcher.UIThread.UnhandledException` (marked handled, so the app survives) in `App`; unhandled and
    unobserved-task exceptions in `Program`; the `ReceiveService` worker, which logs and goes on to the next
    chunk. `ErrorLog.Logged` shows the top banner with **Open log**.
  - `AppSettings.Load` copies a cleanly loaded `settings.json` to `settings.json.bak`. An unreadable file is
    renamed to `settings-unreadable-<time>Z.json`, the backup (or defaults) is used, and `LoadProblem` is shown in
    the banner. `Save` writes a `.tmp` file and swaps it in.
  - Number fields on the Settings tab bind to `MainWindowViewModel.Numbers` (`NumberField`): text that's out of
    range throws in the setter, and Avalonia shows the range under the field. Don't use a converter that returns
    or throws: compiled bindings in Avalonia 12 then show a raw cast exception.
  Tabs are Receive, Transmit, Settings (`MainWindowViewModel.ReceiveTab`, `TransmitTab`, `SettingsTab`), and the app
  opens on Receive. The QSO being worked lives in `QsoViewModel` (exposed as `MainWindowViewModel.Qso` and
  `ReceiveViewModel.Qso`). Both tabs' QSO forms bind `Qso.*`; the main view model redraws the overlay when a QSO
  field changes, and logging is `Qso.LogCommand`.
  `ImageComposer` uses SkiaSharp. Keep its version in step with the one Avalonia.Skia references (3.119.4).

## License

GPL-3.0-or-later (`LICENSE`). OM1PU holds the copyright and plans to sell some features separately under a
commercial license, so contributions come under the relicensing grant in `CONTRIBUTING.md`. Every new `.cs` file
starts with `// SPDX-License-Identifier: GPL-3.0-or-later` and `// Copyright (C) 2026 OM1PU`. When you add a
bundled dependency, list it in `THIRD-PARTY-NOTICES.md` and in `AppInfo.Credits`. Don't port code from GPL-only
projects such as QSSTV: it couldn't go into a commercial edition.

## UI rules and UI tests

- **Picture zoom** (`ZoomViewModel`, one per tab, stored as `RxZoomPercent`/`TxZoomPercent`, where 0 = Fit): the
  viewer is a `ScrollViewer.zoom` holding a `LayoutTransformControl`. Avalonia doesn't re-measure that control when
  the image switches between fitted and zoomed, so `MainWindow.RemeasureZoomViewers` does it on every change. The
  `ZoomShowsPicturesFittedOrAtTheChosenSize` test guards this.

- **Never bind a ComboBox `SelectedItem` two-way to an enum through `$parent[...]`.** When a tab is detached, the
  ComboBox reports a null selection, and writing that into the enum throws InvalidCastException (seen with the noise
  filter). Bind `SelectedIndex` to an int property on the local view model and ignore −1 (`NoiseFilterIndex`).
- **UI tests run headless** (`UiTests.cs`, `Avalonia.Headless`, `HeadlessUnitTestSession`). They use the real
  `MainWindow` and view models with `SSTVSTUDIO_DATA_DIR` pointing at a temp folder, so the operator's settings
  (auto-listen, rig auto-connect) are never used.
- **Check UI changes in a render, not only by asserts.** In a headless test, `window.CaptureRenderedFrame()` saved
  as a PNG shows what a user sees. This caught misaligned rows and an exception dump shown as a validation message,
  where the asserts had passed.
- **Timing-sensitive tests wait for a condition, not a fixed delay.** `ReceptionBenchmark` runs in its own
  collection with parallel test runs disabled, because its parallel decoding starved the fake-rigctld tests.
- **Never test a live window with global mouse or keyboard injection.** The operator's own instance is often in
  front and on the air. To try live reception without a radio, use `SSTVSTUDIO_SIMULATED_INPUT` together with
  `SSTVSTUDIO_DATA_DIR`.

## Verifying encoder changes

Unit tests check timing against the spec. For an end-to-end check, render WAVs and decode them with the PyPI
`sstv` package (`sstv.decode_from_wav`), which auto-detects the VIS. Every mode currently decodes with the correct
mode and colours. Going the other way, our decoder reads PySSTV-generated WAVs correctly. For Robot and PD, primaries decode around 237 instead of 255, because the encoder uses
MMSSTV's studio-range YCrCb.
