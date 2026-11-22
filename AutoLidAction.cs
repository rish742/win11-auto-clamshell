using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

class AutoLidApp : NativeWindow
{
    [DllImport("powrprof.dll", SetLastError = true)]
    static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);

    static Guid GUID_LIDSWITCH_STATE_CHANGE = new Guid("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
    const int WM_POWERBROADCAST = 0x0218;
    const int PBT_POWERSETTINGCHANGE = 0x8013;

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public byte Data;
    }

    static bool isLidClosed = false;
    static int lastExternalCount = -1;
    static System.Windows.Forms.Timer pollTimer;

    public AutoLidApp()
    {
        this.CreateHandle(new CreateParams());
        RegisterPowerSettingNotification(this.Handle, ref GUID_LIDSWITCH_STATE_CHANGE, 0);

        SystemEvents.DisplaySettingsChanged += (s, e) =>
        {
            Thread.Sleep(2000);
            UpdateLidAction();
        };

        pollTimer = new System.Windows.Forms.Timer();
        pollTimer.Interval = 2000; // Check every 2 seconds only while lid is closed
        pollTimer.Tick += (s, e) =>
        {
            if (isLidClosed) UpdateLidAction();
        };
        pollTimer.Start();

        UpdateLidAction();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_POWERBROADCAST && m.WParam.ToInt32() == PBT_POWERSETTINGCHANGE)
        {
            var ps = (POWERBROADCAST_SETTING)Marshal.PtrToStructure(m.LParam, typeof(POWERBROADCAST_SETTING));
            if (ps.PowerSetting == GUID_LIDSWITCH_STATE_CHANGE)
            {
                isLidClosed = (ps.Data == 0);
            }
        }
        base.WndProc(ref m);
    }

    static int GetExternalMonitorCount()
    {
        int count = 0;
        try
        {
            using (var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT Active, VideoOutputTechnology FROM WmiMonitorConnectionParams WHERE Active = True"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    long tech = Convert.ToInt64(obj["VideoOutputTechnology"]);
                    // Exclude internal laptop panel (-2147483648 / 0x80000000 and 11)
                    if (tech != -2147483648 && tech != 2147483648 && tech != 11)
                        count++;
                }
            }
        }
        catch { }
        return count;
    }

    static void RunPowerCfg(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using (var p = Process.Start(psi)) { p.WaitForExit(); }
    }

    static void UpdateLidAction()
    {
        int extCount = GetExternalMonitorCount();

        if (extCount != lastExternalCount)
        {
            lastExternalCount = extCount;
            // Always sleep on battery (1)
            RunPowerCfg("/setdcvalueindex scheme_current sub_buttons lidaction 1");
            // Do nothing (0) when external monitor is on + plugged in, else Sleep (1)
            RunPowerCfg(extCount >= 1
                ? "/setacvalueindex scheme_current sub_buttons lidaction 0"
                : "/setacvalueindex scheme_current sub_buttons lidaction 1");
            RunPowerCfg("/setactive scheme_current");
        }

        // Auto-sleep if external monitor is turned off while lid is closed
        if (extCount == 0 && isLidClosed)
        {
            SetSuspendState(false, false, false);
            Thread.Sleep(5000);
        }
    }

    [STAThread]
    static void Main()
    {
        new AutoLidApp();
        Application.Run();
    }
}
