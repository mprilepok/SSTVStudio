# SSTV Studio — Roadmap and publishing tasks

Status on 2026-10-20: version 1.0.0 builds and passes 124 automated tests, with 1 skipped as a known issue. They
cover:
- encoder and decoder round trips for all 15 modes;
- real-world regressions;
- a reception benchmark;
- fake rigctld and Wavelog servers;
- headless UI tests.

The operator has used it on the air: Robot 36, Martin and Scottie pictures received, and Robot 36 transmitted on
14.230 MHz.

Legend: **P0** = must do before the first public release · **P1** = soon after · **P2** = later ideas.
🧑 = needs a decision from the author · ⚠️ = not yet verified with real hardware or services.

Guiding goal: SSTV Studio must be **easy to use and highly reliable** on the air. Prefer few controls, short plain
messages and actions that are safe to repeat, and guard every off-air failure with a test.

---

## Done on 2026-10-20

- [x] **Fix tuning hardened** (before the hardware test below):
  - Each picture remembers the dial it was received on, and the retune target is that dial ± the offset.
    Pressing the button twice, or on an older picture after a retune, no longer corrects twice.
  - The button is disabled while a picture is being received.
  - After `F`, the rig's frequency is read back, so rounding to a tuning step or a locked VFO is reported.
- [x] **Settings export / import** (Settings tab → BACKUP card). Import keeps the current settings as
  `settings-before-import.json` and rejects files that aren't settings exports.
- [x] **ADIF log export, and Start new log.** The old log is renamed to `log_<date>_<time>Z.adi` in the data folder
  and never deleted. The card shows the QSO count and has an "Open data folder" button.
- [x] **Picture zoom on both tabs:** Fit, a 25–400 % slider and a 1:1 button, remembered separately for Receive
  and Transmit. It replaces the shared 1:1 checkbox; a saved 1:1 setting becomes 100 % on both tabs.
  - Fixed an Avalonia layout problem along the way: the picture kept its old size when switching between Fit and
    a zoom.
  - The Transmit tab's header row is now aligned: the OVERLAY label, the profile chips, the zoom bar and the
    Scaling dropdown.
- [x] **Callsigns are upper case as you type or paste** (QSO callsign on both tabs, and My callsign), with the
  cursor kept in place.
- [x] **Reception benchmark** with fixed-seed HF conditions and PSNR floors. Its findings are listed under P1 →
  Reception.
- [x] User manual (section 2.7, sections 4.2 and 5.1 on zoom, and the files table) and `CLAUDE.md` updated.

Next session: the Robot 36 first-sync bug, then more sensitive VIS detection (P1 → Reception). Nothing from this
session is committed yet.

---

## P0 — Before the first public release

### Project and legal

- [ ] **Put the project under git** and push it to a hosting service (e.g. GitHub). 🧑 Choose a repository name and
  whether it's public from day one.
  Partly done: the repository exists, with an initial commit (`.gitignore`, `.gitattributes`, `LICENSE`) and the
  commit author set to "Michal OM1PU". The source code isn't committed yet and no remote is set.
  *Done when:* the source is committed and pushed to the remote.
- [x] **Choose a licence and add `LICENSE`.** GPL-3.0-or-later. `CONTRIBUTING.md` holds the relicensing grant for
  contributions (needed for a separate commercial edition), and `THIRD-PARTY-NOTICES.md` lists the dependencies. The
  README and the About window name the licence.
- [x] **Confirm the author and copyright.** `<Authors>` is "Michal OM1PU" and `<Copyright>` is "© 2026 Michal OM1PU"
  in `Directory.Build.props`; they appear on the splash and About screens. Source file headers, `CONTRIBUTING.md`
  and the README's licence section still say just "OM1PU".

### Hardware and service verification ⚠️

These paths are covered by automated tests against simulated rigs and servers, but haven't been checked against
real hardware or a real Wavelog instance:

