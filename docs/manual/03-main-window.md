# 3. The main window

**Header (always visible):**
- The logo and program name.
- The rig's frequency and mode, and the rig status badge: **Connected** in green, **TX** in red while transmitting,
  or an error message.
- The **Connect rig / Disconnect** button.
- **ⓘ**, which opens the About window.

**Red banner:** appears below the header when something goes wrong. Read it, then click **Dismiss**.
- *"Something went wrong (…). SSTV Studio keeps running"* means an unexpected error was caught and written to
  `errors.log`. The program carries on, including reception. Click **Open log** to find the file, and please send it
  with a bug report.
- *"Your settings file couldn't be read…"* appears at start-up if `settings.json` was damaged; see
  [2.7](02-setup.md#27-backup-settings-and-log).

**Tabs:**

| Tab | Purpose |
|---|---|
| **Receive** | Live decoding, waterfall and tuning, received-picture gallery, recording, tools, and the QSO form. The program opens on this tab. |
| **Transmit** | Picture, overlay profiles, mode, the transmit button, TX meters, and the QSO form with this session's log. |
| **Settings** | Everything from [section 2](02-setup.md). |

**About (ⓘ):** shows the version and build date, the .NET, Avalonia and Windows versions, and the data folder, and
lists the libraries the program is built with. Use **Copy system info** when reporting a problem, and
**Open data folder** to reach `settings.json` and `log.adi`.

> 📷 **Screenshot [S5]** `images/s05-about.png`: the About window.

---

[← 2. First-time setup](02-setup.md) · [Contents](README.md) · [4. Receiving pictures →](04-receiving.md)
