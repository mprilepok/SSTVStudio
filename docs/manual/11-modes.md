# 11. SSTV modes

SSTV Studio sends and receives 15 modes. This chapter explains how they differ, what each one is good and bad at,
and which to pick. You never have to set the *receiving* mode: it's recognised from the VIS header at the start of
every transmission.

## 11.1 How SSTV sends a picture

An SSTV picture is sent as a sequence of audio tones, one line at a time:

| Tone | Meaning |
|---|---|
| 1900 Hz leader, then 1100/1300 Hz bits | The **VIS header**: a short code naming the mode, sent before the picture |
| 1200 Hz pulse | **Sync**: marks the start of a line (or of a group of lines) so the receiver stays in step |
| 1500 → 2300 Hz | The picture itself: 1500 Hz is black, 2300 Hz is white, and the frequency in between is the brightness |

Because brightness is a *frequency*, the signal survives level changes well. But noise, fading and interference
shift the measured frequency, and that shows up as speckles, streaks and wrong colours. How a mode spends its time
decides how well it copes.

## 11.2 What makes the modes different

**1. Resolution.** Most modes send 320 × 256 (Robot: 320 × 240). The PD modes go up to 640 × 496 and 800 × 616.
More pixels in the same time means each pixel gets less time (see 2).

**2. Time per pixel: the key to noise resistance.** The receiver measures each pixel's tone over the time that pixel
lasts. A longer pixel lets it average away more noise, so slow modes give clean pictures on weak signals and fast modes
need a strong one. In the tables below, "pixel" is that time in milliseconds.

**3. How colour is sent.**
- **RGB** (Martin, Scottie, Wraase) sends each line three times: green, blue and red.
  - *Benefits:* accurate colours, and colour detail as sharp as brightness detail (red text on a blue background
    stays crisp).
  - *Drawbacks:* every pixel is effectively sent three times, so RGB modes are slow for their resolution. A short
    fade that hits only one colour's scan leaves a coloured streak (a dip during the red scan gives a cyan stripe).
- **YCrCb** (Robot, PD) sends brightness (Y) and two colour-difference signals (R−Y and B−Y). Colour is sent with
  less detail, because the eye notices brightness detail far more than colour detail.
  - *Benefits:* more resolution, or a shorter time, for the same perceived quality.
  - *Drawbacks:* colour edges are softer, and noise in the colour signals shows up as coloured blotches. Pictures
    from software that codes colour slightly differently can look a little less saturated.

**4. Sync.** Every mode sends a 1200 Hz sync pulse per line, except PD, which sends one per **two** lines. SSTV
Studio measures the timing from all the sync pulses together and straightens slanted pictures, so in practice every
mode stays in step. Where the sync sits in the line differs, though: Martin, Robot, PD and Wraase start each line
with it, while Scottie puts it in the middle, between the blue and red scans.

