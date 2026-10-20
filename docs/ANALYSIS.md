# SSTV application analysis

Survey of existing SSTV implementations, what they do well and badly, and the
design decisions that follow for **SSTV Studio** (this repository).

## 1. Existing applications

| App | Platform / stack | TX | RX | Rig control | Logging | Status |
|---|---|---|---|---|---|---|
| **MMSSTV** (JE3HHT) | Win32, Borland C++ Builder | ✅ ~40 modes | ✅ best-in-class (auto slant, sync) | Serial RTS/DTR PTT, limited CAT via MMSSTV "radio command" | none (external via plugins) | Frozen since ~2010; still the reference |
| **QSSTV** (ON4QZ) | Linux, Qt5/C++ | ✅ analog + DRM digital (HamDRM) | ✅ | **Hamlib** (direct lib), PTT via CAT/RTS/DTR | none | Active-ish; Linux only, Qt5 |
| **RX-SSTV** | Win32 | ❌ | ✅ (MMSSTV engine) | none | none | RX only (ISS events) |
| **Black Cat SSTV** | macOS / Windows, Xojo | ✅ | ✅ | serial PTT, some CAT | basic | Commercial-ish, closed |
| **Robot36 / SSTV Encoder** (xdsopl) | Android, Java/C (RenderScript→NEON) | separate app | ✅ | none | none | Mobile, field use / ISS |
| **slowrx** (OH2EIQ) | Linux, GTK/C | ❌ | ✅ | none | none | Small, readable decoder |
| **PySSTV** (dnet) | Python | ✅ (WAV only) | ❌ | none | none | Clear reference for mode timings |
| **EasyPal / KG-STV** | Windows | digital (DRM / MSK) | ✅ | PTT | none | Digital SSTV, not analog |
| **JVComm32** | Win32 | ✅ | ✅ | serial PTT | none | Shareware, abandoned |

### Common strengths worth keeping
- **Mode coverage.** Martin, Scottie, Robot, PD and Wraase cover >95 % of HF/VHF
  and ISS traffic. MMSSTV defines the de-facto timing table; PySSTV and slowrx
  document it in readable code.
- **VIS header** auto-detection on RX — so TX *must* always send a standards-
  compliant VIS (300 ms leader / 10 ms break / 300 ms leader / start / 7 bits
  LSB-first / even parity / stop).
- **Overlay templates** (MMSSTV "template" layer): callsign, RST and greeting
  baked onto the picture is how SSTV QSOs are actually exchanged.
- **CW ID** after the picture — required in several licence regimes.

### Common weaknesses (the gap this app fills)
1. **No modern rig control.** MMSSTV relies on RTS/DTR or a fixed command string;
   only QSSTV links Hamlib, and only on Linux. On Windows the standard today is
   `rigctld` (Hamlib daemon) shared between WSJT-X, logbook, and other apps — none
   of the Windows SSTV apps speak it.
2. **No logging integration.** Nobody pushes QSOs to a cloud/self-hosted logbook.
   **Wavelog** (Cloudlog fork, PHP) has a stable REST API (`/api/qso`,
   `/api/station_info`, `/api/radio`) used by WSJT-X bridges (WaveLogGate) — SSTV
   is left out.
3. **Dated UI / DPI handling.** Win32 apps render blurry at 150–200 % scaling,
   lack dark theme, and use modal dialogs for everything.
4. **Timing drift handling is manual.** Sound-card clock error produces slant;
   MMSSTV offers a calibration value, others don't. Encoder must accumulate time
   in floating point (not per-pixel rounded samples) or slant is self-inflicted.
5. **Unsafe PTT handling.** Crashes during TX can leave the rig keyed. A TX
   watchdog and guaranteed un-key on exit are rare.

## 2. Protocol reference used by the encoder

Frequencies: sync **1200 Hz**, black **1500 Hz**, white **2300 Hz**,
`f = 1500 + 800·v/255`.

