# win11-auto-clamshell

A lightweight (~5 MB RAM) Windows 11 background utility that brings macOS-style automatic clamshell mode to Windows laptops. 

It dynamically toggles your laptop's lid-close power behavior based on whether an external monitor is connected and powered on, and automatically puts the laptop to sleep when you turn off your external monitor while the lid is closed.

## Features
- **Automatic Clamshell Mode:** Sets lid-close action to **Do nothing** when an external monitor is connected and the laptop is plugged into AC power.
- **Battery Protection:** Always forces lid-close action to **Sleep** when running on battery power, even if an external monitor is attached.
- **Monitor Power-Off Auto-Sleep:** Uses Windows' native hardware lid sensor (`GUID_LIDSWITCH_STATE_CHANGE`) and WMI display detection (`WmiMonitorConnectionParams`) to put the laptop to sleep within 2 seconds if you power off your external monitor while the lid is closed.
- **Zero External Dependencies:** Uses built-in Windows APIs and compiles using Windows 11's pre-installed .NET C# compiler (`csc.exe`).
- **Ultra-Lightweight:** Uses ~4–6 MB of RAM and ~0% CPU in the background.

## Behavior Matrix

| External Monitor | Power Source | Laptop Lid | Resulting Behavior |
| :--- | :--- | :--- | :--- |
| **ON** | Plugged in (AC) | Closed | **Stays awake** (Clamshell mode active) |
| **ON** | On battery (DC) | Closed | **Sleeps immediately** |
| **Powered OFF / Unplugged** | Plugged in (AC) | Already Closed | **Auto-sleeps within 2 seconds** |
| **Powered OFF / Unplugged** | Plugged in (AC) | Open | **Stays awake** (Switches to laptop screen) |

---

## Repository Structure

```text
win11-auto-clamshell/
├── AutoLidAction.cs     # C# source code (Recommended: ~5 MB RAM)
├── AutoLidAction.ps1    # Pure PowerShell alternative (~35 MB RAM, no compile step)
├── LICENSE              # MIT License
└── README.md            # Setup & usage documentation
```

---

## Option 1: C# Executable Setup (Recommended)

This option compiles `AutoLidAction.cs` into a silent native Windows executable (`AutoLidAction.exe`) with minimal memory usage.

### 1. Save the Source File
Create a folder at `C:\Scripts\` and place `AutoLidAction.cs` inside it (`C:\Scripts\AutoLidAction.cs`).

### 2. Compile & Register in Task Scheduler
Open **Windows PowerShell as Administrator**, paste the following block, and press **Enter**:

```powershell
# Compile AutoLidAction.cs into a silent background executable
csc = "env:WINDIR\Microsoft.NET\Framework64\v4.\(0.30319\csc.\)exe"
& \$csc /target:winexe /out:"C:\Scripts\AutoLidAction.exe" /reference:System.Windows.Forms.dll /reference:System.Management.dll "C:\Scripts\AutoLidAction.cs"

# Register a scheduled task to run silently at user log on
\$action = New-ScheduledTaskAction -Execute "C:\Scripts\AutoLidAction.exe"
\(trigger = New-ScheduledTaskTrigger -AtLogOn\)settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0 -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName "Auto Lid Action" -Action \$action -Trigger trigger -Settings settings -RunLevel Highest -Force

# Start the background service immediately
Start-ScheduledTask -TaskName "Auto Lid Action"
```

---

## Option 2: PowerShell Script Setup (No Compilation)

If you prefer running the script directly without compiling an `.exe`:

1. Place `AutoLidAction.ps1` in `C:\Scripts\AutoLidAction.ps1`.
2. Open **Windows PowerShell as Administrator** and run:

```powershell
\$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "C:\Scripts\AutoLidAction.ps1"'
\(trigger = New-ScheduledTaskTrigger -AtLogOn\)settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit 0 -MultipleInstances IgnoreNew
Register-ScheduledTask -TaskName "Auto Lid Action" -Action \$action -Trigger trigger -Settings settings -RunLevel Highest -Force
Start-ScheduledTask -TaskName "Auto Lid Action"
```

---

## Notes & Hardware Compatibility
- **DisplayPort / USB-C vs. HDMI:** DisplayPort and USB-C monitors drop their hot-plug signal immediately when powered off via their physical power button, triggering auto-sleep reliably. Some HDMI monitors keep an active standby signal when turned off via the power button (cutting power at the wall/power strip will still trigger auto-sleep).
- **Waking from Sleep:** On Windows 11 systems using Modern Standby (`S0 Low Power Idle Network Disconnected`), USB ports power down completely during sleep to prevent battery drain. Open the laptop lid or press the laptop's physical power button to wake it.

---

## Uninstall / Reset to Windows Defaults

To completely remove the background task, delete the files, and reset your lid-close settings back to **Sleep**, run this in **Windows PowerShell as Administrator**:

```powershell
Stop-ScheduledTask -TaskName "Auto Lid Action" -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName "Auto Lid Action" -Confirm:\$false -ErrorAction SilentlyContinue
Stop-Process -Name "AutoLidAction" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "C:\Scripts" -Recurse -Force -ErrorAction SilentlyContinue

powercfg /setdcvalueindex scheme_current sub_buttons lidaction 1
powercfg /setacvalueindex scheme_current sub_buttons lidaction 1
powercfg /setactive scheme_current
```

## License

This project is licensed under the [MIT License](LICENSE).
