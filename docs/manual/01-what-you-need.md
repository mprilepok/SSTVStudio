# 1. What you need

| Item | Notes |
|---|---|
| Windows 10 or 11 PC | With the .NET 10 runtime |
| Transceiver with an audio interface | Built-in USB audio codec (IC-7300, IC-705, FT-991/FT-991A, TS-590 …) or an external interface such as a SignaLink. Verified so far: **Yaesu FT-991** with Hamlib 4.7.2 (dial, PTT and all TX meters). |
| Hamlib | Provides `rigctld.exe` for frequency, mode, PTT, meters and **Fix tuning**. Optional if you use VOX or serial PTT. |
| Wavelog account | Optional, for logging. You need its URL and a **read/write** API key. |

Start the program from the project folder:

```powershell
dotnet run --project src/SstvStudio
```

A splash screen appears for a moment while settings, fonts, audio and the rig connection are prepared. The program
then opens on the **Receive** tab.

---

[Contents](README.md) · [2. First-time setup →](02-setup.md)
