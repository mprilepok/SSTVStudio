# 10. Troubleshooting

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
| **Banner: "Something went wrong (…)"** | An unexpected error was logged, and the program kept running. Click **Open log** and send `errors.log` with your bug report. |
| **Banner: "Your settings file couldn't be read…"** | `settings.json` was damaged (for example by a power cut). The last good copy is in use; the damaged file is kept as `settings-unreadable-….json`. Check your settings and click **Save settings**. |
| **A Settings field turns red with a range, e.g. "Whole number, 1 to 65535"** | The text isn't a number in that range, so it wasn't applied. The previous value stays in use until you correct it. |

---

[← 9. Getting a clean signal](09-clean-signal.md) · [Contents](README.md) · [11. SSTV modes →](11-modes.md)
