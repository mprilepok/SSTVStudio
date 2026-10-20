# Screenshots for the user manual

The manual (`docs/manual/`) has a 📷 marker wherever a screenshot belongs. Each marker gives a number (S1…S18), a file name
under `docs/manual/images/`, and what the picture should show. This list is the checklist.

## How to take them

- **Window size:** take whole-window shots at the same size, e.g. 1280 × 860, at 100 % Windows display scaling.
- **Cropped shots:** crop "crop" shots to the card named, with a few pixels of margin.
- **Format and size:** save PNG, and keep files under about 300 KB (PNG is fine for UI; reduce colours if needed).
- **Real-looking content:** use a real station setup: your callsign, the rig connected, a real received picture.
  The test pattern is fine for the Transmit tab.
- **Privacy:** blank out the **Wavelog API key**, and anything else private (other stations' personal data in the
  log, your home folder path if you prefer).
- **Placing a shot in the manual:** replace the marker (the whole `> 📷 …` block) with an image line, for example:

  ```markdown
  ![The Receive tab while a picture comes in](images/s01-main-window.png)
  ```

## Checklist

| # | File | Manual section | What to show | Needs the rig / a signal |
|---|---|---|---|---|
| S1 | `s01-main-window.png` | top of manual, README | Whole window, Receive tab, picture half drawn, green tuning line | Yes: a picture being received |
| S2 | `s02-settings-tab.png` | 2 First-time setup | Whole Settings tab, filled in (API key blanked) | Rig connected |
| S3 | `s03-header-connected.png` | 2.2 Rig control | Crop of the header: frequency, mode, green **Connected** | Rig connected |
| S4 | `s04-backup-card.png` | 2.7 Backup | Crop of the BACKUP card with "Log: n QSOs" | No |
| S5 | `s05-about.png` | 3 Main window | The About window | No |
| S6 | `s06-receiver-card.png` | 4.1 Start listening | Crop of the RECEIVER card during reception (status, Input, waterfall) | Yes |
| S7 | `s07-zoom.png` | 4.2 Automatic | Received picture at 200 % with scrollbars, zoom bar visible | A received picture |
| S8 | `s08-fix-tuning.png` | 4.3 Waterfall | Crop: yellow "Signal 40 Hz too high" + Fix tuning; optionally "Retuned … (+40 Hz)" | Yes: a mistuned picture |
| S9 | `s09-tools.png` | 4.4 Tools | Tools panel expanded | No |
| S10 | `s10-noise-filter.png` (optional) | 4.4 Tools | One noisy picture with filter Off / Low / High, side by side | A weak received picture |
| S11 | `s11-gallery.png` | 4.6 Gallery | Crop of RECEIVED with several pictures, one *partial*, one *re-decoded* | Received pictures |
| S12 | `s12-transmit-tab.png` | 5 Transmitting | Whole Transmit tab: real photo, **Reply** chip, Martin M1 | No (QSO form filled) |
| S13 | `s13-mode-card.png` | 5.2 Choose the mode | Crop of the MODE card with the mode list open | No |
| S14 | `s14-tx-meters.png` | 5.4 TX meters | Crop of TX METERS + TRANSMIT progress during a transmission | Yes: transmitting (dummy load) |
| S15 | `s15-overlays.png` | 6.1 Profiles | One picture with the CQ, Reply and 73 overlays, side by side | No |
| S16 | `s16-overlay-editor.png` | 6.2 Editing | Edit overlay profile expanded, 3 lines, one with `{CALL} de {MYCALL}` | No |
| S17 | `s17-qso-form.png` | 7 QSO form | Crop of the QSO form (Transmit tab) filled in, THIS SESSION with two ✓ QSOs | Wavelog set up |
| S18 | `s18-mode-examples.png` (optional) | 11.2 Modes (11-modes.md) | Test pattern received in Robot 36, Martin M1, Scottie DX and PD 120, side by side | No (decode exported WAVs) |

Shots marked "No" don't need the radio. They can also be rendered headlessly by the test harness
(`window.CaptureRenderedFrame()`), which gives identical sizes every time. S15 and S18 can be produced the same
way: export WAVs and decode them with **Decode WAV…**.
