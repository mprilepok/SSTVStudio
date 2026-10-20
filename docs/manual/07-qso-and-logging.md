# 7. The QSO form and logging

The **QSO** form appears on both tabs: on **Receive** under the picture, and on **Transmit** in the right column.
It's the **same** QSO. A callsign typed while their picture comes in is already on the Transmit tab and in your
reply overlay (`{CALL}`, `{RST}`, `{NAME}`, `{GRID}`).

| Field | Notes |
|---|---|
| **Callsign** | The other station. Also fills `{CALL}` in the overlay. Shown in capitals as you type or paste. |
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

> 📷 **Screenshot [S17]** `images/s17-qso-form.png`: a crop of the QSO form on the Transmit tab, filled in, with the
> **THIS SESSION** list showing two logged QSOs (✓).

---

[← 6. Overlay profiles](06-overlays.md) · [Contents](README.md) · [8. A typical SSTV QSO →](08-typical-qso.md)
