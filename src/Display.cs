// ディスプレイの明るさの読み書き
//  ・外付けモニター: DDC/CI（dxva2.dll の VCP コード 0x10 = 輝度）
//  ・ノート PC の内蔵画面: WMI（WmiMonitorBrightnessMethods）
//  ・HDR がオンの画面: Windows の「SDR コンテンツの明るさ」（Hdr.cs）
// DDC/CI は 1 回の通信に数十ミリ秒かかるため、専用のスレッドで処理する
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace BrightnessScheduler
{
    public class ApplyResult
    {
        public int Requested;     // 設定しようとした明るさ（%）
        public int Succeeded;     // 設定できた画面の数
        public int Failed;        // 設定できなかった画面の数
        public int HdrFailed;     // そのうち HDR の画面（SDR コンテンツの明るさを変えられなかった）
        public List<string> Names = new List<string>();
    }

    public class DetectResult
    {
        public List<string> Names = new List<string>();     // 明るさを変える画面
        public List<string> Skipped = new List<string>();   // HDR がオンで対象外にしている画面
        public List<DetectedMonitor> Monitors = new List<DetectedMonitor>();  // 補正の画面に出すもの（Names と同じ順）
    }

    public class DetectedMonitor
    {
        public string Id, Name;
        public bool Hdr;
    }

    static class Display
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PHYSICAL_MONITOR
        {
            public IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        }

        delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("dxva2.dll")]
        static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);
        [DllImport("dxva2.dll", CharSet = CharSet.Unicode)]
        static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);
        [DllImport("dxva2.dll")]
        static extern bool DestroyPhysicalMonitors(uint count, PHYSICAL_MONITOR[] monitors);
        [DllImport("dxva2.dll")]
        static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr hMonitor, byte code, IntPtr type, out uint current, out uint max);
        [DllImport("dxva2.dll")]
        static extern bool SetVCPFeature(IntPtr hMonitor, byte code, uint value);

        const byte VcpBrightness = 0x10;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct MONITORINFOEX
        {
            public int Size;
            public RECT Monitor, Work;
            public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string device, uint index, ref DISPLAY_DEVICE dd, uint flags);
        const uint EDD_GET_DEVICE_INTERFACE_NAME = 1;

        /// 接続中の外付けモニターに対して処理する（終わったらハンドルを解放）
        /// action の引数: モニター、表示用の名前、\\.\DISPLAY1 などのデバイス名、補正用の ID
        static void ForEachPhysical(Action<PHYSICAL_MONITOR, string, string, string> action)
        {
            var handles = new List<IntPtr>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr h, IntPtr dc, IntPtr r, IntPtr d) { handles.Add(h); return true; }, IntPtr.Zero);
            Dictionary<string, string> edidNames = null;
            foreach (var h in handles)
            {
                uint n;
                if (!GetNumberOfPhysicalMonitorsFromHMONITOR(h, out n) || n == 0) continue;
                var arr = new PHYSICAL_MONITOR[n];
                if (!GetPhysicalMonitorsFromHMONITOR(h, n, arr)) continue;
                var mi = new MONITORINFOEX { Size = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                string device = GetMonitorInfo(h, ref mi) ? mi.Device : "";
                string baseId = InterfaceId(device);
                try
                {
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var m = arr[i];
                        // 複製表示などで 1 つの画面に複数台あるときは、順番で見分ける
                        string id = arr.Length == 1 ? baseId : baseId + "#" + i;
                        string name = CleanName(m.Description);
                        if (name == null)
                        {
                            // 「汎用 PnP モニター」としか名乗らないモニターは、EDID に書かれたモデル名を使う
                            if (edidNames == null) edidNames = ReadEdidNames();
                            name = n == 1 ? EdidNameFor(h, edidNames) : null;
                            if (name == null) name = "モニター";
                        }
                        action(m, name, device, id);
                    }
                }
                finally { DestroyPhysicalMonitors(n, arr); }
            }
        }

        /// モニターを見分ける ID（\\?\DISPLAY#IVM6633#5&xxx&UID4353#{...} を小文字にしたもの）。
        /// HDR の画面の DISPLAYCONFIG_TARGET_DEVICE_NAME.monitorDevicePath と同じ形式
        static string InterfaceId(string device)
        {
            try
            {
                var dd = new DISPLAY_DEVICE { Size = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                if (!string.IsNullOrEmpty(device) && EnumDisplayDevices(device, 0, ref dd, EDD_GET_DEVICE_INTERFACE_NAME) && !string.IsNullOrEmpty(dd.DeviceID))
                    return dd.DeviceID.ToLowerInvariant();
            }
            catch { }
            return (device ?? "").ToLowerInvariant();
        }

        /// 説明文から「(HDMI1 HDR)」などを除いた名前。汎用の名前なら null
        static string CleanName(string description)
        {
            string s = (description ?? "").Trim();
            if (s.Length == 0 || s.StartsWith("Generic PnP", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("汎用 PnP", StringComparison.OrdinalIgnoreCase)) return null;
            int p = s.IndexOf('(');
            return p > 0 ? s.Substring(0, p).Trim() : s;
        }

        /// WMI の WmiMonitorID から、インスタンス名 → モデル名 の対応表を作る
        static Dictionary<string, string> ReadEdidNames()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    new System.Management.ManagementScope(@"root\WMI"), new System.Management.SelectQuery("WmiMonitorID")))
                    foreach (System.Management.ManagementObject o in searcher.Get())
                        using (o)
                        {
                            var raw = o["UserFriendlyName"] as ushort[];
                            string inst = o["InstanceName"] as string;
                            if (raw == null || inst == null) continue;
                            var sb = new System.Text.StringBuilder();
                            foreach (var c in raw) if (c != 0) sb.Append((char)c);
                            if (sb.Length > 0) map[inst] = sb.ToString().Trim();
                        }
            }
            catch { }
            return map;
        }

        /// hMonitor → \\?\DISPLAY#IVM6633#5&xxx&UID4353#{...} → DISPLAY\IVM6633\5&xxx&UID4353 で WMI の名前を探す
        static string EdidNameFor(IntPtr hMonitor, Dictionary<string, string> map)
        {
            try
            {
                var mi = new MONITORINFOEX { Size = Marshal.SizeOf(typeof(MONITORINFOEX)) };
                if (!GetMonitorInfo(hMonitor, ref mi)) return null;
                var dd = new DISPLAY_DEVICE { Size = Marshal.SizeOf(typeof(DISPLAY_DEVICE)) };
                if (!EnumDisplayDevices(mi.Device, 0, ref dd, EDD_GET_DEVICE_INTERFACE_NAME)) return null;
                string id = dd.DeviceID ?? "";
                if (id.StartsWith(@"\\?\")) id = id.Substring(4);
                int brace = id.IndexOf("#{");
                if (brace > 0) id = id.Substring(0, brace);
                id = id.Replace('#', '\\');
                foreach (var kv in map)
                    if (kv.Key.StartsWith(id, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            }
            catch { }
            return null;
        }

        /// DDC/CI の通信は失敗することがあるので、少し待って数回試す
        static bool Retry(Func<bool> f)
        {
            for (int i = 0; i < 3; i++)
            {
                if (f()) return true;
                Thread.Sleep(60);
            }
            return false;
        }

        public const string InternalId = "internal";

        /// 補正をかけた、そのモニターに設定する値
        static int Corrected(IList<MonitorCorrection> corrections, string id, int percent)
        {
            if (corrections != null)
                foreach (var c in corrections)
                    if (c != null && string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)) return c.Apply(percent);
            return percent;
        }

        /// すべての画面の明るさを percent（0〜100）にする
        /// skipHdr: HDR がオンの画面には何もしない
        /// corrections: モニターごとの補正（null なら補正なし）
        public static ApplyResult SetAll(int percent, bool skipHdr, IList<MonitorCorrection> corrections)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            var res = new ApplyResult { Requested = percent };

            // HDR がオンの画面は、モニター本体の明るさが効かないので「SDR コンテンツの明るさ」を変える
            // （対象外のときも、効かない DDC/CI は送らない）
            var hdr = Hdr.Query();
            var hdrSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in hdr)
            {
                hdrSources.Add(t.Source);
                if (skipHdr) continue;
                if (Hdr.SetSdrBrightness(t, Corrected(corrections, t.Id, percent))) { res.Succeeded++; res.Names.Add(t.Name + "（HDR）"); }
                else { res.Failed++; res.HdrFailed++; }
            }

            ForEachPhysical(delegate(PHYSICAL_MONITOR m, string name, string device, string id)
            {
                if (hdrSources.Contains(device)) return;
                uint cur = 0, max = 0;
                bool read = Retry(delegate { return GetVCPFeatureAndVCPFeatureReply(m.Handle, VcpBrightness, IntPtr.Zero, out cur, out max); });
                if (!read || max == 0) { res.Failed++; return; }  // DDC/CI 非対応（内蔵画面など）
                uint value = (uint)Math.Round(max * Corrected(corrections, id, percent) / 100.0);
                bool ok = cur == value || Retry(delegate { return SetVCPFeature(m.Handle, VcpBrightness, value); });
                if (ok) { res.Succeeded++; res.Names.Add(name); }
                else res.Failed++;
            });

            // ノート PC の内蔵画面（WMI 経由）
            int wmi = WmiSet(Corrected(corrections, InternalId, percent));
            if (wmi > 0)
            {
                res.Succeeded += wmi;
                res.Names.Add("内蔵ディスプレイ");
                // 内蔵画面は DDC/CI で失敗として数えられているので、その分を差し引く
                res.Failed = Math.Max(0, res.Failed - wmi);
            }
            return res;
        }

        /// 明るさを変えられる画面の名前（一覧表示用）
        public static DetectResult Detect(bool skipHdr)
        {
            var res = new DetectResult();
            var hdrSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in Hdr.Query())
            {
                hdrSources.Add(t.Source);
                if (skipHdr) res.Skipped.Add(t.Name);
                else
                {
                    res.Names.Add(t.Name + "（HDR）");
                    res.Monitors.Add(new DetectedMonitor { Id = t.Id, Name = t.Name, Hdr = true });
                }
            }
            ForEachPhysical(delegate(PHYSICAL_MONITOR m, string name, string device, string id)
            {
                if (hdrSources.Contains(device)) return;
                uint cur, max;
                if (Retry(delegate { return GetVCPFeatureAndVCPFeatureReply(m.Handle, VcpBrightness, IntPtr.Zero, out cur, out max); }))
                {
                    res.Names.Add(name);
                    res.Monitors.Add(new DetectedMonitor { Id = id, Name = name });
                }
            });
            if (WmiAvailable())
            {
                res.Names.Add("内蔵ディスプレイ");
                res.Monitors.Add(new DetectedMonitor { Id = InternalId, Name = "内蔵ディスプレイ" });
            }
            return res;
        }

        // ---- WMI（ノート PC の内蔵画面。デスクトップ PC では見つからないので何もしない） ----
        static int WmiSet(int percent)
        {
            try
            {
                var scope = new System.Management.ManagementScope(@"root\WMI");
                using (var searcher = new System.Management.ManagementObjectSearcher(scope,
                    new System.Management.SelectQuery("WmiMonitorBrightnessMethods")))
                {
                    int count = 0;
                    foreach (System.Management.ManagementObject o in searcher.Get())
                        using (o)
                        {
                            o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                            count++;
                        }
                    return count;
                }
            }
            catch { return 0; }
        }

        static bool WmiAvailable()
        {
            try
            {
                var scope = new System.Management.ManagementScope(@"root\WMI");
                using (var searcher = new System.Management.ManagementObjectSearcher(scope,
                    new System.Management.SelectQuery("WmiMonitorBrightness")))
                    foreach (System.Management.ManagementObject o in searcher.Get())
                        using (o) return true;
            }
            catch { }
            return false;
        }
    }

    /// 明るさの変更を専用スレッドで順に処理する。
    /// 処理中に新しい値が来たら、古い値は飛ばして最新の値だけを設定する
    class BrightnessWorker : IDisposable
    {
        readonly Thread thread;
        readonly AutoResetEvent signal = new AutoResetEvent(false);
        readonly object gate = new object();
        int pending = -1;
        bool detectPending, stopping;

        /// 設定が終わったとき（ワーカースレッドから呼ばれる）
        public event Action<ApplyResult> Applied;
        /// 画面の検出が終わったとき（ワーカースレッドから呼ばれる）
        public event Action<DetectResult> Detected;
        /// true: HDR がオンの画面は対象外（UI スレッドから設定し、ワーカーが読む）
        public volatile bool SkipHdr;
        /// モニターごとの補正。UI スレッドでは書き換えず、差し替える（中身を変えるときは複製を渡す）
        public volatile List<MonitorCorrection> Corrections;

        public BrightnessWorker()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "BrightnessWorker" };
            thread.Start();
        }

        public void Request(int percent)
        {
            lock (gate) pending = percent;
            signal.Set();
        }

        public void RequestDetect()
        {
            lock (gate) detectPending = true;
            signal.Set();
        }

        void Run()
        {
            while (true)
            {
                signal.WaitOne();
                while (true)
                {
                    int value; bool detect;
                    lock (gate)
                    {
                        if (stopping) return;
                        value = pending; pending = -1;
                        detect = detectPending; detectPending = false;
                    }
                    if (value < 0 && !detect) break;
                    if (detect)
                    {
                        DetectResult found;
                        try { found = Display.Detect(SkipHdr); } catch (Exception ex) { Debug.WriteLine(ex); found = new DetectResult(); }
                        var d = Detected; if (d != null) d(found);
                    }
                    if (value >= 0)
                    {
                        ApplyResult r;
                        try { r = Display.SetAll(value, SkipHdr, Corrections); }
                        catch (Exception ex) { Debug.WriteLine(ex); r = new ApplyResult { Requested = value, Failed = 1 }; }
                        var a = Applied; if (a != null) a(r);
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (gate) stopping = true;
            signal.Set();
            thread.Join(2000);
        }
    }
}
