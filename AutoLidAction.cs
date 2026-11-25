// AutoLidAction.cs  -  v3
//
// Keeps a Windows laptop awake in clamshell mode while an external display is
// active, and puts it to sleep when that display goes away with the lid closed.
//
// Written for the C# 5 compiler that ships with Windows (.NET Framework 4.x):
//   %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe
//       /out:C:\Scripts\AutoLidAction.exe /reference:System.Windows.Forms.dll
//       /reference:System.Management.dll C:\Scripts\AutoLidAction.cs
//
// Log file (only when EnableLogging = true): %LOCALAPPDATA%\AutoLidAction\AutoLidAction.log

using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

sealed class AutoLidApp : NativeWindow
{
    // ============================ Tunables ============================

    const int PollIntervalMs        = 2000;   // how often display state is checked
    const int StartupGraceMs        = 20000;  // no auto-sleep right after the app starts
    const int ResumeGraceMs         = 45000;  // no auto-sleep right after waking up (monitors re-link slowly)
    const int GpuChangeGraceMs      = 90000;  // no auto-sleep while a graphics driver is installed/reset
    const int SleepRetryCooldownMs  = 30000;  // wait this long before trying to sleep again
    const int MonitorGoneDebounceMs = 8000;   // display must be gone this long before sleeping (ignores flickers)
    const int EdgeIdleMs            = 5000;   // ...and no keyboard/mouse input for this long
    const int NoMonitorTimeoutMs    = 60000;  // lid closed with no display at all: sleep after this much idle time
    const int BatteryLidClosedMs    = 10000;  // on battery with lid closed (e.g. charger pulled): sleep after this

    // false: on battery with the lid closed and a monitor on, stay awake (the lid
    // action on battery is then also "Do nothing" while a monitor is on).
    static readonly bool SleepOnBatteryWhenLidClosed = false;
    static readonly bool RestoreSleepActionOnExit    = true;

    // true: write every decision to %LOCALAPPDATA%\AutoLidAction\AutoLidAction.log
    // (turn on again if something needs troubleshooting).
    static readonly bool EnableLogging = false;

    // If your laptop's own screen is wrongly counted as external, add the
    // "tech=" number the log shows for it here, e.g. new uint[] { 10 }.
    static readonly uint[] ExtraInternalTechnologies = new uint[0];

    // ============================ Win32 ============================

    const int WM_ENDSESSION          = 0x0016;
    const int WM_DISPLAYCHANGE       = 0x007E;
    const int WM_SYSCOMMAND          = 0x0112;
    const int WM_POWERBROADCAST      = 0x0218;
    const int WM_DEVICECHANGE        = 0x0219;
    const int PBT_APMSUSPEND         = 0x0004;
    const int PBT_APMRESUMESUSPEND   = 0x0007;
    const int PBT_APMRESUMEAUTOMATIC = 0x0012;
    const int PBT_POWERSETTINGCHANGE = 0x8013;
    const int DBT_DEVICEARRIVAL        = 0x8000;
    const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    const int DBT_DEVTYP_DEVICEINTERFACE = 5;
    const int SC_MONITORPOWER        = 0xF170;
    const uint QDC_ONLY_ACTIVE_PATHS = 0x00000002;
    const int ERROR_INSUFFICIENT_BUFFER = 122;
    const int PATH_INFO_SIZE = 72;   // sizeof(DISPLAYCONFIG_PATH_INFO)
    const int MODE_INFO_SIZE = 64;   // sizeof(DISPLAYCONFIG_MODE_INFO)
    const int TARGET_TECH_OFFSET = 36;
    const uint LID_DO_NOTHING = 0, LID_SLEEP = 1;

