# 5. Transmitting a picture

Everything here is on the **Transmit** tab.

> 📷 **Screenshot [S12]** `images/s12-transmit-tab.png`: the whole **Transmit** tab with a real photo loaded, the
> **Reply** overlay selected in the OVERLAY list (so the preview shows e.g. "OK1ABC de OM1PU" and "UR 595"), and Martin M1 chosen.

## 5.1 Choose the picture

| Button | Action |
|---|---|
| **Open picture…** | Load a PNG, JPG, BMP, GIF or WebP file. The last picture is remembered for next time. |
| **Test pattern** | Colour bars and a grey ramp; handy for tests. |
| **Use as TX picture** (Receive tab) | Reply with a picture you received. See [4.6](04-receiving.md#46-the-gallery). |

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

## 5.2 Choose the mode

Pick a mode in the **MODE** card. The line underneath shows the resolution, the VIS code and the total time on air.
**Martin M1** (Europe) and **Scottie S1** (North America) are the most common on HF. When you answer a station, use
the mode they sent. **Robot 36** and **Martin M2** are quick. The PD modes give higher resolution, and **PD 120** is
the usual mode of ISS SSTV events. [11.1 Modes](11-modes.md) describes every mode and helps you choose.

| Option | Effect |
|---|---|
| **Calibration preamble** | Sends 0.8 s of tones before the header, as MMSSTV does. Leave it on. |
| **CW ID after picture** | Sends `DE <your call>` in Morse after the picture, at the CW ID speed from Settings. |
| **TX audio level** | Output level from 5 to 100 %. Set it so the rig shows little or no ALC; see [section 9](09-clean-signal.md). Drag the slider, or point at it and turn the mouse wheel (1 % per notch). It can be changed during a transmission. |

> 📷 **Screenshot [S13]** `images/s13-mode-card.png`: a crop of the **MODE** card with the mode list open, showing
> the modes with their resolution and time.

## 5.3 Transmit

1. Check the rig is on the right frequency and in the right mode: USB, or the data mode (USB-D / PKTUSB) if your
   rig takes USB audio that way.
2. Click **▶ TRANSMIT**.
   - The rig is keyed, the audio plays and the progress bar shows elapsed and total time.
   - The receiver is muted while you transmit.
   - If no callsign has been entered yet, the QSO start time is set now.
3. To abort, click **■ STOP & UNKEY**. Audio stops and the rig is un-keyed straight away.

**Export WAV…** saves the same transmission as an audio file without keying the rig. Use it to test with another
decoder, or to play the file from another program.

## 5.4 TX meters

While you transmit, the **TX METERS** card shows what the rig reports through rigctld:

| Meter | Shows | Colour guide |
|---|---|---|
| **Power** | Output in W, or % of full power if the rig doesn't report watts | — |
| **SWR** | Antenna match | green < 1.5 · yellow < 2.0 · red ≥ 2.0 |
| **ALC** | Automatic level control action | green < 10 % · yellow < 30 % · red ≥ 30 % |
| **Current** | PA drain current (Id) | — |

The card header also shows the rig's power setting. After each transmission a summary line appears, e.g.
`Last TX: 48 W avg · SWR ≤ 1.3 · ALC ≤ 4 % · Id ≤ 12.1 A`. A meter your rig doesn't support shows `n/a`.

Some rigs show a small ALC value even with no drive at all. The Yaesu FT-991, for example, reads about 8 % at rest.
Judge ALC by whether it **rises** as you turn the TX level up, not by its exact value.

> 📷 **Screenshot [S14]** `images/s14-tx-meters.png`: a crop of the **TX METERS** card during a transmission
> (power, SWR, ALC and current all showing), plus the progress bar of the **TRANSMIT** card above it.

## 5.5 Safety features

- **TX watchdog:** a transmission can never run longer than the watchdog time. A mode that wouldn't fit is refused
  before the rig is keyed.
- **SWR trip:** transmission stops with a warning when SWR is too high.
- **Guaranteed un-key:** un-keying is retried if it fails. If it still fails, you get a clear **PTT UN-KEY FAILED**
  warning.
- **Safe exit:** closing the window during a transmission first stops it and releases PTT.

---

[← 4. Receiving pictures](04-receiving.md) · [Contents](README.md) · [6. Overlay profiles →](06-overlays.md)