| Mode | VIS | Size | Line structure (ms) | Total |
|---|---|---|---|---|
| Martin M1 | 44 | 320×256 | sync 4.862 · porch 0.572 · **G** 146.432 · 0.572 · **B** · 0.572 · **R** · 0.572 | 114.3 s |
| Martin M2 | 40 | 320×256 | as M1, scan 73.216 | 58.1 s |
| Scottie S1 | 60 | 320×256 | (first line: sync 9) · 1.5 · **G** 138.240 · 1.5 · **B** · sync 9 · 1.5 · **R** | 109.6 s |
| Scottie S2 | 56 | 320×256 | as S1, scan 88.064 | 71.1 s |
| Scottie DX | 76 | 320×256 | as S1, scan 345.6 | 268.9 s |
| Robot 36 | 8 | 320×240 | sync 9 · porch 3 · **Y** 88 · sep 4.5 (1500 even / 2300 odd) · porch 1.5 @1900 · **R‑Y / B‑Y** 44 | 36 s |
| Robot 72 | 12 | 320×240 | sync 9 · 3 · **Y** 138 · 4.5 · 1.5 · **R‑Y** 69 · 4.5 · 1.5 · **B‑Y** 69 | 72 s |
| PD 50/90/120/160/180/240/290 | 93/99/95/98/96/97/94 | 320×256 … 800×616 | per *line pair*: sync 20 · porch 2.08 · **Y₀ · R‑Y · B‑Y · Y₁** (chroma averaged) | 50 … 289 s |
| Wraase SC2‑180 | 55 | 320×256 | sync 5.5225 · porch 0.5 · **R G B** 235 each | 182 s |

YCrCb (Robot / PD), as used by MMSSTV/QSSTV:
`Y = 16 + (65.738R + 129.057G + 25.064B)/256`,
`R‑Y = 128 + (112.439R − 94.154G − 18.285B)/256`,
`B‑Y = 128 + (−37.945R − 74.494G + 112.439B)/256`.

## 3. Integration reference

**Hamlib rigctld** (TCP 4532): `f` get freq, `m` get mode (+passband), `t` get
PTT, `T 1`/`T 0` set PTT. Some rigctld emulators omit `RPRT 0` on success, so
the client reuses the idle-window heuristic proven in `../hamlibui`.

**Wavelog API** (base URL + `/index.php/api/…`):
- `POST /api/qso` — `{"key","station_profile_id","type":"adif","string":"<ADIF record>"}` → `201 created`
- `GET  /api/station_info/{key}` — list of station profiles (id, name, callsign, grid)
- `POST /api/radio` — `{"key","radio","frequency","mode"}` live CAT state (same as WaveLogGate)

ADIF mode is `SSTV` (official ADIF enumeration); the SSTV sub-mode name goes in
`COMMENT`. Reports use RSV (e.g. `595`).

## 4. Resulting design — SSTV Studio

| Concern | Decision |
|---|---|
| Runtime / UI | .NET 10, **Avalonia 12** (Fluent, dark), MVVM via CommunityToolkit.Mvvm — same stack as `hamlibui` |
| Encoder | Pure C#, dependency-free `SstvStudio.Core`; lazy `IEnumerable<Tone>` so 5-minute PD290 never needs a 50 MB buffer |
| Synthesis | Phase-continuous oscillator, **cumulative fractional timing** (no per-pixel rounding drift), ± ppm sound-card clock correction, 5 ms start/end ramps |
| Audio out | NAudio WinMM (selectable USB codec), plus WAV export (works anywhere) |
| Imaging | SkiaSharp (already shipped by Avalonia): fit/fill scaling, test pattern, two overlay text lines with placeholders `{CALL} {MYCALL} {RST} {NAME} {MODE} {DATE} {TIME} {FREQ}` |
| Rig control | `rigctld` TCP client, 1 s poll, auto-reconnect; PTT via rigctld, serial RTS/DTR, or VOX |
| TX safety | PTT lead/tail delay, **TX watchdog** (max seconds), polling paused during TX, un-key in `finally` and on window close |
| Logging | Wavelog `/api/qso` with station profile picker, optional `/api/radio` push, **local ADIF backup** (`log.adi`) written before upload |
| Testability | xUnit: mode durations vs spec, VIS bits/parity, synthesizer sample counts & drift, ADIF format, band mapping |

### Out of scope (v1) / roadmap
- ~~RX decoder~~: implemented in `SstvStudio.Core/Decoding` (VIS auto-detect, sync-fit slant correction).
- Manual RX start without a VIS header, and auto-slant across a missed header.
- Digital SSTV (DRM/HamDRM).
- Linux/macOS audio backend (PortAudio / miniaudio); WAV export already cross-platform.
- Image gallery / template designer with drag-and-drop layers.
