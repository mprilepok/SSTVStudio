# 9. Getting a clean signal

## 9.1 Transmit level and ALC

SSTV needs a clean, undistorted signal. The rig's ALC compresses the audio and smears the picture.

1. Set the rig's power as you like, e.g. 50 W.
2. Send a test picture and watch the **ALC** meter.
3. Lower **TX audio level** (or the rig's USB MOD level) until ALC stays green, at its resting value, while power
   still nearly reaches the set value. The mouse wheel over the slider is handy for this during the transmission.

Example from a Yaesu FT-991 set to 20 W: a TX level of 10 % gave 0.5 W, 25 % gave 3.3 W, and 49 % gave 16 W with
no ALC action. That's a good setting. Power rises roughly with the square of the level, so the last few percent
before ALC matter most.

SSTV sends a continuous carrier for the whole picture. Run the rig below its full-power rating for long modes,
especially PD 180 and longer.

## 9.2 Receive level and tuning

- Adjust the rig's USB audio output level, or Windows' recording level for the codec, until **Input** reads about
  −30 to −10 dB on a received signal.
- Keep noise reduction and narrow filters off. SSTV needs roughly 1100–2300 Hz, so a 2.4–2.8 kHz SSB filter works
  well.
- Use the waterfall and **Fix tuning** ([4.3](04-receiving.md#43-tuning-with-the-waterfall)) to keep stations centred. The receiver
  corrects up to ±250 Hz, but staying close helps on weak signals.

## 9.3 Slant and clock correction

If other stations see **your** pictures slanted, your sound card's clock is slightly off. Ask for the direction and
adjust **Clock correction (ppm)** in Settings:

- Start with ±100 ppm steps, then refine. A positive value means the sound card runs fast.
- Typical corrections are under ±300 ppm.

Pictures you **receive** are corrected automatically (up to ±3 %), so this setting doesn't affect reception. If a
received picture is still slightly slanted, use **Tools → Re-decode** with a **Slant (ppm)** trim.

---

[← 8. A typical SSTV QSO](08-typical-qso.md) · [Contents](README.md) · [10. Troubleshooting →](10-troubleshooting.md)