- [ ] **Fix tuning** (`F <hz>` through rigctld) on a real rig, on USB and on LSB. Check the dial moves in the right
  direction and that the next picture reads "Tuning OK".
  Plan:
  1. Detune by +50 Hz and by −50 Hz, then press Fix tuning.
  2. Check that the "Retuned A → B" message shows the right frequencies.
  3. Press the button again; it must say "Already corrected".
  4. Repeat on LSB (with a second rig as the source) and in a data mode (PKTUSB).
  5. Check the refusals: FM, while transmitting, rig disconnected.
  6. Note how the rig handles RIT, split and its tuning step.
  7. Record the rig model and Hamlib backend with each result.
  If correct retunes often still show 10–15 Hz off, widen `Retune.ToleranceHz` to about 20 Hz.
- [ ] **PTT through serial RTS and DTR** with a real interface (only rigctld PTT has been used on the air).
- [ ] **Wavelog upload** to a real instance. Check the QSO shows up with the right band, mode (SSTV), reports and
  station profile, and test a wrong API key and an unreachable server.
- [ ] **Wavelog live CAT push** (`/api/radio`): check that Wavelog's QSO form follows the rig.
- [ ] **TX meters** on at least two rig models. Check which meters report `n/a`, and test the SWR trip with a
  deliberately bad match into a dummy load.
- [ ] **A long mode** (PD 180/240/290) transmitted end to end. Check the watchdog margin and PA temperature at real
  power.
- [ ] **Receive at 44.1 kHz** on real hardware (testing so far was at 48 kHz).

### Packaging and distribution

- [ ] **Release build**: `dotnet publish src/SstvStudio -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`.
  Check that it starts on a clean Windows machine without the .NET SDK, and that the NAudio and SkiaSharp native
  parts load.
- [ ] **Installer or portable ZIP.** 🧑 Choose one: a portable ZIP (simplest; `SSTVSTUDIO_DATA_DIR` already supports a
  portable data folder), an installer (Inno Setup / MSIX), or Velopack (installer plus auto-update; see P1).
  *Done when:* the package installs and uninstalls cleanly, and uninstalling leaves user data in place.
- [ ] **Code signing.** 🧑 Unsigned executables trigger Windows SmartScreen warnings. Choose between a code-signing
  certificate (costs money) and documenting the warning in the README.
- [ ] **Versioning**: take the version from the git tag (e.g. `v1.0.0`) so the About window, file properties and
  release always match. Add a `CHANGELOG.md`.

### Continuous integration

- [ ] **GitHub Actions workflow**: build and run `dotnet test` on every push and pull request, on a Windows runner.
  The headless UI tests need no display.
- [ ] **Release workflow**: on a `v*` tag, run `dotnet publish`, package, and attach the artifacts to a GitHub
  Release.

### Robustness

- [ ] **Crash and error log**: today an unexpected error on the receiver worker only goes to `Trace` and is
  invisible. Write unhandled exceptions (UI thread, worker, `TaskScheduler.UnobservedTaskException`) to
  `errors.log` in the data folder, and show a "something went wrong — see log" banner.
  *Done when:* a forced exception in the decoder appears in the log, and the app keeps receiving.
- [ ] **Settings safety**: today a corrupt `settings.json` silently resets to defaults, losing overlay profiles. Keep
  `settings.json.bak` from the last good load, and tell the user when a reset happens. (Manual export/import and the
  backup kept before an import exist now; an automatic backup of the last good load is still missing.)
- [ ] **Validate number fields in Settings** (port, PTT lead/tail, watchdog, ppm, SWR trip). Today typing text gives
  a red binding error, but the old value silently stays in use. Show the allowed range.

### Documentation for users

- [ ] **README for the public**: screenshots (Receive tab with waterfall, Transmit tab with overlay), download and
  install steps, supported modes, a link to the user manual, and the licence.
- [ ] **Known limitations** section in the README and manual (see "Known limitations" below).

---

## P1 — Soon after release

### Reception