**5. Time on air.** Longer modes mean more chance that QSB, QRM or a band change spoils the picture. They also
mean a continuous carrier for minutes, which loads your PA (see [9.1](09-clean-signal.md#91-transmit-level-and-alc)).

**6. Popularity.** A mode is only useful if the other station sends or expects it. Martin M1, Scottie S1, Robot 36
and PD 120 are heard most. All 15 modes are in MMSSTV, the most widely used SSTV program.

> 📷 **Screenshot [S18]** `images/s18-mode-examples.png` (optional): the test pattern as received in four modes
> side by side (e.g. Robot 36, Martin M1, Scottie DX, PD 120), to show the difference in detail and colour.

## 11.3 The mode families

| Family | Named after | Colour | Line structure |
|---|---|---|---|
| **Martin** | Martin Emmerson, G3OQD | RGB | Sync, then green, blue, red |
| **Scottie** | Eddie Murphy, GM3SBC | RGB | Green, blue, sync, red: sync in the middle of the line |
| **Robot** | Robot Research Inc., maker of early SSTV scan converters | YCrCb | Sync, brightness, then colour (Robot 36: one colour signal per line, alternating) |
| **PD** | Paul Turner, G4IJE, and Don Rotier, K0HEO | YCrCb | Sync, then brightness of line 1, R−Y, B−Y, brightness of line 2 (two lines share their colour) |
| **Wraase** | Volker Wraase, DL2RZ, maker of the SC-1/SC-2 scan converters | RGB | Sync, then red, green, blue |

- **Martin:** the everyday family of Europe. Plain RGB with full colour detail, and well-balanced speed and
  robustness. It's what most European stations call CQ in.
- **Scottie:** the everyday family of North America, very similar in quality to Martin. If the other station is in
  the Americas, they probably use Scottie.
- **Robot:** the oldest family still in common use, built for speed. Robot 36 is the quickest colour mode there
  is, but it's also the most fragile in noise.
- **PD:** the most efficient family: brightness and colour at full horizontal detail, with colour shared by
  each pair of lines. It covers everything from quick 320-pixel pictures to the largest 800 × 616 one, and is the
  usual choice for ISS SSTV events.
- **Wraase:** a slow, very clean RGB mode from the scan-converter era. It's rarely heard now.

## 11.4 Mode by mode

Pixel times are for brightness. For YCrCb modes the colour pixel is given too, when it differs.

### Martin M1
320 × 256 · 114 s · pixel 0.46 ms · VIS 44 · RGB

The standard SSTV mode in Europe.
- **Benefits:**
  - It's what European stations expect and reply in.
  - Full colour detail.
  - Good noise resistance for its speed.
- **Drawbacks:**
  - Nearly 2 minutes for a standard-resolution picture.
  - Like all RGB modes, a short fade can leave a coloured stripe.
- **Use it for:** CQ and QSOs on HF, especially in Europe. The safe default.

### Martin M2
320 × 256 · 58 s · pixel 0.23 ms · VIS 40 · RGB

Martin M1 in half the time.
- **Benefits:**
  - Twice as fast as M1 with the same colours and picture size.
  - In SSTV Studio's simulated-noise tests (5 dB SNR) it still came out clearer than Robot 36, despite the
    shorter pixel.
- **Drawbacks:**
  - Less fine detail than M1.
  - Noticeably noisier on weak signals, since half the pixel time means half the averaging.
- **Use it for:** quick replies and busy frequencies, on a reasonably strong signal.

### Scottie S1
320 × 256 · 110 s · pixel 0.43 ms · VIS 60 · RGB

The standard SSTV mode in North America.
- **Benefits:**
  - It's what American stations expect.
  - Quality and robustness are much like Martin M1.
  - Full colour detail.
- **Drawbacks:**
  - Nearly 2 minutes.
  - RGB fade stripes, as with Martin.
- **Use it for:** QSOs with the Americas, or when the other station sends Scottie.

### Scottie S2
320 × 256 · 71 s · pixel 0.28 ms · VIS 56 · RGB

A faster Scottie.
- **Benefits:** two-thirds of the time of S1, with full colour detail.
- **Drawbacks:** less detail and more noise than S1.
- **Use it for:** quick exchanges with Scottie users on a good signal.

### Scottie DX
320 × 256 · 269 s · pixel 1.08 ms · VIS 76 · RGB

The slow, robust Scottie.
- **Benefits:**
  - The longest pixel of all modes, by far the most noise-resistant.
  - It gives a clean, detailed picture where other modes turn to snow.
- **Drawbacks:**
  - 4½ minutes on air: a long time to keep a band, and a long carrier for your PA.
  - Many stations won't wait that long for a reply.
- **Use it for:** weak or noisy signals and long DX paths, when the picture quality matters more than speed.

### Robot 36
320 × 240 · 36 s · pixel 0.28 ms (colour 0.14 ms) · VIS 8 · YCrCb

The quickest colour mode.
- **Benefits:**
  - Only 36 seconds.
  - Widely supported, including by phone apps.
  - Ideal when band time is short.
- **Drawbacks:**
  - Fragile in noise: its colour pixels are very short, so noise shows as coloured blotches. Of the three modes in
    SSTV Studio's simulated-noise tests (Robot 36, Martin M2, Scottie S2), it scored lowest.
  - Colour detail is reduced both ways: each line carries only one of the two colour signals.
  - 240 lines instead of 256.
- **Use it for:** strong signals, VHF/UHF FM and repeaters, quick "picture received" replies.

### Robot 72
320 × 240 · 72 s · pixel 0.43 ms (colour 0.22 ms) · VIS 12 · YCrCb

Robot 36 done properly.
- **Benefits:**
  - Both colour signals on every line, and slower brightness: much cleaner and sharper than Robot 36.
  - Still faster than Martin M1.
- **Drawbacks:**
  - Colour is still softer than in RGB modes.
  - 240 lines.
  - Less common than Robot 36, so fewer stations use it.
- **Use it for:** when you'd use Robot 36 but the signal isn't strong enough.

### PD 50
320 × 256 · 50 s · pixel 0.29 ms · VIS 93 · YCrCb

The fastest PD mode.
- **Benefits:**
  - Standard resolution in under a minute.
  - Colour at full horizontal detail, so better colour than Robot 36 at similar speed.
- **Drawbacks:**
  - Short pixel, so it needs a good signal.
  - Not often heard.
- **Use it for:** quick pictures on a good signal, as an alternative to Robot 36.

### PD 90
320 × 256 · 90 s · pixel 0.53 ms · VIS 99 · YCrCb

Standard resolution with a long pixel.
- **Benefits:**
  - More robust than Martin M1 and Scottie S1, in less time.
  - Colour at full horizontal detail.
  - One of the best choices for weak signals at standard resolution.
- **Drawbacks:**
  - Colour shared by pairs of lines, so it's slightly softer vertically than RGB.
  - Less common than Martin and Scottie on HF.
- **Use it for:** weak or fading HF signals when Scottie DX would take too long.

### PD 120
640 × 496 · 126 s · pixel 0.19 ms · VIS 95 · YCrCb

High resolution in about two minutes.
- **Benefits:**
  - Nearly four times the pixels of the 320-pixel modes, in about the time of Martin M1.
  - The usual mode of ISS SSTV events, so most stations can receive it.
- **Drawbacks:**
  - The shortest pixel of all modes, so it's the most demanding of a clean signal. On a noisy band the extra detail
    disappears under the noise.
- **Use it for:** detailed photos on a good band, and on VHF/UHF FM.

### PD 160
512 × 400 · 161 s · pixel 0.38 ms · VIS 98 · YCrCb

A middle step.
- **Benefits:** clearly more detail than 320-pixel modes, and twice the pixel time of PD 120.
- **Drawbacks:**
  - Over 2½ minutes.
  - An unusual resolution.
  - Rarely heard, so few stations will expect it.
- **Use it for:** a detailed picture when PD 120 would be too noisy.

### PD 180
640 × 496 · 187 s · pixel 0.29 ms · VIS 96 · YCrCb

PD 120's resolution, more robust.
- **Benefits:**
  - The same detail as PD 120 with a 50 % longer pixel, so it holds up better on weaker signals.
  - Also used for ISS events.
- **Drawbacks:**
  - Over 3 minutes on air.
  - A long carrier: reduce power.
- **Use it for:** high-resolution pictures when the band is fair rather than excellent.

### PD 240
640 × 496 · 248 s · pixel 0.38 ms · VIS 97 · YCrCb

PD 120's resolution, robust.
- **Benefits:** twice the pixel time of PD 120 at the same resolution. It's the cleanest way to send a 640 × 496
  picture over a weak path.
- **Drawbacks:**
  - Over 4 minutes.
  - A heavy PA load.
  - Rarely used, and the band can change while you send.
- **Use it for:** high-resolution DX on a stable band.

### PD 290
800 × 616 · 289 s · pixel 0.29 ms · VIS 94 · YCrCb

The highest resolution.
- **Benefits:** the most detail of any mode, about six times the pixels of Martin M1.
- **Drawbacks:**
  - Nearly 5 minutes on air: it needs a stable band, a PA that can take the duty cycle, and a **TX watchdog** set
    above 300 s.
  - Each pixel is short for such a long transmission.
  - Rarely used.
- **Use it for:** showing off a detailed photo on an excellent, stable band (e.g. strong VHF/UHF, or local HF).

### Wraase SC2-180
320 × 256 · 182 s · pixel 0.73 ms · VIS 55 · RGB

A slow, clean RGB mode.
- **Benefits:**
  - Long pixel and full RGB colour detail: very clean pictures.
  - Second only to Scottie DX for noise resistance among the 320-pixel modes.
- **Drawbacks:**
  - 3 minutes.
  - Rarely used today, so the other station may not expect it.
- **Use it for:** a very clean standard-resolution picture when the other station uses it.

## 11.5 All modes at a glance

| Mode | Resolution | Time | Pixel (ms) | Noise resistance | Colour detail | Popularity |
|---|---|---|---|---|---|---|
| Martin M1 | 320 × 256 | 114 s | 0.46 | ●●●○○ | full | ●●●●● (Europe) |
| Martin M2 | 320 × 256 | 58 s | 0.23 | ●●○○○ | full | ●●●○○ |
| Scottie S1 | 320 × 256 | 110 s | 0.43 | ●●●○○ | full | ●●●●● (Americas) |
| Scottie S2 | 320 × 256 | 71 s | 0.28 | ●●○○○ | full | ●●●○○ |
| Scottie DX | 320 × 256 | 269 s | 1.08 | ●●●●● | full | ●●○○○ |
| Robot 36 | 320 × 240 | 36 s | 0.28 / 0.14 | ●○○○○ | ¼ | ●●●●○ |
| Robot 72 | 320 × 240 | 72 s | 0.43 / 0.22 | ●●○○○ | ½ | ●●○○○ |
| PD 50 | 320 × 256 | 50 s | 0.29 | ●●○○○ | ½ (vertical) | ●○○○○ |
| PD 90 | 320 × 256 | 90 s | 0.53 | ●●●●○ | ½ (vertical) | ●●○○○ |
| PD 120 | 640 × 496 | 126 s | 0.19 | ●○○○○ | ½ (vertical) | ●●●●○ (ISS) |
| PD 160 | 512 × 400 | 161 s | 0.38 | ●●●○○ | ½ (vertical) | ●○○○○ |
| PD 180 | 640 × 496 | 187 s | 0.29 | ●●○○○ | ½ (vertical) | ●●●○○ |
| PD 240 | 640 × 496 | 248 s | 0.38 | ●●●○○ | ½ (vertical) | ●○○○○ |
| PD 290 | 800 × 616 | 289 s | 0.29 | ●●○○○ | ½ (vertical) | ●○○○○ |
| Wraase SC2-180 | 320 × 256 | 182 s | 0.73 | ●●●●○ | full | ●○○○○ |

Notes on the table:
- **Time** is the picture only. The calibration preamble adds 0.8 s, the VIS header 0.9 s, and the CW ID a few
  seconds more.
- **Colour detail** compares colour with brightness detail. "½ (vertical)" means full across, shared by two
  lines. Robot 36's "¼" means half across and every other line.
- **Noise resistance** follows the pixel time, adjusted for Robot's shorter colour pixels.
- **Popularity** is a rough guide to how often each mode is heard.

## 11.6 Which mode to choose

| Situation | Suggestion |
|---|---|
| Answering a station | **Reply in the mode they sent.** It's the custom, and you know their software receives it. |
| Calling CQ on HF in Europe | **Martin M1** |
| Calling CQ on HF to the Americas | **Scottie S1** |
| Quick exchange on a strong signal | **Martin M2** or **Scottie S2**. **Robot 36** only if the signal is really strong. |
| Weak, noisy or fading signal at standard resolution | **PD 90** (1½ min) or **Scottie DX** (4½ min). Avoid Robot 36, Martin M2 and PD 120. |
| Weak signal, but you want high resolution | **PD 180**, or **PD 240** on a stable band |
| Detailed photo on a good band | **PD 120**, or **PD 290** for the most detail |
| VHF/UHF FM, repeaters | **Robot 36** for speed, **PD 120** for detail |
| Receiving the ISS | Nothing to set: the mode is detected (usually PD 120). Tune to 145.800 MHz FM and allow for Doppler shift. |

## 11.7 Practical tips

- **The other station decides.** The best mode on paper is useless if they don't use it. Listen first: the mode of
  the pictures on the frequency tells you what's expected.
- **Match the mode to the band.** On a noisy evening band a slower mode gets the picture through; on a quiet, strong
  path a faster mode saves everyone's time.
- **Partial pictures still count.** If a long picture is cut short by fading, SSTV Studio keeps what arrived (marked
  *partial*). A slow mode that's 90 % received is often better than a fast one full of noise.
- **Mind your PA.** Every SSTV mode is a continuous carrier. For PD 180 and longer, and for Scottie DX, run the
  rig below full power and keep the **TX watchdog** above the mode's time (PD 290 needs about 300 s).
- **Try a mode safely.** **Export WAV…** on the Transmit tab saves a transmission without keying the rig. Decode
  it with **Decode WAV…** on the Receive tab to see what the other station will get.

---

[← 10. Troubleshooting](10-troubleshooting.md) · [Contents](README.md) · [12. Reference →](12-reference.md)
