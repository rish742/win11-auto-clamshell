# win11-auto-clamshell

A lightweight Windows 11 background utility that brings macOS-style automatic clamshell mode to Windows laptops.

It switches the laptop's lid-close action according to whether an external monitor is connected and powered on. If you turn the external monitor off with its power button while the lid is closed, the laptop goes to sleep, so you don't have to unplug the cable.

## Features
- **Automatic clamshell mode:** while an external monitor is on, the lid-close action is **Do nothing**, both when plugged in and on battery.
- **Sleeps when the monitor is turned off:** with the lid closed, turning the monitor off with its power button, or unplugging it, puts the laptop to sleep within about 10–15 seconds.
- **Reliable detection:** checks the active display paths (`QueryDisplayConfig`) and whether the monitor is actually powered on (WMI `WmiMonitorConnectionParams`). Some DisplayPort monitors stay listed as active display paths after they've been powered off.
- **Safety delays:** the app won't put the laptop to sleep right after it starts, right after the laptop wakes, or while a graphics driver is being installed or reset. It also ignores brief flickers in the display signal.
- **No external dependencies:** compiles with the C# compiler that comes with Windows (`csc.exe`, .NET Framework 4.x).
- **Optional logging:** set `EnableLogging = true` in `AutoLidAction.cs` to troubleshoot. Logging is off by default.

## Behavior matrix

| External monitor | Power source | Laptop lid | Result |
| :--- | :--- | :--- | :--- |
| **On** | Plugged in (AC) | Closed | **Stays awake** (clamshell mode) |
| **On** | On battery (DC) | Closed | **Stays awake** (set `SleepOnBatteryWhenLidClosed = true` to sleep instead) |
| **Turned off / unplugged** | Either | Closed | **Sleeps** within about 10–15 seconds |
| **None** | Either | Closed | **Sleeps** (normal Windows lid action) |
| Any | Either | Open | **Stays awake** |

---

## Repository structure

```text
win11-auto-clamshell/
├── AutoLidAction.cs                          # C# source code (v3)
├── Install-AutoLidAction.ps1                 # Compiles the .exe and registers the logon task
├── AutoLidAction.ps1                         # Older v1 pure-PowerShell alternative (does not have the v3 fixes)
├── Windows_11_Auto_Lid_Monitor_Guide_v3.pdf  # Full setup, power-settings and troubleshooting guide
├── LICENSE                                   # MIT License
└── README.md
```

---

## Install

1. Put `AutoLidAction.cs` and `Install-AutoLidAction.ps1` in `C:\Scripts\`.
2. Open **Windows PowerShell as Administrator** and run:

```powershell
powershell -ExecutionPolicy Bypass -File C:\Scripts\Install-AutoLidAction.ps1
```

This compiles `C:\Scripts\AutoLidAction.exe`, registers the **Auto Lid Action** scheduled task to run at logon, and starts it straight away. Run it again after any change to the `.cs` file.

To check it's running:

```powershell
Get-ScheduledTask -TaskName "Auto Lid Action" | Select-Object TaskName, State
Get-Process AutoLidAction
```

---

## Notes & hardware compatibility
- **DisplayPort / USB-C vs. HDMI:** most DisplayPort and USB-C monitors report themselves as powered off soon after you press their power button. Some HDMI monitors keep a standby signal active. With those monitors, cutting their power at the wall still triggers sleep.
- **Modern Standby:** many Windows 11 laptops only support Modern Standby (S0) and not classic S3 sleep. To avoid battery drain overnight, have Windows hibernate after a period asleep (`powercfg /setacvalueindex scheme_current sub_sleep hibernateidle 1200` hibernates after 20 minutes asleep on AC). See the PDF guide for details.
- **Waking:** open the lid or press the laptop's power button.

---

## Uninstall

Run this in **Windows PowerShell as Administrator**. It removes the task, the `.exe` and the log folder, and sets the lid-close action back to **Sleep**. Your files in `C:\Scripts` are kept.

```powershell
Stop-ScheduledTask -TaskName "Auto Lid Action" -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "Auto Lid Action" -Confirm:$false -ErrorAction SilentlyContinue
Stop-Process -Name "AutoLidAction" -Force -ErrorAction SilentlyContinue
Remove-Item "C:\Scripts\AutoLidAction.exe" -Force -ErrorAction SilentlyContinue
Remove-Item "$env:LOCALAPPDATA\AutoLidAction" -Recurse -Force -ErrorAction SilentlyContinue

powercfg /setdcvalueindex scheme_current sub_buttons lidaction 1
powercfg /setacvalueindex scheme_current sub_buttons lidaction 1
powercfg /setactive scheme_current
```

## License

This project is licensed under the [MIT License](LICENSE).