- [x] **Reception benchmark** (`tests/SstvStudio.Tests/ReceptionBenchmark.cs`). Robot 36, Martin M2 and Scottie
  S2 are run through 9 simulated conditions with fixed noise seeds and scored by PSNR. Floors guard against
  regressions; known gaps are reported but not yet required. To see the table, run it with
  `--logger "console;verbosity=detailed"`.
  Findings on 2026-10-20:
  - **No header found at 2 dB SNR or less, or 8 dB deep in a fade**, although the picture data is still readable
    at 5 dB. The header detector is the weakest link.
  - **A steady carrier at half the signal's level ruins pictures:** 5–9 dB PSNR, and none at all at 2100 Hz.
  - **About 2 % of Robot 36 pictures are dropped at 5–10 dB SNR**, and ~1 in 48 Scottie S2 at 5 dB (see below).
- [ ] **Robot 36 first-sync false edge** (reliability bug). Robot's VIS stop bit runs into the first sync, making
  one long 1200 Hz tone. A 1 ms noise blip inside it looks like a sync end, the first pulse locks ~30 ms early,
  and the picture is dropped at the 8-period check. It's pinned by the skipped test
  `FirstSyncIsNotFooledByNoiseInTheVisStopBit`.
  Two tries so far:
  - Judging "high after the edge" over 3 ms instead of 1 ms fixed the pinned seeds, but broke manual start and
    weak fading pictures, because noise scored higher.
  - Measuring the 1 ms weight over 3 ms passed the tests, but made Scottie S2 with a carrier much worse
    (16.8 → 5.6 dB).

  Ideas: don't let a single pulse move the timing until a second pulse confirms it; or redo the 8-period check
  with the best-fitting phase before dropping the picture.
- [~] **Robust pixel values (weak signals): tried, no gain.** Weighting readings by signal power and clamping
  them to 1400–2400 Hz changed PSNR by ≤ 0.05 dB in every condition. In this regime the noise isn't spiky, so
  it was reverted.
- [ ] **More sensitive VIS detection** (top reception gain per the benchmark). For example, correlate whole
  30 ms bits instead of testing 10 ms blocks, and accept a header when the parity and the following sync agree.
- [ ] **Auto start without a VIS header.** Spot a run of regular sync pulses, work out the mode from their spacing,
  and start receiving automatically, so pictures whose header was lost in QRM or fading aren't missed.
- [ ] **Smooth more on noisy signals** (as slowrx does). Measure the signal-to-noise ratio, then average over longer
  stretches when it's noisy and keep pixels short when it's clean.
- [ ] **Adaptive notch filter** for steady interfering tones (a carrier, or CW in the passband) that leave stripes
  in the picture. The benchmark shows this matters more than expected: a carrier at half the signal's level costs
  7–15 dB PSNR, or the whole picture.
- [ ] **Keep the gallery across restarts**: received pictures are lost from the gallery when the app closes, although
  their PNG/WAV files remain. Reload the newest N from the save folder at start-up.
- [ ] **FSK ID decoding**: MMSSTV and many other programs send the operator's callsign as a short FSK burst after the
  picture. Decode it and offer to fill the QSO callsign automatically. This saves typing on every QSO.
- [ ] **More modes**: Martin M3/M4, Scottie S3/S4, Pasokon P3/P5/P7, Robot 8/24 B/W, MMSSTV MP/MR/ML. Each needs a
  `ScanLayout`, an encoder and round-trip tests.
- [ ] **Weak-signal sync**: slowrx-style Hough-transform slant detection over the whole picture, as a final
  re-render, would improve pictures where few sync pulses lock.
- [ ] **Edge columns**: a thin dark or green strip at the far left and right of decoded pictures (filter smearing into
  the porch/separator). Trim or compensate.
- [ ] **Colour range in Robot/PD**: we use MMSSTV's studio-range YCrCb. Pictures from encoders that use full range
  (e.g. PySSTV) come out slightly flat (≈ 8 levels). Consider auto-detecting from black/white levels, or an option.
- [ ] **Picture timestamps from WAV files** come from decode time. Use the recording's start time (from the file
  name, if it follows our `rx_…Z.wav` pattern) plus the offset in the file.

### Transmit

- [ ] **FSK ID transmit** (MMSSTV-compatible), so other stations' software fills in your callsign automatically.
- [ ] **TX picture library**: a strip of recent or favourite pictures for one-click selection, instead of a file
  dialog every time.
