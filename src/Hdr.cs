// HDR がオンの画面の明るさ
// HDR 中は多くのモニターで本体の明るさ（DDC/CI）が無視され、Windows の
// 「設定 > ディスプレイ > HDR > SDR コンテンツの明るさ」で明るさが決まる。
// その値を DisplayConfig API で読み書きする（Windows の設定画面のスライダーと同じ値）
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BrightnessScheduler
{
    class HdrTarget
    {
        public string Source;   // \\.\DISPLAY2 など（MONITORINFOEX.Device と同じ形式）
        public string Name;     // モニターの名前
        public string Id;       // 補正用の ID（\\?\DISPLAY#...#{...} を小文字にしたもの）
        internal Hdr.LUID Adapter;
        internal uint TargetId;
    }

    static class Hdr
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct LUID { public uint Low; public int High; }

        [StructLayout(LayoutKind.Sequential)]
        struct PATH_SOURCE { public LUID adapterId; public uint id, modeInfoIdx, statusFlags; }

        [StructLayout(LayoutKind.Sequential)]
        struct PATH_TARGET
        {
            public LUID adapterId;
            public uint id, modeInfoIdx, outputTechnology, rotation, scaling, refreshNum, refreshDen, scanLineOrdering;
            public int targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PATH_INFO { public PATH_SOURCE source; public PATH_TARGET target; public uint flags; }

        [StructLayout(LayoutKind.Sequential)]
        struct MODE_INFO
        {
            public uint infoType, id;
            public LUID adapterId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] data;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct HEADER { public int type, size; public LUID adapterId; public uint id; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SOURCE_NAME { public HEADER h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string name; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TARGET_NAME
        {
            public HEADER h;
            public uint flags, outputTechnology;
            public ushort edidManufactureId, edidProductCodeId;
            public uint connectorInstance;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string friendlyName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string devicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct ADVANCED_COLOR { public HEADER h; public uint value, colorEncoding, bitsPerColorChannel; }

        [StructLayout(LayoutKind.Sequential)]
        struct SDR_WHITE { public HEADER h; public uint level; }

        [StructLayout(LayoutKind.Sequential)]
        struct SET_SDR_WHITE { public HEADER h; public uint level; public byte finalValue; }

        const uint QDC_ONLY_ACTIVE_PATHS = 2;
        const int GET_SOURCE_NAME = 1, GET_TARGET_NAME = 2, GET_ADVANCED_COLOR_INFO = 9, GET_SDR_WHITE_LEVEL = 11;
        const int SET_SDR_WHITE_LEVEL = -18;  // 0xFFFFFFEE（非公開。Windows の設定画面が使っているもの）

        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint np, [Out] PATH_INFO[] p, ref uint nm, [Out] MODE_INFO[] m, IntPtr topology);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SOURCE_NAME r);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref TARGET_NAME r);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref ADVANCED_COLOR r);
        [DllImport("user32.dll")] static extern int DisplayConfigGetDeviceInfo(ref SDR_WHITE r);
        [DllImport("user32.dll")] static extern int DisplayConfigSetDeviceInfo(ref SET_SDR_WHITE r);

        static HEADER Header<T>(int type, LUID adapter, uint id)
        {
            return new HEADER { type = type, size = Marshal.SizeOf(typeof(T)), adapterId = adapter, id = id };
        }

        /// HDR がオンになっている画面の一覧
        public static List<HdrTarget> Query()
        {
            var list = new List<HdrTarget>();
            try
            {
                uint np, nm;
                if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0) return list;
                var paths = new PATH_INFO[np];
                var modes = new MODE_INFO[nm];
                if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return list;
                for (int i = 0; i < np; i++)
                {
                    var t = paths[i].target;
                    var color = new ADVANCED_COLOR { h = Header<ADVANCED_COLOR>(GET_ADVANCED_COLOR_INFO, t.adapterId, t.id) };
                    if (DisplayConfigGetDeviceInfo(ref color) != 0) continue;
                    bool enabled = (color.value & 2) != 0;  // advancedColorEnabled
                    if (!enabled) continue;

                    var src = new SOURCE_NAME { h = Header<SOURCE_NAME>(GET_SOURCE_NAME, paths[i].source.adapterId, paths[i].source.id) };
                    var tn = new TARGET_NAME { h = Header<TARGET_NAME>(GET_TARGET_NAME, t.adapterId, t.id) };
                    bool hasName = DisplayConfigGetDeviceInfo(ref tn) == 0;
                    string name = hasName && !string.IsNullOrEmpty(tn.friendlyName) ? tn.friendlyName : "モニター";
                    string source = DisplayConfigGetDeviceInfo(ref src) == 0 ? src.name : "";
                    list.Add(new HdrTarget
                    {
                        Source = source, Name = name, Adapter = t.adapterId, TargetId = t.id,
                        Id = (hasName && !string.IsNullOrEmpty(tn.devicePath) ? tn.devicePath : source).ToLowerInvariant()
                    });
                }
            }
            catch { }
            return list;
        }

        /// HDR のオン／オフの組み合わせ（変わったら明るさを設定し直すため）
        public static string Signature()
        {
            var parts = new List<string>();
            foreach (var t in Query()) parts.Add(t.Source);
            return string.Join("|", parts.ToArray());
        }

        // Windows の設定画面のスライダー 0〜100 は 80〜480 nits に対応する（値は 1000 = 80 nits の単位）
        const double MinNits = 80, MaxNits = 480;

        static uint LevelFor(int percent)
        {
            double nits = MinNits + (MaxNits - MinNits) * percent / 100.0;
            return (uint)Math.Round(nits * 1000 / 80);
        }

        /// SDR コンテンツの明るさを percent（0〜100、Windows の設定のスライダーと同じ）にする
        public static bool SetSdrBrightness(HdrTarget t, int percent)
        {
            try
            {
                uint level = LevelFor(Math.Max(0, Math.Min(100, percent)));
                var cur = new SDR_WHITE { h = Header<SDR_WHITE>(GET_SDR_WHITE_LEVEL, t.Adapter, t.TargetId) };
                if (DisplayConfigGetDeviceInfo(ref cur) == 0 && cur.level == level) return true;
                var req = new SET_SDR_WHITE { h = Header<SET_SDR_WHITE>(SET_SDR_WHITE_LEVEL, t.Adapter, t.TargetId), level = level, finalValue = 1 };
                return DisplayConfigSetDeviceInfo(ref req) == 0;
            }
            catch { return false; }
        }
    }
}
