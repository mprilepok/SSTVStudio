# SSTV Studio — User Manual

SSTV Studio sends and receives slow-scan television (SSTV) pictures from a Windows PC. It controls your transceiver
through **Hamlib rigctld** and logs QSOs to **Wavelog**.

This manual describes version 1.0.0. The version you have is shown under **ⓘ** (About).

---

## Contents

1. [What you need](#1-what-you-need)
2. [First-time setup](#2-first-time-setup)
3. [The main window](#3-the-main-window)
4. [Receiving pictures](#4-receiving-pictures)
5. [Transmitting a picture](#5-transmitting-a-picture)
6. [Overlay profiles (CQ, Reply, 73 …)](#6-overlay-profiles)
7. [The QSO form and logging](#7-the-qso-form-and-logging)
8. [A typical SSTV QSO, step by step](#8-a-typical-sstv-qso-step-by-step)
9. [Getting a clean signal: levels, ALC and slant](#9-getting-a-clean-signal)
10. [Troubleshooting](#10-troubleshooting)
11. [Reference](#11-reference)

---

## 1. What you need

| Item | Notes |
|---|---|
| Windows 10 or 11 PC | With the .NET 10 runtime |
| Transceiver with an audio interface | Built-in USB audio codec (IC-7300, IC-705, FT-991A, TS-590 …) or an external interface such as a SignaLink |
| Hamlib | Provides `rigctld.exe` for frequency, mode, PTT, meters and **Fix tuning**. Optional if you use VOX or serial PTT. |
| Wavelog account | Optional, for logging. You need its URL and a **read/write** API key. |

Start the program from the project folder:

```powershell
dotnet run --project src/SstvStudio
```

A splash screen appears for a moment while settings, fonts, audio and the rig connection are prepared. The program
then opens on the **Receive** tab.

---

## 2. First-time setup

Open the **Settings** tab and go through the cards from top to bottom. Click **Save settings** when you're done.
Settings are also saved automatically when you transmit and when you close the program.

### 2.1 Station

| Field | What to enter |
|---|---|
| **My callsign** | Your callsign. Used by `{MYCALL}`, the CW ID and the log. |
| **My locator** | Your Maidenhead locator, e.g. `JN88PC`. Used by `{MYGRID}` and the log. |
| **TX power (W)** | Power written to the log. It's also the full-scale value of the power meter. |
| **CW ID speed (WPM)** | Speed of the optional Morse ID after each picture. |

### 2.2 Rig control (Hamlib rigctld)

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

If the connection drops, the program retries every 5 seconds by itself.

### 2.3 PTT

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

### 2.4 Audio output (transmit)

| Setting | What to choose |
|---|---|
| **Device (rig USB codec)** | The rig's sound card, often called "USB Audio CODEC". Click **Refresh** after plugging it in. |
| **Sample rate** | 48000 (recommended) or 44100. Used for both transmit and receive. |
| **Clock correction (ppm)** | Leave at 0 unless other stations report slanted pictures; see [9.3](#93-slant-and-clock-correction). |

### 2.5 Receive

| Setting | Meaning |
|---|---|
| **Input device (rig USB codec)** | The sound card the rig's receive audio comes in on. |
| **Start listening on launch** | Begin receiving as soon as the program starts. |
| **Auto-save received pictures** | Save every received picture as PNG in the save folder. |
| **…with their audio (WAV)** | Also save each picture's audio next to its PNG. |
| **Save folder** | Where pictures, audio and recordings go. Default: `Pictures\SstvStudio`. |

After changing the input device, stop and start listening again.

### 2.6 Wavelog

1. In Wavelog, create an API key with **read/write** rights. It's under your account menu → API keys.
2. Enter the **Wavelog URL**, e.g. `https://log.example.com`. Don't add `/index.php`; the program adds it.
3. Paste the **API key (read/write)**.
4. Click **Load**. Your station profiles appear in the list; pick the one you're operating from. If your callsign
   and locator are still empty, they're filled in from the chosen profile.
5. Optional: tick **Push rig frequency/mode to Wavelog (live CAT)**. Wavelog's QSO form then follows your rig, the
   same way it does with WaveLogGate.

### 2.7 Backup: settings and log

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

---

## 3. The main window

**Header (always visible):**
- The logo and program name.
- The rig's frequency and mode, and the rig status badge: **Connected** in green, **TX** in red while transmitting,
  or an error message.
- The **Connect rig / Disconnect** button.
- **ⓘ**, which opens the About window.

**Red banner:** appears below the header when something goes wrong. Read it, then click **Dismiss**.

**Tabs:**

| Tab | Purpose |
|---|---|
| **Receive** | Live decoding, waterfall and tuning, received-picture gallery, recording, tools, and the QSO form. The program opens on this tab. |
| **Transmit** | Picture, overlay profiles, mode, the transmit button, TX meters, and the QSO form with this session's log. |
| **Settings** | Everything from [section 2](#2-first-time-setup). |

**About (ⓘ):** shows the version and build date, the .NET, Avalonia and Windows versions, and the data folder, and
lists the libraries the program is built with. Use **Copy system info** when reporting a problem, and
**Open data folder** to reach `settings.json` and `log.adi`.

---

## 4. Receiving pictures

### 4.1 Start listening

On the **Receive** tab, click **● Start listening**. The button turns into **■ Stop listening**. The **RECEIVER**
card on the right shows:

| Item | Meaning |
|---|---|
| Status line | `Listening — waiting for a VIS header…`, `Receiving Martin M1 · line 120/256`, `Received Robot 36 at 09:53Z — listening for the next picture…` |
| Progress bar | How much of the picture has arrived. |
| **Input** | Audio level in dB. Aim for **−30 to −10 dB**: green is good, yellow is too quiet, red is clipping. |
| **Waterfall** | The audio from 1000 to 2500 Hz, with the current dominant tone in Hz above it. See [4.3](#43-tuning-with-the-waterfall). |
| Tuning line and **Fix tuning** | How far the last picture was off tune, and a one-click fix. |
| ⚠ warning (only if needed) | `… s of input audio lost` or `Decoder … s behind`; see [Troubleshooting](#10-troubleshooting). |
| `● REC …` (only while recording) | Running time and size of a recording; see [4.7](#47-recording-everything). |

### 4.2 What happens automatically

- **Mode detection:** the mode is detected from the VIS header that starts every SSTV transmission. All
  [15 modes](#111-modes) are supported.
- **Live drawing:** the picture builds line by line on screen.
- **Slant and clock correction:** sync timing is measured throughout. Pictures from stations whose sound card is off
  by up to ±3 % come out straight, with correct colours. When the picture is complete it's redrawn once more with
  the final correction.
- **Mistuned signals:** pictures up to ±250 Hz off tune are received and corrected.
- **Gallery and saving:** the finished picture goes into the gallery and, if enabled, is saved as PNG (plus WAV).
- **Ready for the next picture:** the receiver immediately waits for the next header.
- **Pictures cut short:** if the signal disappears for about 6 s, or a new header arrives in the middle of a picture,
  the partial picture is kept (marked *partial*) and the receiver goes on to the next transmission. Weak or fading
  pictures are **not** cut short as long as the signal is still there.
- **Your own TX:** reception and recording pause while you transmit, so you never decode yourself.

**Finish picture now** ends the current picture by hand and keeps what has arrived.

**Zoom** (next to the buttons): **Fit** scales pictures to the panel. The slider sets a fixed zoom from 25 % to
400 %, with scrollbars when the picture is larger than the panel. **1:1** jumps to 100 %, one picture pixel per
screen pixel. The Transmit tab has its own zoom, and both are remembered.

### 4.3 Tuning with the waterfall

The **Waterfall** shows the newest audio at the top, with dotted markers:

| Marker | Tone |
|---|---|
| pink | 1200 Hz sync |
| grey | 1500 Hz black and 2300 Hz white |
| blue | 1900 Hz VIS leader |

A correctly tuned SSTV signal fills the space between the grey markers, with sync pulses on the pink line.

When a picture starts, the receiver measures how far off it is, e.g. **"Signal 40 Hz too high"**:
- **Green:** under 10 Hz. **Yellow:** under 60 Hz. **Red:** 60 Hz or more.
- Selecting a picture in the gallery shows that picture's measurement.
- **Fix tuning** moves the rig's dial through Hamlib so the station's next picture is centred. It uses the correct
  direction on USB and LSB and their data modes, and refuses in other modes (FM, AM, CW …). The result is shown
  under the button, e.g. `Retuned 14.230.000 → 14.230.040 (+40 Hz)`.

### 4.4 Tools: manual start, re-decode, filter

Expand **Tools** in the right column.

- **Start now** (while listening): if you tuned in after a picture began, or its header was lost in noise or QRM,
  choose the mode and click Start now. Decoding begins at the next sync pulse, and gives up after about 6 s if none
  is found. If a header is heard later, it takes over. A weak picture whose header is missed can often still be
  received this way.
- **Re-decode selected:** decodes the selected picture's audio again with:
  - **Mode:** *Auto (VIS header)* uses the header; choose a mode if it was misread.
  - **Shift (ms):** moves the picture sideways; positive moves it right.
  - **Slant (ppm):** a trim on top of the automatic slant correction.

  The result is added to the gallery, marked *re-decoded*. **Reset** clears the settings. The same settings also
  apply to **Decode WAV…**: with a forced mode, a recording that has no header is decoded from its start as that
  mode.
- **Noise filter:** **Off**, **Low** (removes speckles with a median filter) or **High** (also smooths the remaining
  grain, at the cost of some sharpness). It applies to every picture in the gallery and the live picture, and to
  files saved from then on. The unfiltered picture is kept, so you can switch back at any time.

### 4.5 Decoding a recording

With listening stopped, click **Decode WAV…** and pick a WAV file. Every SSTV picture in the file is decoded into
the gallery, using the **Tools** re-decode settings. PCM (8/16/24/32-bit) and 32-bit float WAVs work, mono or
stereo (the first channel is used). Pictures from a file are timestamped with the time you decode them, not when they
were recorded.

### 4.6 The gallery

Received pictures are listed under **RECEIVED**, newest first, with the time, mode and tags: *partial* (cut short),
*audio* (audio available), *re-decoded*. Click one to view it; **Live view** goes back to the picture being received.

| Button | Action |
|---|---|
| **Save as…** | Save the selected picture as PNG. |
| **Save audio…** | Save the selected picture's audio as WAV. It runs from about 3 s before its header to its end, so it decodes again with Decode WAV…. Audio is kept in memory for the 10 newest pictures; older ones can be saved only if their WAV was auto-saved. |
| **Use as TX picture** | Load the picture on the Transmit tab and switch there, so you can reply with it and your overlay. |
| **Open folder** | Show the picture's file in Explorer, or open the save folder. |

### 4.7 Recording everything

While listening, **● Record** writes everything heard on the input to `rx_<date>_<time>Z.wav` in the save folder.
The button turns into **■ Stop recording** and the receiver card shows the running time and file size. Recording
stops when you click Stop recording or Stop listening. Your own transmissions aren't recorded.

### 4.8 Saved file names

```
20261019_093958Z_Martin-M2.png           picture
20261019_093958Z_Martin-M2.wav           its audio (if "…with their audio" is on)
20261019_094512Z_Scottie-S1_partial.png  a picture that was cut short
rx_20261019_100000Z.wav                  a ● Record session
```

---

## 5. Transmitting a picture

Everything here is on the **Transmit** tab.

### 5.1 Choose the picture

| Button | Action |
|---|---|
| **Open picture…** | Load a PNG, JPG, BMP, GIF or WebP file. The last picture is remembered for next time. |
| **Test pattern** | Colour bars and a grey ramp; handy for tests. |
| **Use as TX picture** (Receive tab) | Reply with a picture you received. See [4.6](#46-the-gallery). |

**Scaling** (above the preview) decides how the picture is fitted to the mode's resolution:

| Scaling | Result |
|---|---|
| **Fill** | Fills the whole frame and crops the edges if the shape differs. Usually the best choice. |
| **Fit** | Shows the whole picture, with black bars if the shape differs. |
| **Stretch** | Fills the frame by distorting the picture. |

The preview always shows exactly what will be sent, at the mode's real resolution and with the overlay text drawn on.
By default it's scaled to fit the panel (**Fit**). Use the zoom slider (next to Scaling) for 25–400 %, or **1:1**
for actual size, one picture pixel per screen pixel. Large modes such as PD 290 (800×616) then get scrollbars.
The Receive tab has its own zoom. If Windows display scaling is above 100 %, every zoom is enlarged by that
factor.

### 5.2 Choose the mode

Pick a mode in the **MODE** card. The line underneath shows the resolution, the VIS code and the total time on air.
**Martin M1** and **Scottie S1** are the most common on HF. **Robot 36** is quick and is used by the ISS. The PD modes
give higher resolution. See the [mode table](#111-modes).

| Option | Effect |
|---|---|
| **Calibration preamble** | Sends 0.8 s of tones before the header, as MMSSTV does. Leave it on. |
| **CW ID after picture** | Sends `DE <your call>` in Morse after the picture, at the CW ID speed from Settings. |
| **TX audio level** | Output level from 5 to 100 %. Set it so the rig shows little or no ALC; see [section 9](#9-getting-a-clean-signal). It can be changed during a transmission. |

### 5.3 Transmit

1. Check the rig is on the right frequency and in the right mode: USB, or the data mode (USB-D / PKTUSB) if your
   rig takes USB audio that way.
2. Click **▶ TRANSMIT**.
   - The rig is keyed, the audio plays and the progress bar shows elapsed and total time.
   - The receiver is muted while you transmit.
   - If no callsign has been entered yet, the QSO start time is set now.
3. To abort, click **■ STOP & UNKEY**. Audio stops and the rig is un-keyed straight away.

**Export WAV…** saves the same transmission as an audio file without keying the rig. Use it to test with another
decoder, or to play the file from another program.

### 5.4 TX meters

While you transmit, the **TX METERS** card shows what the rig reports through rigctld:

| Meter | Shows | Colour guide |
|---|---|---|
| **Power** | Output in W, or % of full power if the rig doesn't report watts | — |
| **SWR** | Antenna match | green < 1.5 · yellow < 2.0 · red ≥ 2.0 |
| **ALC** | Automatic level control action | green < 10 % · yellow < 30 % · red ≥ 30 % |
| **Current** | PA drain current (Id) | — |

The card header also shows the rig's power setting. After each transmission a summary line appears, e.g.
`Last TX: 48 W avg · SWR ≤ 1.3 · ALC ≤ 4 % · Id ≤ 12.1 A`. A meter your rig doesn't support shows `n/a`.

### 5.5 Safety features

- **TX watchdog:** a transmission can never run longer than the watchdog time. A mode that wouldn't fit is refused
  before the rig is keyed.
- **SWR trip:** transmission stops with a warning when SWR is too high.
- **Guaranteed un-key:** un-keying is retried if it fails. If it still fails, you get a clear **PTT UN-KEY FAILED**
  warning.
- **Safe exit:** closing the window during a transmission first stops it and releases PTT.

---

## 6. Overlay profiles

The overlay is the text drawn on top of your picture. That's how callsigns and reports are exchanged in SSTV.
A **profile** is a named set of text lines, so you can switch the whole overlay with one click.

### 6.1 Switching profiles

The chips above the preview (**CQ**, **Reply**, **73**, plus any you add) select the active profile. The preview
updates immediately.

| Built-in profile | Lines |
|---|---|
| **CQ** | `CQ CQ SSTV` (large, yellow, top) · `de {MYCALL}` · `{MYGRID}` (bottom) |
| **Reply** | `{CALL} de {MYCALL}` (top) · `UR {RST}` · `{NAME}` (bottom) |
| **73** | `{CALL} de {MYCALL}` (top) · `TNX QSO 73!` (large, centre) · `{DATE} {TIME}` (bottom) |

### 6.2 Editing a profile

Expand **Edit overlay profile** below the preview. It edits the profile that's currently selected.

| Control | Purpose |
|---|---|
| Name box | Rename the profile. |
| **New** / **Duplicate** / **Delete** | Create an empty profile, copy the current one, or remove it. The last remaining profile can't be deleted. |
| **Restore defaults** | Adds back any missing CQ / Reply / 73 profile. It never overwrites your edits. |
| **+ Add line** | Add a text line. |

Each line has these settings:

| Column | Meaning |
|---|---|
| **Text** | The text, with optional placeholders (below). |
| **Font** | Any font installed on your PC. The list shows each font in its own typeface. |
| **Size %** | Text height as a percentage of the picture height. 10 % is a good default. Type a number, or use ↑/↓ or the mouse wheel. |
| **Color** | Click the swatch to open a colour picker. |
| **B** | Bold. |
| **Out** (O) | Black outline. Keeps text readable on any picture; recommended. |
| **Position** | **Top**, **Middle** or **Bottom**. |
| **Align** | **Left**, **Center** or **Right**. |
| **Order** (▲ / ▼) | Move the line up or down. Order matters: lines at the same position stack in list order, top to bottom. |
| **✕** | Remove the line. |

A line that's too wide for the picture shrinks automatically to fit. Every change shows in the preview at once and
is saved with your settings.

### 6.3 Placeholders

| Placeholder | Replaced with |
|---|---|
| `{CALL}` | The other station's callsign (QSO form) |
| `{MYCALL}` | Your callsign (Settings) |
| `{RST}` | Report sent (QSO form) |
| `{NAME}` | The other operator's name (QSO form) |
| `{GRID}` | The other station's locator (QSO form) |
| `{MYGRID}` | Your locator (Settings) |
| `{MODE}` | The SSTV mode, e.g. `Martin M1` |
| `{DATE}` | UTC date, `2026-10-19` |
| `{TIME}` | UTC time, `09:05Z` |
| `{FREQ}` | Frequency in MHz (from the rig, or the QSO form) |

If **all** the placeholders on a line are still empty, the line is hidden. For example, `{CALL} de {MYCALL}` doesn't
show a lonely "de" before you've typed any callsigns. Date and time are refreshed when you click TRANSMIT.

---

## 7. The QSO form and logging

The **QSO** form appears on both tabs: on **Receive** under the picture, and on **Transmit** in the right column.
It's the **same** QSO. A callsign typed while their picture comes in is already on the Transmit tab and in your
reply overlay (`{CALL}`, `{RST}`, `{NAME}`, `{GRID}`).

| Field | Notes |
|---|---|
| **Callsign** | The other station. Also fills `{CALL}` in the overlay. |
| **Name**, **Locator** | Optional. Also fill `{NAME}` and `{GRID}`. |
| **RSV sent** | Your report of their picture (Readability, Strength, Video), e.g. `595`. Fills `{RST}`. |
| **RSV rcvd** | The report they gave you. |
| **Freq (MHz)** | Filled from the rig automatically; type it yourself if the rig isn't connected. |
| **Comment** | Optional. The SSTV mode is added to the comment automatically. |

The text at the top right of the form shows when the QSO started: the moment you first entered their callsign, or
your first transmission if that came earlier.

Click **Log QSO** (on either tab) to save it. Two things happen:

1. The QSO is written to the local ADIF backup `log.adi` in the data folder. This always happens first, so nothing
   is lost if the internet is down.
2. If Wavelog is set up, the QSO is uploaded to your selected station profile.

The result appears under the buttons, and on the Transmit tab the QSO is added to the **THIS SESSION** list
(✓ = uploaded). When logging succeeds, the form is cleared for the next contact. **Clear** empties it by hand.

The ADIF record uses `MODE = SSTV`, the band worked out from the frequency, both reports, your station callsign and
locator, and your TX power. The SSTV mode logged is the one selected on the Transmit tab.

---

## 8. A typical SSTV QSO, step by step

1. **Set up:** tune to an SSTV frequency, e.g. 14.230 MHz USB, and click **Start listening** on the Receive tab.
   Check that **Input** is green.
2. **Call CQ:** on the Transmit tab, select the **CQ** overlay chip, choose a picture and mode (e.g. Martin M1), and
   click **▶ TRANSMIT**.
3. **Someone answers:** their picture appears on the Receive tab.
   - If the tuning line is yellow or red, click **Fix tuning**.
   - Type their callsign, name and your report (**RSV sent**) into the QSO form under the picture.
4. **Reply:**
   1. Optionally select their picture in the gallery and click **Use as TX picture**.
   2. On the Transmit tab, select the **Reply** chip. The preview now shows e.g. `OK1ABC de OM1PU` and `UR 595`.
   3. Transmit.
5. **Their report:** when their next picture arrives, enter the report they gave you in **RSV rcvd**.
6. **Finish:** select the **73** chip and send your final picture.
7. **Log:** click **Log QSO** on either tab. The QSO goes to the local ADIF file and to Wavelog.

---

## 9. Getting a clean signal

### 9.1 Transmit level and ALC

SSTV needs a clean, undistorted signal. The rig's ALC compresses the audio and smears the picture.

1. Set the rig's power as you like, e.g. 50 W.
2. Send a test picture and watch the **ALC** meter.
3. Lower **TX audio level** (or the rig's USB MOD level) until ALC stays green, ideally near 0 %, while power still
   reaches the set value.

SSTV sends a continuous carrier for the whole picture. Run the rig below its full-power rating for long modes,
especially PD 180 and longer.

### 9.2 Receive level and tuning

- Adjust the rig's USB audio output level, or Windows' recording level for the codec, until **Input** reads about
  −30 to −10 dB on a received signal.
- Keep noise reduction and narrow filters off. SSTV needs roughly 1100–2300 Hz, so a 2.4–2.8 kHz SSB filter works
  well.
- Use the waterfall and **Fix tuning** ([4.3](#43-tuning-with-the-waterfall)) to keep stations centred. The receiver
  corrects up to ±250 Hz, but staying close helps on weak signals.

### 9.3 Slant and clock correction

If other stations see **your** pictures slanted, your sound card's clock is slightly off. Ask for the direction and
adjust **Clock correction (ppm)** in Settings:

- Start with ±100 ppm steps, then refine. A positive value means the sound card runs fast.
- Typical corrections are under ±300 ppm.

Pictures you **receive** are corrected automatically (up to ±3 %), so this setting doesn't affect reception. If a
received picture is still slightly slanted, use **Tools → Re-decode** with a **Slant (ppm)** trim.

---

## 10. Troubleshooting

| Problem | What to check |
|---|---|
| **"PTT is set to rigctld but the rig is not connected"** | Start rigctld and click **Connect rig**, or switch PTT to SerialRts/Dtr or Vox. |
| **Rig error: No connection could be made…** | rigctld isn't running, or Host/Port are wrong. Check that `rigctld -m … -r COM…` is running. |
| **Rig keys but nothing is transmitted** | Wrong output device. The rig may need a data mode (USB-D / PKTUSB) to take USB audio, or its USB MOD level is 0. |
| **No ALC / power meter values (`n/a`)** | Your rig or its Hamlib backend doesn't report that meter. Everything else still works. |
| **Transmission refused: "longer than the TX watchdog"** | Raise **TX watchdog (s)** in Settings (PD 290 needs about 300 s). |
| **Transmission stopped: SWR reached the trip level** | Check the antenna and tuner. Change **SWR trip** only if your antenna is known to be fine at that SWR. |
| **Nothing is received** | Check the **Input** level and the waterfall. The right input device must be selected. Restart listening after changing the device. |
| **"No picture followed (false trigger, or nothing found for the manual start)"** | A header-like tone sequence was heard but no picture followed (QRM), or **Start now** found no sync pulses. Harmless; the receiver keeps listening. |
| **⚠ … s of input audio lost** | Windows dropped audio before it reached the program: the PC is overloaded, or the USB codec disconnected. Pictures received during a dropout will be damaged. |
| **⚠ Decoder … s behind** | The PC can't keep up with decoding. Close other heavy programs. |
| **Waterfall shows the signal off the markers** | Mistuned; click **Fix tuning** (USB/LSB) or retune by hand. |
| **"Can't retune in FM mode"** | Fix tuning works only on USB/LSB and their data modes. |
| **A picture was missed (no header)** | Use **Tools → Start now**, or record it and use **Decode WAV…** with a forced mode. |
| **Received picture is slanted or shifted** | Select it and use **Tools → Re-decode** with **Slant (ppm)** or **Shift (ms)**. |
| **Received pictures are very noisy** | The signal is weak. Turn off noise reduction, widen the filter, check the input level isn't clipping (red), and try **Tools → Noise filter**. |
| **Decode WAV… is greyed out** | Stop listening first. |
| **Wavelog: "missing api key" / 401** | Use a **read/write** API key and check the URL. |
| **Wavelog unreachable** | The QSO is still in `log.adi`; import that file into Wavelog later. |

---

## 11. Reference

### 11.1 Modes

| Mode | Resolution | Time on air | VIS | Colour |
|---|---|---|---|---|
| Martin M1 | 320 × 256 | 114 s | 44 | RGB |
| Martin M2 | 320 × 256 | 58 s | 40 | RGB |
| Scottie S1 | 320 × 256 | 110 s | 60 | RGB |
| Scottie S2 | 320 × 256 | 71 s | 56 | RGB |
| Scottie DX | 320 × 256 | 269 s | 76 | RGB |
| Robot 36 | 320 × 240 | 36 s | 8 | YCrCb |
| Robot 72 | 320 × 240 | 72 s | 12 | YCrCb |
| PD 50 | 320 × 256 | 50 s | 93 | YCrCb |
| PD 90 | 320 × 256 | 90 s | 99 | YCrCb |
| PD 120 | 640 × 496 | 126 s | 95 | YCrCb |
| PD 160 | 512 × 400 | 161 s | 98 | YCrCb |
| PD 180 | 640 × 496 | 187 s | 96 | YCrCb |
| PD 240 | 640 × 496 | 248 s | 97 | YCrCb |
| PD 290 | 800 × 616 | 289 s | 94 | YCrCb |
| Wraase SC2-180 | 320 × 256 | 182 s | 55 | RGB |

Times are for the picture only. The calibration preamble adds 0.8 s, the VIS header 0.9 s, and the CW ID a few
seconds more. All modes can be both sent and received.

### 11.2 Common SSTV frequencies

These are popular calling frequencies; check your national band plan.

| Band | Frequency | Sideband |
|---|---|---|
| 80 m | 3.730–3.735 MHz | LSB (by custom) |
| 40 m | 7.165–7.171 MHz | LSB (by custom) |
| 20 m | **14.230 MHz** (also 14.233) | USB |
| 15 m | 21.340 MHz | USB |
| 10 m | 28.680 MHz | USB |
| 2 m | 145.500 MHz (also the ISS on 145.800) | FM |

### 11.3 Files and folders

| File | Location |
|---|---|
| Settings (including overlay profiles) | `settings.json` in the data folder (default `%APPDATA%\SstvStudio`) |
| Local QSO log (ADIF) | `log.adi` in the data folder |
| Earlier logs (after **Start new log**) | `log_<date>_<time>Z.adi` in the data folder |
| Settings before the last import | `settings-before-import.json` in the data folder |
| Received pictures, audio, recordings | The **Save folder** (default `Pictures\SstvStudio`) |

**ⓘ → Open data folder** or **Settings → Open data folder** opens the data folder.

### 11.4 How SSTV sounds

| Tone | Meaning |
|---|---|
| 1900 Hz, steady | Leader at the start of the VIS header |
| 1100 / 1300 Hz | VIS data bits (they identify the mode) |
| 1200 Hz pulses | Line sync |
| 1500 → 2300 Hz | Picture brightness from black to white |

### 11.5 Advanced: environment variables

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
