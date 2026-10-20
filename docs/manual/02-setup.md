# 2. First-time setup

Open the **Settings** tab and go through the cards from top to bottom. Click **Save settings** when you're done.
Settings are also saved automatically when you transmit and when you close the program.

Number fields are checked as you type. If a value isn't a number in the allowed range, the field turns red and shows
the range, e.g. "Whole number, 1 to 65535". The previous value stays in use until you correct it. Decimals can be
typed with a dot or a comma.

> 📷 **Screenshot [S2]** `images/s02-settings-tab.png`: the **Settings** tab with all cards visible and filled in
> (your own callsign, rig connected). Blank out or blur the Wavelog API key.

## 2.1 Station

| Field | What to enter |
|---|---|
| **My callsign** | Your callsign. Used by `{MYCALL}`, the CW ID and the log. It's always shown in capitals, whatever you type. |
| **My locator** | Your Maidenhead locator, e.g. `JN88PC`. Used by `{MYGRID}` and the log. |
| **TX power (W)** | Power written to the log. It's also the full-scale value of the power meter. |
| **CW ID speed (WPM)** | Speed of the optional Morse ID after each picture. |

## 2.2 Rig control (Hamlib rigctld)

SSTV Studio talks to **rigctld**, the Hamlib network daemon, so other programs (WSJT-X, your logger, …) can share
the rig at the same time.

1. Start rigctld for your rig, for example an IC-7300 on COM5:
   ```powershell
   rigctld -m 3073 -r COM5 -s 115200
   ```
   Run `rigctld -l` to find your rig's model number.
2. Enter **Host** (usually `127.0.0.1`) and **Port** (default `4532`).
3. Click **Connect rig** at the top right. The frequency, mode and a green **Connected** badge appear in the header.
4. Optionally tick **Connect on startup**.

> 📷 **Screenshot [S3]** `images/s03-header-connected.png`: a crop of the header only, with the rig connected:
> frequency, mode and the green **Connected** badge.

If the connection drops, the program retries every 5 seconds by itself.

## 2.3 PTT

| PTT method | When to use it |
|---|---|
| **Rigctld** | Recommended. The rig is keyed with a CAT command through rigctld. |
| **SerialRts / SerialDtr** | Your interface keys the rig through the RTS or DTR line of a serial port. Choose the port next to it. |
| **Vox** | The rig keys itself on audio. Nothing is sent. |

| Setting | Default | Meaning |
|---|---|---|
| **PTT lead (ms)** | 150 | Wait between keying and starting the audio, so the rig is fully in TX. |
| **PTT tail (ms)** | 150 | Wait after the audio before un-keying. |
| **TX watchdog (s)** | 330 | Hard limit on one transmission. It must be longer than the slowest mode you use (PD 290 ≈ 290 s). |
| **SWR trip (0 = off)** | 3.0 | Transmission stops when SWR reaches this value on two readings in a row. |

## 2.4 Audio output (transmit)

| Setting | What to choose |
|---|---|
| **Device (rig USB codec)** | The rig's sound card, often called "USB Audio CODEC". Click **Refresh** after plugging it in. |
| **Sample rate** | 48000 (recommended) or 44100. Used for both transmit and receive. |
| **Clock correction (ppm)** | Leave at 0 unless other stations report slanted pictures; see [9.3](09-clean-signal.md#93-slant-and-clock-correction). |

## 2.5 Receive

| Setting | Meaning |
|---|---|
| **Input device (rig USB codec)** | The sound card the rig's receive audio comes in on. |
| **Start listening on launch** | Begin receiving as soon as the program starts. |
| **Auto-save received pictures** | Save every received picture as PNG in the save folder. |
| **…with their audio (WAV)** | Also save each picture's audio next to its PNG. |
| **Save folder** | Where pictures, audio and recordings go. Default: `Pictures\SstvStudio`. |

After changing the input device, stop and start listening again.

## 2.6 Wavelog

1. In Wavelog, create an API key with **read/write** rights. It's under your account menu → API keys.
2. Enter the **Wavelog URL**, e.g. `https://log.example.com`. Don't add `/index.php`; the program adds it.
3. Paste the **API key (read/write)**.
4. Click **Load**. Your station profiles appear in the list; pick the one you're operating from. If your callsign
   and locator are still empty, they're filled in from the chosen profile.
5. Optional: tick **Push rig frequency/mode to Wavelog (live CAT)**. Wavelog's QSO form then follows your rig, the
   same way it does with WaveLogGate.

## 2.7 Backup: settings and log

The **BACKUP · SETTINGS AND LOG** card on the Settings tab:

- **Export settings…** saves all settings, including overlay profiles, to a `.json` file. Use it as a backup or
  to move to another PC. The file contains your Wavelog API key, so keep it private.
- **Import settings…** replaces all settings with an exported file. Your current settings are first kept as
  `settings-before-import.json` in the data folder, so you can import them back. Afterwards, reconnect the rig and
  restart listening so the imported devices are used.
- **Export log (ADIF)…** saves a copy of the local log `log.adi`, e.g. to import into another logbook. The line
  above the buttons shows how many QSOs it holds.
- **Start new log** begins an empty log, e.g. for a new year or a contest. The old log is never deleted: it's
  renamed to `log_<date>_<time>Z.adi` in the data folder.
- **Open data folder** shows the folder with `settings.json`, the current log and the old logs.

> 📷 **Screenshot [S4]** `images/s04-backup-card.png`: a crop of the **BACKUP · SETTINGS AND LOG** card, with the
> QSO count line showing (e.g. "Log: 12 QSOs").

Your settings are also protected automatically. Every time they load cleanly, a copy is kept as
`settings.json.bak`. If `settings.json` is ever damaged, for example by a power cut, the program starts with that
last good copy and tells you so in the red banner.

## 2.8 Overlay profiles

The **OVERLAY PROFILES** card lists your overlay profiles in the order they appear in the Transmit tab's
**OVERLAY** list. Use **▲** and **▼** to reorder them. The profiles themselves are edited on the Transmit tab; see
[6. Overlay profiles](06-overlays.md).

---

[← 1. What you need](01-what-you-need.md) · [Contents](README.md) · [3. The main window →](03-main-window.md)
