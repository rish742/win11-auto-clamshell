Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName Microsoft.Win32

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class LidAndPowerHelper : NativeWindow {
    [DllImport("powrprof.dll", SetLastError = true)]
    public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct POWERBROADCAST_SETTING {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    static Guid GUID_LIDSWITCH_STATE_CHANGE = new Guid("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
    const int WM_POWERBROADCAST = 0x0218;
    const int PBT_POWERSETTINGCHANGE = 0x8013;

    public static bool IsLidClosed = false;

    public LidAndPowerHelper() {
        this.CreateHandle(new CreateParams());
        RegisterPowerSettingNotification(this.Handle, ref GUID_LIDSWITCH_STATE_CHANGE, 0);
    }

    protected override void WndProc(ref Message m) {
        if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt32() == PBT_POWERSETTINGCHANGE) {
            POWERBROADCAST_SETTING ps = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(m.LParam, typeof(POWERBROADCAST_SETTING));
            if (ps.PowerSetting == GUID_LIDSWITCH_STATE_CHANGE) {
                IsLidClosed = (ps.Data == 0);
            }
        }
        base.WndProc(ref m);
    }
}
"@ -ReferencedAssemblies System.Windows.Forms

$lidWatcher = New-Object LidAndPowerHelper
$script:lastExternalCount = -1

function Update-LidAction {
    param([int]$DelaySeconds = 0)
    if ($DelaySeconds -gt 0) { Start-Sleep -Seconds $DelaySeconds }

    # Check specifically for EXTERNAL active monitors
    $externalMonitors = @(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorConnectionParams -ErrorAction SilentlyContinue | Where-Object {
        $_.Active -eq $true -and $_.VideoOutputTechnology -ne -2147483648 -and $_.VideoOutputTechnology -ne 11
    })

    $extCount = $externalMonitors.Count
    $lidClosed = [LidAndPowerHelper]::IsLidClosed

    # Only update power settings when external monitor state actually changes
    if ($extCount -ne $script:lastExternalCount) {
        $script:lastExternalCount = $extCount
        powercfg /setdcvalueindex scheme_current sub_buttons lidaction 1

        if ($extCount -ge 1) {
            powercfg /setacvalueindex scheme_current sub_buttons lidaction 0
            $status = "Do nothing"
        } else {
            powercfg /setacvalueindex scheme_current sub_buttons lidaction 1
            $status = "Sleep"
        }
        powercfg /setactive scheme_current
        "$(Get-Date) - External: $extCount | LidClosed: $lidClosed | LidAction: $status" | Out-File "C:\Scripts\log.txt" -Append
    }

    # Auto-Sleep Rule: If external monitor is OFF (0) AND laptop lid is Closed ($true)
    if ($extCount -eq 0 -and $lidClosed) {
        "$(Get-Date) - External turned OFF while lid closed -> Sleeping now!" | Out-File "C:\Scripts\log.txt" -Append
        [LidAndPowerHelper]::SetSuspendState($false, $false, $false) | Out-Null
        Start-Sleep -Seconds 5
    }
}

# Run once immediately on startup/login
Update-LidAction -DelaySeconds 2

# Listen for Windows display plug/unplug events
$action = { Update-LidAction -DelaySeconds 2 }
Register-ObjectEvent -InputObject ([Microsoft.Win32.SystemEvents]) -EventName "DisplaySettingsChanged" -Action $action | Out-Null

$tick = 0
while ($true) {
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 500
    $tick++

    # While lid is closed, check every 2 seconds in case the external monitor was powered off
    if ([LidAndPowerHelper]::IsLidClosed -and ($tick % 4 -eq 0)) {
        Update-LidAction -DelaySeconds 0
    }
}
