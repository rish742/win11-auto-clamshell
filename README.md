# win11-auto-clamshell

![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D6)
![Language](https://img.shields.io/badge/language-C%23%205-239120)
![Version](https://img.shields.io/badge/version-3.0-blue)
![License](https://img.shields.io/badge/license-MIT-green)

A lightweight Windows 11 background app that brings macOS-style automatic **clamshell mode** to Windows laptops.

Close the lid while an external monitor is on and the laptop keeps running. Turn the monitor off with its power button, or unplug it, and the laptop goes to sleep. You never have to touch the cable or open Power Options.

---

## Table of contents

- [Why](#why)
- [Features](#features)
- [Behavior](#behavior)
- [Requirements](#requirements)
- [Installation](#installation)
- [Usage](#usage)
- [Configuration](#configuration)
- [How it works](#how-it-works)
- [Recommended Windows power settings](#recommended-windows-power-settings)
- [Troubleshooting](#troubleshooting)
- [FAQ](#faq)
- [Uninstall](#uninstall)
- [Project structure](#project-structure)
- [Changelog](#changelog)
- [Contributing](#contributing)
- [License](#license)

---

## Why

Windows has one lid-close action per power source: **Sleep** or **Do nothing**. Neither works for a laptop that is sometimes docked and sometimes carried:

- **Sleep** means you can't close the lid and keep working on the external monitor.
- **Do nothing** means the laptop keeps running in your bag after you unplug it.

This app switches the setting for you based on whether an external monitor is actually in use, and puts the laptop to sleep when you're done with the monitor.

## Features

- **Automatic clamshell mode:** while an external monitor is on, the lid-close action is **Do nothing**, both on the charger and on battery.
- **Sleeps when the monitor is turned off:** with the lid closed, turning the monitor off with its power button or unplugging it puts the laptop to sleep.
- **Detects monitors that are switched off but still listed:** some DisplayPort monitors stay in Windows' display layout after their power button is pressed. The app also checks whether the monitor reports itself as powered on (WMI), so it notices these too.
- **Safe by design:** never sleeps right after starting, right after waking, during a graphics driver update, or when the display state can't be read. Brief flickers in the display signal are ignored.
- **Works on Modern Standby laptops:** detects Modern Standby (S0) and enters standby the way those laptops expect.
- **Cleans up after itself:** on exit, logoff or shutdown, the lid action is put back to Sleep. Task Scheduler restarts the app if it ever crashes.
- **No dependencies:** compiled with the C# compiler that comes with Windows. No Visual Studio, no .NET SDK and no third-party libraries are needed.
- **Lightweight:** a few MB of RAM and ~0% CPU.
- **Optional logging** for troubleshooting, off by default.

## Behavior

| External monitor | Power source | Laptop lid | Result |
| :--- | :--- | :--- | :--- |
| **On** | Plugged in | Closed | **Stays awake** (clamshell mode) |
| **On** | Battery | Closed | **Stays awake**. Pulling the charger doesn't sleep the laptop |
| **Turned off with its power button** | Either | Closed | **Sleeps** after about 20 s |
| **Unplugged** | Either | Closed | **Sleeps** after about 8–10 s |
| **None since waking** | Either | Closed | **Sleeps** after 60 s with no keyboard/mouse input |
| Turned off / unplugged | Either | Open | **Stays awake**. The picture moves to the laptop screen |
| Any | Either | Just woke up | **Never sleeps for 45 s**, so you can open the lid and log in |
| Graphics driver updating | Either | Any | **Never sleeps** for 90 s after the graphics adapter resets |

Windows' own idle sleep and screen-off timeouts still apply as normal.

## Requirements

- Windows 11 laptop (Windows 10 should also work, but hasn't been tested)
- .NET Framework 4.x, which is included with Windows
- Administrator rights to install (the app runs with highest privileges so it can change the power plan)
- An external monitor (DisplayPort, USB-C or HDMI)

Tested on a Windows 11 laptop that supports Modern Standby only (no S3 sleep), with a Lenovo Legion 24-10 monitor connected over DisplayPort.

## Installation

1. **Get the files.** Clone the repo or download it as a ZIP (green **Code** button → **Download ZIP**). Then put `AutoLidAction.cs` and `Install-AutoLidAction.ps1` in `C:\Scripts\`. The install script expects that folder.

   ```powershell
   git clone https://github.com/rish742/win11-auto-clamshell.git C:\Scripts
   ```

2. **Run the installer as administrator.** Open the Start menu, type *PowerShell*, right-click **Windows PowerShell** and choose **Run as administrator**. Then run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File C:\Scripts\Install-AutoLidAction.ps1
   ```

   The script:
   - stops any running copy
   - compiles `C:\Scripts\AutoLidAction.exe`
   - registers the **Auto Lid Action** scheduled task to run at logon, restarting the app if it crashes
   - starts the app straight away

3. **Check it's running:**

   ```powershell
   Get-ScheduledTask -TaskName "Auto Lid Action" | Select-Object TaskName, State
   ```

   State should be **Running**. You can also find `AutoLidAction.exe` on the **Details** tab of Task Manager.

> Logging is off by default, so you can ignore the "Log:" line the install script prints.

## Usage

There's nothing to click. Once it's installed:

- **Start working docked:** turn the external monitor on and close the lid. Within about 2 s the lid action switches to Do nothing and the laptop keeps running. If you close the lid within a second or two of plugging in, the laptop can still sleep, because the monitor hasn't connected yet.
- **Finish:** turn the monitor off with the lid closed. About 20 s later the laptop sleeps.
- **Unplug and go:** unplugging the cable or dock with the lid closed also sleeps the laptop, after about 8–10 s.
- **Wake up:** open the lid or press the laptop's power button.

## Configuration

Settings are at the top of `AutoLidAction.cs`. After changing any of them, run the install script again to rebuild.

| Setting | Default | What it does |
| :--- | :--- | :--- |
| `SleepOnBatteryWhenLidClosed` | `false` | `true` = on battery with the lid closed, sleep even if a monitor is on |
| `EnableLogging` | `false` | `true` = log every decision to `%LOCALAPPDATA%\AutoLidAction\AutoLidAction.log` |
| `RestoreSleepActionOnExit` | `true` | Put the lid action back to Sleep when the app exits |
| `ExtraInternalTechnologies` | none | Extra display connection types to treat as the laptop's own screen (see [Troubleshooting](#troubleshooting)) |
| `PollIntervalMs` | `2000` | How often the display state is checked |
| `MonitorGoneDebounceMs` | `8000` | How long a monitor must be gone before the laptop sleeps |
| `EdgeIdleMs` | `5000` | Time with no keyboard/mouse input required before that sleep |
| `NoMonitorTimeoutMs` | `60000` | Lid closed with no monitor at all: sleep after this much idle time |
| `StartupGraceMs` | `20000` | No auto-sleep for this long after the app starts |
| `ResumeGraceMs` | `45000` | No auto-sleep for this long after waking |
| `GpuChangeGraceMs` | `90000` | No auto-sleep for this long after a graphics adapter reset |
| `SleepRetryCooldownMs` | `30000` | Time to wait before trying to sleep again |

The code is written for C# 5, the version of the compiler that comes with Windows. Avoid newer syntax such as `$"..."` strings, `?.`, `nameof`, `out var` and `=>` members.

## How it works

1. A hidden window receives lid-switch, power-source, wake and display-change notifications from Windows.
2. Every 2 s, the app counts the active external displays in two ways:
   - **Display layout** (`QueryDisplayConfig`): which monitors Windows is currently using.
   - **WMI** (`root\wmi` → `WmiMonitorConnectionParams.Active`): whether each monitor reports itself as powered on. This drops about 10 s after a monitor's power button is pressed, even while Windows still lists the monitor.

   It uses the lower of the two counts. If WMI fails or takes more than 3 s, it uses the display layout on its own. A failed check is never treated as "no monitors". Built-in, eDP, LVDS, embedded and virtual displays aren't counted as external.
3. It sets the lid-close action (AC and DC) with the Windows power API. The action is **Do nothing** while an external monitor is on, and **Sleep** otherwise. The setting is corrected if a power plan change resets it.
4. When the lid is closed and a monitor that was in use goes away for 8 s, with no keyboard/mouse input, the laptop goes to sleep. On Modern Standby laptops the app turns the display off instead, which is how those laptops enter standby.

## Recommended Windows power settings

Many Windows 11 laptops only support **Modern Standby**, which still draws a little power while asleep. To avoid draining the battery, have Windows hibernate after the laptop has been asleep for a while. Run these in PowerShell (the first line needs an administrator window):

```powershell
powercfg /hibernate on
powercfg /setacvalueindex scheme_current sub_sleep hibernateidle 1200   # plugged in: hibernate after 20 min asleep
powercfg /setdcvalueindex scheme_current sub_sleep hibernateidle 3600   # battery: hibernate after 1 hour asleep
powercfg /setactive scheme_current
```

This setting only counts time spent asleep, so it never hibernates a laptop that is in use. It belongs to the current power plan, so set it again if you switch plans.

Some plans hide the lid setting. To read it anyway:

```powershell
powercfg /qh scheme_current sub_buttons lidaction
```

## Troubleshooting

First turn logging on: set `EnableLogging = true`, run the install script again, and reproduce the problem. Then open `%LOCALAPPDATA%\AutoLidAction\AutoLidAction.log`. When you're done, set it back to `false` and run the install script again.

| Problem | What to do |
| :--- | :--- |
| Doesn't sleep when the monitor is turned off | Look at the `Displays:` lines in the log. If `WMI powered on:` stays at 1 after the monitor is off, the monitor still reports itself as on. Check its on-screen menu (DP deep sleep, auto input detection, DP version) or unplug the cable instead. |
| Laptop screen is counted as external | Find its `tech=` number in the log, add it to `ExtraInternalTechnologies`, then run the install script again. |
| `WMI unavailable` in the log | Restart the PC. If it keeps happening, check that the **Windows Management Instrumentation** service is running. |
| Sleeps too soon or too late | Adjust `MonitorGoneDebounceMs`, `NoMonitorTimeoutMs` or `ResumeGraceMs`. |
| Should sleep when the charger is pulled | Set `SleepOnBatteryWhenLidClosed = true`. |
| `powercfg failed` in the log | The power API was blocked, often by company policy. Make sure the task runs with highest privileges. |
| Nothing happens at all | In Task Scheduler, find **Auto Lid Action** and check **Last Run Result**. Then run the install script again. |
| Install script: "Compile failed" | Check the error lines above that message. The code must stay valid C# 5 (see [Configuration](#configuration)). |

The [PDF guide](Windows_11_Auto_Lid_Monitor_Guide_v3.pdf) has the full setup steps, the full source code and more detail on all of the above.

## FAQ

**Does it work with HDMI monitors?**
Usually. Some HDMI monitors keep a standby signal active when turned off with their power button. With those monitors, unplug the cable or cut their power at the wall.

**Will it sleep my laptop while I'm using it?**
No. It only sleeps the laptop when the lid is closed and there's been no keyboard/mouse input for a few seconds. It never sleeps the laptop in the first 45 s after waking, or when the display state can't be read.

**Why does it turn the display off instead of calling sleep?**
On Modern Standby laptops the old sleep call (`SetSuspendState`) does nothing. Turning the display off with the lid closed is how those laptops enter standby.

**Can I use it without compiling?**
The old v1 PowerShell script is in this repo's history, but it doesn't have any of the v2 or v3 fixes. The compiled app is recommended.

**Does it change my power plan permanently?**
It only changes the lid-close action, and puts it back to Sleep when it exits. The [uninstall](#uninstall) steps reset it as well.

## Uninstall

Run in **Windows PowerShell as administrator**. This removes the task, the `.exe` and the log folder, and sets the lid-close action back to **Sleep**. Your files in `C:\Scripts` are kept.

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

To also undo the hibernate timing on the charger:

```powershell
powercfg /setacvalueindex scheme_current sub_sleep hibernateidle 0
powercfg /setactive scheme_current
```

## Project structure

```text
win11-auto-clamshell/
├── AutoLidAction.cs                          # The app (C# 5 source)
├── Install-AutoLidAction.ps1                 # Compiles the .exe and registers the logon task
├── Windows_11_Auto_Lid_Monitor_Guide_v3.pdf  # Full setup, power-settings and troubleshooting guide
├── LICENSE                                   # MIT License
└── README.md
```

`AutoLidAction.exe` is built locally by the install script and isn't stored in the repo.

## Changelog

### v3
- Detects a monitor turned off with its power button while the lid is closed (WMI power state, as well as the display layout).
- On battery with a monitor on, the laptop stays awake (new `SleepOnBatteryWhenLidClosed` setting).
- Logging can be turned off and is off by default (`EnableLogging`).
- The log shows the real battery lid action.
- Added the install script and the v3 guide.

### v2
- No longer re-sleeps while you're logging in after a wake. The lid state is re-read on every wake.
- A failed display check no longer counts as "no monitors". The app doesn't sleep during graphics driver updates.
- Sleeps when a monitor goes away, not repeatedly whenever no monitor is present.
- Supports Modern Standby (S0) laptops.
- No blocking calls on the message thread, and the setting is corrected after power plan changes.
- Virtual displays are excluded. The lid action is restored on exit.

### v1
- First release: C# app and a PowerShell alternative.

## Contributing

Issues and pull requests are welcome. If you report a problem, turn on `EnableLogging` and include the relevant log lines, along with your laptop model, monitor and connection type (DisplayPort, USB-C or HDMI).

## License

This project is licensed under the [MIT License](LICENSE). © 2026 Rishab RK