- [ ] **Overlay images and logos**: allow a PNG layer (club logo, QSL-style frame) in overlay profiles, not only
  text.
- [ ] **Drag and drop** a picture file onto the preview, and paste from the clipboard.

### Logging and Wavelog

- [ ] **Callsign lookup** through Wavelog's private-lookup API, to fill name and locator and show "worked before".
  Check the endpoint and its fields against the Wavelog version in use.
- [ ] **Duplicate warning** before logging the same call on the same band within N minutes.
- [ ] **Retry queue**: QSOs that failed to upload (Wavelog unreachable) are only in `log.adi`. Queue them and retry
  automatically, with a visible "n waiting" counter.

### Audio and devices

- [ ] **WASAPI device selection**: WinMM truncates device names to 31 characters, so two similar USB codecs can look
  the same. Use WASAPI endpoint IDs, keeping WinMM as a fallback.
- [ ] **Audio device lost or replugged**: detect it and restart listening automatically, instead of only showing an
  error.

### Code health

- [ ] **Split `MainWindowViewModel`** (≈ 750 lines) into Transmit, Rig/Meters and Settings view models, following the
  pattern of `QsoViewModel` and `ReceiveViewModel`.
- [ ] **Auto-update** (Velopack or similar), if an installer was chosen in P0.

---

## P2 — Later ideas

- [ ] **Linux and macOS**: Avalonia already runs there; only audio needs a cross-platform backend (e.g. PortAudio
  or miniaudio). WAV decode and export already work everywhere.
- [ ] **Localisation**: Slovak and other languages for the UI and manual.
- [ ] **Digital SSTV** (DRM / EasyPal / KG-STV compatible). A large separate project.
- [ ] **Beacon / auto-reply mode**: send a picture on a schedule or reply automatically. Check licensing rules
  (unattended operation) before building it.
- [ ] **Several rig profiles** (e.g. HF rig plus a 2 m rig for ISS events), switchable in the header.
- [ ] **Satellite and ISS helper**: Doppler correction for 2 m FM SSTV events, using rigctld frequency control.
- [ ] **Automatic TX level from ALC.** The user sets the maximum ALC, and the app adjusts the TX audio level to keep
  ALC there.
  - **Settings:** `Automatic TX level`, `Max ALC` (default 10 %) and `Highest level allowed` (a safety ceiling).
  - **At the start of each transmission:** use the steady leader and VIS tone to find the level. Raise it in about
    0.5 dB steps until ALC reaches the maximum, then back off one step.
  - **During it:** average the last 3 ALC readings, cut about 1 dB when over the maximum, and adjust at most every
    600 ms. Stay between 5 % and the ceiling. It never stops the transmission.
  - **After it:** remember the level per band. Show "Auto level 70 → 62 %" in the TX summary.
  - **No ALC from the rig:** switch off with a clear message.
  - **The user stays in control:** moving the TX level slider by hand turns auto off for that transmission.
  - **Code:** the controller is a separate class with no UI. Unit tests use a simulated rig (slow meter, missing
    readings, the ceiling), and the fake rigctld gets an ALC that follows the level.
  - **Risks:** ALC scales differently on each rig (calibrate the maximum against the rig's own ALC zone). Some rigs
    smooth the meter, and some read 0 until over-driven.
  - ⚠️ Verify on at least two rig models.
  - 🧑 Decide: raise as well as lower (recommended), per-band memory (recommended), and that touching the slider by
    hand turns auto off (recommended).
- [ ] **Ctrl + mouse wheel zoom** over the picture on both tabs, using the same zoom setting as the slider.
- [ ] **Accessibility**: keyboard shortcuts (F5 = TX, Esc = stop), screen-reader names for all controls.

---

## Known limitations (for the README and manual)

- Live audio input and output are Windows-only. WAV decode and export work on any OS.
- Reception needs the VIS header; without it, use **Tools → Start now** (manual start) or **Decode WAV…** with a
  forced mode.
- "1:1" view is exact at 100 % Windows display scaling; at higher scaling it's enlarged by that factor.
- Pictures decoded from WAV files are timestamped with decode time.
- Robot/PD colours differ by a few levels from decoders that use full-range YCrCb.
