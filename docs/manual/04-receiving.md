# 4. Receiving pictures

## 4.1 Start listening

On the **Receive** tab, click **● Start listening**. The button turns into **■ Stop listening**. The **RECEIVER**
card on the right shows:

| Item | Meaning |
|---|---|
| Status line | `Listening — waiting for a VIS header…`, `Receiving Martin M1 · line 120/256`, `Received Robot 36 at 09:53Z — listening for the next picture…` |
| Progress bar | How much of the picture has arrived. |
| **Input** | Audio level in dB. Aim for **−30 to −10 dB**: green is good, yellow is too quiet, red is clipping. |
| **Waterfall** | The audio from 1000 to 2500 Hz, with the current dominant tone in Hz above it. See [4.3](#43-tuning-with-the-waterfall). |
| Tuning line and **Fix tuning** | How far the last picture was off tune, and a one-click fix. |
| ⚠ warning (only if needed) | `… s of input audio lost` or `Decoder … s behind`; see [Troubleshooting](10-troubleshooting.md). |
| `● REC …` (only while recording) | Running time and size of a recording; see [4.7](#47-recording-everything). |

> 📷 **Screenshot [S6]** `images/s06-receiver-card.png`: a crop of the **RECEIVER** card while a picture is being
> received: the status line with the line count, a green Input level, and the waterfall with the signal between the
> grey markers and sync on the pink one.

## 4.2 What happens automatically

- **Mode detection:** the mode is detected from the VIS header that starts every SSTV transmission. All
  [15 modes](11-modes.md) are supported.
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

**Zoom** (below the buttons under the picture): **Fit** scales pictures to the panel. The slider sets a fixed zoom
from 25 % to 400 %, with scrollbars when the picture is larger than the panel. **1:1** jumps to 100 %, one picture
pixel per screen pixel. The Transmit tab has its own zoom, and both are remembered.

> 📷 **Screenshot [S7]** `images/s07-zoom.png`: a received picture at 200 % with scrollbars, and the zoom bar
> (Fit unticked, slider, "200 %", 1:1) visible below the buttons.

## 4.3 Tuning with the waterfall

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
  under the button, e.g. `Retuned 14.230.000 → 14.230.040 (+40 Hz)`. The frequency shown is the one the rig
  reports back, so you see the real result.
  - It's safe to press twice. The correction is worked out from the dial the picture was received on, so a second
    press (or pressing it on an older picture after retuning) says **Already corrected** instead of moving again.
  - It's greyed out while a picture is coming in. Fix tuning once the picture ends, so the picture isn't spoiled
    halfway.
  - It refuses with a short reason if it can't be done safely: the rig isn't connected, the rig has been moved to
    another frequency since that picture, or the picture came from a WAV file.
  - If the rig ignores the change (for example the VFO is locked), it says *The rig didn't move…*.

> 📷 **Screenshot [S8]** `images/s08-fix-tuning.png`: a crop of the tuning line in yellow ("Signal 40 Hz too
> high") with the **Fix tuning** button. Optionally a second crop after pressing it, showing "Retuned … (+40 Hz)".

## 4.4 Tools: manual start, re-decode, filter

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

> 📷 **Screenshot [S9]** `images/s09-tools.png`: the **Tools** panel expanded (Start now, Re-decode settings, Noise
> filter).
>
> 📷 **Screenshot [S10]** `images/s10-noise-filter.png` (optional): the same weak, noisy received picture three
> times side by side, with the noise filter Off, Low and High.

## 4.5 Decoding a recording

With listening stopped, click **Decode WAV…** and pick a WAV file. Every SSTV picture in the file is decoded into
the gallery, using the **Tools** re-decode settings. PCM (8/16/24/32-bit) and 32-bit float WAVs work, mono or
stereo (the first channel is used). Pictures from a file are timestamped with the time you decode them, not when they
were recorded.

## 4.6 The gallery

Received pictures are listed under **RECEIVED**, newest first, with the time, mode and tags: *partial* (cut short),
*audio* (audio available), *re-decoded*. Click one to view it; **Live view** goes back to the picture being received.

| Button | Action |
|---|---|
| **Save as…** | Save the selected picture as PNG. |
| **Save audio…** | Save the selected picture's audio as WAV. It runs from about 3 s before its header to its end, so it decodes again with Decode WAV…. Audio is kept in memory for the 10 newest pictures; older ones can be saved only if their WAV was auto-saved. |
| **Use as TX picture** | Load the picture on the Transmit tab and switch there, so you can reply with it and your overlay. |
| **Open folder** | Show the picture's file in Explorer, or open the save folder. |

> 📷 **Screenshot [S11]** `images/s11-gallery.png`: a crop of the **RECEIVED** gallery with several pictures,
> including one tagged *partial* and one *re-decoded*.

## 4.7 Recording everything

While listening, **● Record** writes everything heard on the input to `rx_<date>_<time>Z.wav` in the save folder.
The button turns into **■ Stop recording** and the receiver card shows the running time and file size. Recording
stops when you click Stop recording or Stop listening. Your own transmissions aren't recorded.

## 4.8 Saved file names

```
20261019_093958Z_Martin-M2.png           picture
20261019_093958Z_Martin-M2.wav           its audio (if "…with their audio" is on)
20261019_094512Z_Scottie-S1_partial.png  a picture that was cut short
rx_20261019_100000Z.wav                  a ● Record session
```

---

[← 3. The main window](03-main-window.md) · [Contents](README.md) · [5. Transmitting a picture →](05-transmitting.md)
