# 6. Overlay profiles

The overlay is the text drawn on top of your picture. That's how callsigns and reports are exchanged in SSTV.
A **profile** is a named set of text lines, so you can switch the whole overlay with one click.

## 6.1 Switching profiles

The **OVERLAY** list above the preview (**CQ**, **Reply**, **73**, plus any you add) selects the active profile. The
preview updates immediately.

To change the order of the list, open **Settings → OVERLAY PROFILES** and use **▲** and **▼** next to each profile.
For example, put your most-used profile first. Reordering never changes which profile is selected, and the order
is saved with your settings (and included in **Export settings…**).

> 📷 **Screenshot [S15]** `images/s15-overlays.png`: the same picture three times side by side, as sent with the
> **CQ**, **Reply** and **73** profiles (Export WAV and decode it, or crop the preview).

| Built-in profile | Lines |
|---|---|
| **CQ** | `CQ CQ SSTV` (large, yellow, top) · `de {MYCALL}` · `{MYGRID}` (bottom) |
| **Reply** | `{CALL} de {MYCALL}` (top) · `UR {RST}` · `{NAME}` (bottom) |
| **73** | `{CALL} de {MYCALL}` (top) · `TNX QSO 73!` (large, centre) · `{DATE} {TIME}` (bottom) |

## 6.2 Editing a profile

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

> 📷 **Screenshot [S16]** `images/s16-overlay-editor.png`: **Edit overlay profile** expanded with three lines,
> one of them using a placeholder such as `{CALL} de {MYCALL}`.

## 6.3 Placeholders

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

[← 5. Transmitting a picture](05-transmitting.md) · [Contents](README.md) · [7. The QSO form and logging →](07-qso-and-logging.md)
