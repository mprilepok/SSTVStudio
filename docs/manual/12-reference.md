# 12. Reference

## 12.1 Common SSTV frequencies

These are popular calling frequencies; check your national band plan.

| Band | Frequency | Sideband |
|---|---|---|
| 80 m | 3.730–3.735 MHz | LSB (by custom) |
| 40 m | 7.165–7.171 MHz | LSB (by custom) |
| 20 m | **14.230 MHz** (also 14.233) | USB |
| 15 m | 21.340 MHz | USB |
| 10 m | 28.680 MHz | USB |
| 2 m | 145.500 MHz (also the ISS on 145.800) | FM |

## 12.2 Files and folders

| File | Location |
|---|---|
| Settings (including overlay profiles) | `settings.json` in the data folder (default `%APPDATA%\SstvStudio`) |
| Local QSO log (ADIF) | `log.adi` in the data folder |
| Earlier logs (after **Start new log**) | `log_<date>_<time>Z.adi` in the data folder |
| Settings before the last import | `settings-before-import.json` in the data folder |
| Last settings that loaded cleanly | `settings.json.bak` in the data folder |
| Unreadable settings, set aside | `settings-unreadable-<date>_<time>Z.json` in the data folder |
| Error log (for bug reports) | `errors.log` (and `errors.log.old`) in the data folder |
| Received pictures, audio, recordings | The **Save folder** (default `Pictures\SstvStudio`) |

**ⓘ → Open data folder** or **Settings → Open data folder** opens the data folder.

## 12.3 How SSTV sounds

| Tone | Meaning |
|---|---|
| 1900 Hz, steady | Leader at the start of the VIS header |
| 1100 / 1300 Hz | VIS data bits (they identify the mode) |
| 1200 Hz pulses | Line sync |
| 1500 → 2300 Hz | Picture brightness from black to white |

## 12.4 Advanced: environment variables

These are for special set-ups. Normal use doesn't need them. Set them before starting the program.

| Variable | Effect |
|---|---|
| `SSTVSTUDIO_DATA_DIR` | Use this folder instead of `%APPDATA%\SstvStudio` for settings and `log.adi`. Useful for a portable install, or to run a second copy with separate settings. |
| `SSTVSTUDIO_SIMULATED_INPUT` | Path to a WAV file (same sample rate as Settings). **Start listening** then plays that file in real time, looped, instead of opening the sound card. Useful for demonstrations or trying the receiver without a radio. |

Example (PowerShell):

```powershell
$env:SSTVSTUDIO_DATA_DIR = "D:\SstvStudio-portable"
dotnet run --project src/SstvStudio
```

---

[← 11. SSTV modes](11-modes.md) · [Contents](README.md)