    static Guid GUID_LIDSWITCH_STATE_CHANGE = new Guid("BA3E0F4D-B817-4094-A2D1-D56379E6A0F3");
    static Guid GUID_ACDC_POWER_SOURCE      = new Guid("5D3E9A59-E9D5-4B00-A6BD-FF34FF516548");
    static Guid GUID_CONSOLE_DISPLAY_STATE  = new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47");
    static Guid GUID_SYSTEM_BUTTON_SUBGROUP = new Guid("4F971E89-EEBD-4455-A8DE-9E59040E7347");
    static Guid GUID_LIDCLOSE_ACTION        = new Guid("5CA83367-6E45-459F-A27B-476B1D01C936");
    static Guid GUID_DEVINTERFACE_DISPLAY_ADAPTER = new Guid("5B45201D-F2F2-4F3B-85BB-30FF1F953599");

    [StructLayout(LayoutKind.Sequential)]
    struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int dbcc_size;
        public int dbcc_devicetype;
        public int dbcc_reserved;
        public Guid dbcc_classguid;
        public short dbcc_name;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    [DllImport("powrprof.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool SetSuspendState([MarshalAs(UnmanagedType.U1)] bool hibernate,
                                       [MarshalAs(UnmanagedType.U1)] bool forceCritical,
                                       [MarshalAs(UnmanagedType.U1)] bool disableWakeEvent);
    [DllImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    static extern bool GetPwrCapabilities([Out] byte[] caps);
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);

    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid setting, int flags);
    [DllImport("user32.dll")] static extern bool UnregisterPowerSettingNotification(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, ref DEV_BROADCAST_DEVICEINTERFACE filter, int flags);
    [DllImport("user32.dll")] static extern bool UnregisterDeviceNotification(IntPtr h);
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint numPaths, IntPtr paths, ref uint numModes, IntPtr modes, IntPtr topologyId);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // ============================ State ============================

    // Kept in a static field: NativeWindow only holds a weak reference to
    // itself, so an unreferenced instance can be garbage-collected and stop
    // receiving lid messages (the lid state then gets stuck).
    static AutoLidApp instance;

    readonly bool isModernStandby;
    readonly System.Windows.Forms.Timer pollTimer = new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer checkSoonTimer = new System.Windows.Forms.Timer();
    string checkSoonReason = "";

    IntPtr hLidNotify, hAcDcNotify, hDisplayNotify, hDevNotify;

    bool? lidClosed;               // null = not known yet: never auto-sleep on unknown
    long lidClosedAtMs;
    bool onAC = true;
    int consoleDisplayState = -1;  // 0 off, 1 on, 2 dimmed
    bool asleep;
    volatile bool sleepInProgress;

    int lastCount = int.MinValue;
    string lastDetail = "";
    long zeroSinceMs;              // when the last external display disappeared (0 = one is present)
    long dcLidClosedSinceMs;
    bool externalSeenSinceWake;    // an external display was active at some point since the last wake
    bool userPresentSinceWake = true;
    long suppressUntilMs;
    long policyRetryAfterMs;
    bool shutDown;

    // ============================ Startup ============================

    AutoLidApp()
    {
        CreateHandle(new CreateParams());   // hidden top-level window: receives broadcasts

        isModernStandby = DetectModernStandby();
        suppressUntilMs = NowMs() + StartupGraceMs;

        hAcDcNotify    = RegisterPowerSettingNotification(Handle, ref GUID_ACDC_POWER_SOURCE, 0);
        hDisplayNotify = RegisterPowerSettingNotification(Handle, ref GUID_CONSOLE_DISPLAY_STATE, 0);
        RefreshLidState();   // Windows replies right away with the current lid state

        var filter = new DEV_BROADCAST_DEVICEINTERFACE();
        filter.dbcc_size = Marshal.SizeOf(typeof(DEV_BROADCAST_DEVICEINTERFACE));
        filter.dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE;
        filter.dbcc_classguid = GUID_DEVINTERFACE_DISPLAY_ADAPTER;
        hDevNotify = RegisterDeviceNotification(Handle, ref filter, 0);

        pollTimer.Interval = PollIntervalMs;
        pollTimer.Tick += delegate { SafeEvaluate("poll"); };
        pollTimer.Start();

        checkSoonTimer.Interval = 1500;
        checkSoonTimer.Tick += delegate { checkSoonTimer.Stop(); SafeEvaluate(checkSoonReason); };

        SafeEvaluate("startup");
    }

    static bool DetectModernStandby()
    {
        // SYSTEM_POWER_CAPABILITIES: LidPresent @2, SystemS3 @5, AoAc @20
        var caps = new byte[128];
        if (!GetPwrCapabilities(caps)) { Log("GetPwrCapabilities failed; assuming classic S3 sleep"); return false; }
        bool aoac = caps[20] != 0, s3 = caps[5] != 0, lid = caps[2] != 0;
        Log("Started. Sleep type: " + (aoac ? "Modern Standby (S0)" : s3 ? "classic S3" : "NONE reported") +
            ", lid switch present: " + lid);
        return aoac;
    }

    // ============================ Messages ============================

    protected override void WndProc(ref Message m)
    {
        try
        {
            switch (m.Msg)
            {
                case WM_POWERBROADCAST: OnPowerBroadcast((int)m.WParam.ToInt64(), m.LParam); break;
                case WM_DISPLAYCHANGE:  CheckSoon("display change"); break;
                case WM_DEVICECHANGE:   OnDeviceChange((int)m.WParam.ToInt64(), m.LParam); break;
                case WM_ENDSESSION:     if (m.WParam != IntPtr.Zero) Shutdown("Windows session ending"); break;
            }
        }
        catch (Exception ex) { Log("ERROR in WndProc: " + ex); }
        base.WndProc(ref m);
    }

    void OnPowerBroadcast(int evt, IntPtr lParam)
    {
        switch (evt)
        {
            case PBT_APMSUSPEND:
                Log("System suspending");
                asleep = true;
                externalSeenSinceWake = false;
                userPresentSinceWake = false;
                zeroSinceMs = 0;
                break;
            case PBT_APMRESUMEAUTOMATIC: OnWake(false, true, "resume (automatic)"); break;
            case PBT_APMRESUMESUSPEND:   OnWake(true, asleep, "resume (user)"); break;
            case PBT_POWERSETTINGCHANGE: OnPowerSetting(lParam); break;
        }
    }

    // freshWake: a real sleep happened, so forget what was connected before it.
    // (Automatic resume always arrives first; the user resume may follow it.)
    void OnWake(bool byUser, bool freshWake, string what)
    {
        asleep = false;
        long until = NowMs() + ResumeGraceMs;
        if (until > suppressUntilMs) suppressUntilMs = until;
        if (byUser) userPresentSinceWake = true;
        if (freshWake) { externalSeenSinceWake = false; zeroSinceMs = 0; }
        Log("Wake: " + what + "; auto-sleep paused for " + ResumeGraceMs / 1000 + "s");
        RefreshLidState();   // never trust the lid state from before sleep
        policyRetryAfterMs = 0;
        CheckSoon(what);
    }

    void OnPowerSetting(IntPtr p)
    {
        Guid setting = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
        int len = Marshal.ReadInt32(p, 16);
        int data = len >= 4 ? Marshal.ReadInt32(p, 20) : len >= 1 ? Marshal.ReadByte(p, 20) : 0;

        if (setting == GUID_LIDSWITCH_STATE_CHANGE)
        {
            bool closed = data == 0;
            if (lidClosed != closed)
            {
                Log("Lid " + (closed ? "closed" : "open"));
                if (closed) lidClosedAtMs = NowMs();
                else userPresentSinceWake = true;
            }
            lidClosed = closed;
            CheckSoon("lid");
        }
        else if (setting == GUID_ACDC_POWER_SOURCE)
        {
            bool ac = data == 0;
            if (ac != onAC) Log("Power source: " + (ac ? "AC adapter" : "battery"));
            onAC = ac;
            CheckSoon("power source");
        }
        else if (setting == GUID_CONSOLE_DISPLAY_STATE)
        {
            int prev = consoleDisplayState;
            consoleDisplayState = data;
            // On Modern Standby laptops "display turned back on" is how a wake
            // looks; classic PBT_APMRESUME* messages may not be sent.
            if (isModernStandby && prev == 0 && data != 0) OnWake(true, true, "display on (Modern Standby wake)");
        }
    }

    void OnDeviceChange(int evt, IntPtr lParam)
    {
        if ((evt != DBT_DEVICEARRIVAL && evt != DBT_DEVICEREMOVECOMPLETE) || lParam == IntPtr.Zero) return;
        if (Marshal.ReadInt32(lParam, 4) != DBT_DEVTYP_DEVICEINTERFACE) return;
        byte[] g = new byte[16];
        Marshal.Copy(new IntPtr(lParam.ToInt64() + 12), g, 0, 16);
        if (new Guid(g) != GUID_DEVINTERFACE_DISPLAY_ADAPTER) return;

        // Graphics driver install/update/reset: displays vanish for a while.
        long until = NowMs() + GpuChangeGraceMs;
        if (until > suppressUntilMs) suppressUntilMs = until;
        zeroSinceMs = 0;
        Log("Graphics adapter " + (evt == DBT_DEVICEARRIVAL ? "arrived" : "removed") +
            " (driver update/reset?) - auto-sleep paused for " + GpuChangeGraceMs / 1000 + "s");
        CheckSoon("graphics adapter change");
    }

    void RefreshLidState()
    {
        if (hLidNotify != IntPtr.Zero) UnregisterPowerSettingNotification(hLidNotify);
        lidClosed = null;
        hLidNotify = RegisterPowerSettingNotification(Handle, ref GUID_LIDSWITCH_STATE_CHANGE, 0);
        if (hLidNotify == IntPtr.Zero) Log("WARNING: could not register for lid notifications (" + Marshal.GetLastWin32Error() + ")");
    }

    void CheckSoon(string reason)
    {
        checkSoonReason = reason;
        checkSoonTimer.Stop();
        checkSoonTimer.Start();
    }

    // ============================ Decision logic ============================

    void SafeEvaluate(string reason)
    {
        try { Evaluate(reason); }
        catch (Exception ex) { Log("ERROR in Evaluate: " + ex); }
    }

    void Evaluate(string reason)
    {
        if (asleep || shutDown) return;
        long now = NowMs();

        string detail;
        int count = CountActiveExternalDisplays(out detail);
        if (count != lastCount || detail != lastDetail)
        {
            Log("Displays: " + (count < 0 ? "UNKNOWN (query failed)" : count + " external active") +
                " [" + detail + "] (" + reason + ")");
            lastCount = count; lastDetail = detail;
        }

        // Display query failed (typical mid driver update): do nothing at all.
        if (count < 0) { zeroSinceMs = 0; return; }

        if (count > 0) { externalSeenSinceWake = true; zeroSinceMs = 0; }
        else if (zeroSinceMs == 0) zeroSinceMs = now;

        EnsureLidPolicy(count > 0, now);

        bool closed = lidClosed == true;
        if (closed && !onAC) { if (dcLidClosedSinceMs == 0) dcLidClosedSinceMs = now; }
        else dcLidClosedSinceMs = 0;

        if (!closed || sleepInProgress || now < suppressUntilMs) return;
        if (SystemInformation.TerminalServerSession) return;   // someone is using it over Remote Desktop

        long idle = IdleMs();
        string why = null;

        if (SleepOnBatteryWhenLidClosed && dcLidClosedSinceMs != 0 && now - dcLidClosedSinceMs >= BatteryLidClosedMs)
        {
            why = "on battery with the lid closed";
        }
        else if (count == 0)
        {
            long gone = now - Math.Max(zeroSinceMs, lidClosedAtMs);
            if (externalSeenSinceWake && gone >= MonitorGoneDebounceMs && idle >= EdgeIdleMs)
                why = "external display gone for " + gone / 1000 + "s with lid closed";
            else if (userPresentSinceWake && gone >= NoMonitorTimeoutMs && idle >= NoMonitorTimeoutMs)
                why = "lid closed, no external display and idle for " + idle / 1000 + "s";
        }

        if (why != null) GoToSleep(why);
    }

    void GoToSleep(string why)
    {
        Log("SLEEP: " + why);
        suppressUntilMs = NowMs() + SleepRetryCooldownMs;

        if (isModernStandby)
        {
            // SetSuspendState does not work on Modern Standby laptops.
            // Turning the display off is what makes them enter standby.
            PostMessage(Handle, WM_SYSCOMMAND, new IntPtr(SC_MONITORPOWER), new IntPtr(2));
            return;
        }

        // SetSuspendState blocks until resume; keep it off the message thread so
        // suspend/resume/lid messages are still handled.
        sleepInProgress = true;
        var t = new Thread(delegate ()
        {
            try
            {
                if (!SetSuspendState(false, false, false))
                    Log("SetSuspendState failed, error " + Marshal.GetLastWin32Error());
            }
            catch (Exception ex) { Log("SetSuspendState threw: " + ex.Message); }
            finally { sleepInProgress = false; }
        });
        t.IsBackground = true;
        t.Start();
    }

    // ============================ Displays ============================

    static bool IsInternalTechnology(uint tech)
    {
        switch (tech)
        {
            case 0x80000000: // INTERNAL (laptop panel)
            case 6:          // LVDS (older laptop panels)
            case 11:         // DISPLAYPORT_EMBEDDED (eDP panel)
            case 13:         // UDI_EMBEDDED
            case 17:         // INDIRECT_VIRTUAL (virtual / software displays)
            case 0xFFFFFFFE: // UNINITIALIZED
                return true;
        }
        return Array.IndexOf(ExtraInternalTechnologies, tech) >= 0;
    }

    static string TechName(uint tech)
    {
        switch (tech)
        {
            case 0: return "VGA"; case 4: return "DVI"; case 5: return "HDMI"; case 6: return "LVDS";
            case 10: return "DisplayPort"; case 11: return "eDP-internal"; case 13: return "UDI-internal";
            case 15: return "Miracast"; case 16: return "USB/dock(indirect)"; case 17: return "virtual";
            case 18: return "USB-C/Thunderbolt DP"; case 0x80000000: return "internal";
            case 0xFFFFFFFF: return "other";
        }
        return "unknown";
    }

    // Number of external displays that are in use AND powered on, or -1 if Windows
    // could not be asked.
    //
    // QueryDisplayConfig (the data Windows Display Settings uses) is the main source,
    // but with the lid closed a monitor switched off with its own power button stays
    // in the display layout (there is no other screen to move the desktop to), so it
    // never drops to 0. WMI's WmiMonitorConnectionParams.Active does drop about 10 s
    // after the monitor is switched off, so the count is capped at the WMI count.
    // If WMI fails or times out, the QueryDisplayConfig count is used on its own:
    // a failed query must never be treated as "0 monitors" (a v1 bug).
    static int CountActiveExternalDisplays(out string detail)
    {
        int count = CountActiveExternalPaths(out detail);
        if (count <= 0) return count;

        string wmiError;
        int wmiCount = CountWmiActiveExternalMonitors(out wmiError);
        if (wmiCount < 0)
        {
            detail += " | WMI unavailable (" + wmiError + "), using display paths only";
            return count;
        }
        detail += " | WMI powered on: " + wmiCount;
        return Math.Min(count, wmiCount);
    }

    // External monitors WMI reports as Active, or -1 (with the reason) if WMI failed.
    static int CountWmiActiveExternalMonitors(out string error)
    {
        error = null;
        try
        {
            var options = new EnumerationOptions();
            options.Timeout = TimeSpan.FromSeconds(3);   // WMI can hang; never block the message thread for long
            options.ReturnImmediately = true;
            options.Rewindable = false;
            using (var searcher = new ManagementObjectSearcher(@"root\wmi",
                "SELECT Active, VideoOutputTechnology FROM WmiMonitorConnectionParams", options))
            using (var results = searcher.Get())
            {
                int count = 0;
                foreach (ManagementBaseObject obj in results)
                {
                    using (obj)
                    {
                        object active = obj["Active"], techObj = obj["VideoOutputTechnology"];
                        if (active == null || techObj == null || !(bool)active) continue;
                        uint tech = unchecked((uint)Convert.ToInt64(techObj));
                        if (!IsInternalTechnology(tech)) count++;
                    }
                }
                return count;
            }
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return -1;
        }
    }

    // Number of *active* external display paths, or -1 if Windows could not be asked.
    static int CountActiveExternalPaths(out string detail)
    {
        detail = "";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            uint numPaths, numModes;
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out numPaths, out numModes) != 0) return -1;

            IntPtr paths = Marshal.AllocHGlobal((int)Math.Max(1, numPaths) * PATH_INFO_SIZE);
            IntPtr modes = Marshal.AllocHGlobal((int)Math.Max(1, numModes) * MODE_INFO_SIZE);
            try
            {
                int err = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);
                if (err == ERROR_INSUFFICIENT_BUFFER) continue;   // display layout changed between calls
                if (err != 0) return -1;

                int count = 0;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < numPaths; i++)
                {
                    uint tech = unchecked((uint)Marshal.ReadInt32(paths, i * PATH_INFO_SIZE + TARGET_TECH_OFFSET));
                    bool external = !IsInternalTechnology(tech);
                    if (external) count++;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(TechName(tech)).Append(" tech=").Append(tech).Append(external ? " ext" : " int");
                }
                detail = sb.Length == 0 ? "no active displays" : sb.ToString();
                return count;
            }
            finally
            {
                Marshal.FreeHGlobal(paths);
                Marshal.FreeHGlobal(modes);
            }
        }
        return -1;
    }

    // ============================ Lid policy ============================

    void EnsureLidPolicy(bool hasExternal, long now)
    {
        if (now < policyRetryAfterMs) return;
        uint wantAc = hasExternal ? LID_DO_NOTHING : LID_SLEEP;
        // Battery: Windows would otherwise sleep as soon as the lid is closed.
        uint wantDc = (hasExternal && !SleepOnBatteryWhenLidClosed) ? LID_DO_NOTHING : LID_SLEEP;
        if (!SetLidPolicy(wantAc, wantDc, true)) policyRetryAfterMs = now + 60000;
    }

    // Checks the active power plan every time (cheap), so a plan switch or
    // someone editing the setting gets corrected, and only writes on a change.
    static bool SetLidPolicy(uint wantAc, uint wantDc, bool log)
    {
        IntPtr p;
        if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0) return PowerCfgFallback(wantAc, wantDc);
        Guid scheme = (Guid)Marshal.PtrToStructure(p, typeof(Guid));
        LocalFree(p);

        uint ac, dc;
        bool readOk = PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref GUID_SYSTEM_BUTTON_SUBGROUP, ref GUID_LIDCLOSE_ACTION, out ac) == 0;
        readOk &= PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref GUID_SYSTEM_BUTTON_SUBGROUP, ref GUID_LIDCLOSE_ACTION, out dc) == 0;
        if (readOk && ac == wantAc && dc == wantDc) return true;

        uint e1 = PowerWriteACValueIndex(IntPtr.Zero, ref scheme, ref GUID_SYSTEM_BUTTON_SUBGROUP, ref GUID_LIDCLOSE_ACTION, wantAc);
        uint e2 = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref GUID_SYSTEM_BUTTON_SUBGROUP, ref GUID_LIDCLOSE_ACTION, wantDc);
        uint e3 = PowerSetActiveScheme(IntPtr.Zero, ref scheme);
        if (e1 == 0 && e2 == 0 && e3 == 0)
        {
            if (log) Log("Lid action set: plugged in = " + (wantAc == 0 ? "Do nothing" : "Sleep") + ", battery = " + (wantDc == 0 ? "Do nothing" : "Sleep"));
            return true;
        }
        Log("Power API write failed (" + e1 + "/" + e2 + "/" + e3 + "), trying powercfg");
        return PowerCfgFallback(wantAc, wantDc);
    }

    static bool PowerCfgFallback(uint wantAc, uint wantDc)
    {
        bool ok = RunPowerCfg("/setacvalueindex scheme_current sub_buttons lidaction " + wantAc)
                & RunPowerCfg("/setdcvalueindex scheme_current sub_buttons lidaction " + wantDc)
                & RunPowerCfg("/setactive scheme_current");
        Log(ok ? "Lid action set via powercfg" : "ERROR: powercfg failed too; will retry in 60s");
        return ok;
    }

    static bool RunPowerCfg(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg.exe", args) { CreateNoWindow = true, UseShellExecute = false };
            using (var p = Process.Start(psi))
            {
                if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return false; }
                return p.ExitCode == 0;
            }
        }
        catch (Exception ex) { Log("powercfg " + args + " failed: " + ex.Message); return false; }
    }

    // ============================ Shutdown ============================

    void Shutdown(string why)
    {
        if (shutDown) return;
        shutDown = true;
        Log("Stopping: " + why);
        pollTimer.Stop();
        checkSoonTimer.Stop();
        // If this app isn't running, closing the lid should do the normal thing.
        if (RestoreSleepActionOnExit) SetLidPolicy(LID_SLEEP, LID_SLEEP, true);
        if (hLidNotify != IntPtr.Zero) UnregisterPowerSettingNotification(hLidNotify);
        if (hAcDcNotify != IntPtr.Zero) UnregisterPowerSettingNotification(hAcDcNotify);
        if (hDisplayNotify != IntPtr.Zero) UnregisterPowerSettingNotification(hDisplayNotify);
        if (hDevNotify != IntPtr.Zero) UnregisterDeviceNotification(hDevNotify);
        hLidNotify = hAcDcNotify = hDisplayNotify = hDevNotify = IntPtr.Zero;
    }

    // ============================ Helpers ============================

    static long NowMs() { return (long)GetTickCount64(); }   // monotonic, unaffected by clock changes

    static long IdleMs()
    {
        var lii = new LASTINPUTINFO();
        lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
        if (!GetLastInputInfo(ref lii)) return 0;   // unknown: act as if the user is active
        return unchecked((uint)Environment.TickCount - lii.dwTime);
    }

    static readonly object logLock = new object();
    static string logPath;

    static void Log(string msg)
    {
        if (!EnableLogging) return;
        try
        {
            lock (logLock)
            {
                if (logPath == null)
                {
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoLidAction");
                    Directory.CreateDirectory(dir);
                    logPath = Path.Combine(dir, "AutoLidAction.log");
                }
                var fi = new FileInfo(logPath);
                if (fi.Exists && fi.Length > 512 * 1024)
                {
                    string old = logPath + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(logPath, old);
                }
                File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
            }
        }
        catch { }
    }

    [STAThread]
    static void Main()
    {
        bool createdNew;
        using (var mutex = new Mutex(true, @"Local\AutoLidAction.SingleInstance", out createdNew))
        {
            if (!createdNew) return;   // already running in this session

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e) { Log("Unhandled: " + e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate (object s, UnhandledExceptionEventArgs e)
            {
                Log("FATAL: " + e.ExceptionObject);
                if (RestoreSleepActionOnExit) SetLidPolicy(LID_SLEEP, LID_SLEEP, true);
            };

            instance = new AutoLidApp();
            Application.ApplicationExit += delegate { instance.Shutdown("application exit"); };
            Application.Run();
            GC.KeepAlive(mutex);
        }
    }
}
