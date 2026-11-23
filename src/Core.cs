// データ・スケジュール計算・自動起動（UI に依存しない部分）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using Microsoft.Win32;

// バージョンはここで一元管理（exe のプロパティ・配布 ZIP の名前に使われます）
[assembly: System.Reflection.AssemblyTitle("輝度スケジューラ")]
[assembly: System.Reflection.AssemblyDescription("指定した時刻にディスプレイの明るさを変えるアプリ")]
[assembly: System.Reflection.AssemblyProduct("輝度スケジューラ")]
[assembly: System.Reflection.AssemblyVersion("1.1.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.1.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.1.1")]

namespace BrightnessScheduler
{
    /// アプリ情報と外部リンク
    static class AppInfo
    {
        public const string Name = "輝度スケジューラ";

        // ▼ 支援ページと公式ページの URL（https:// で始まる URL を入れるとボタンが表示されます。空欄なら非表示）
        public const string SupportUrl = "https://ofuse.me/c0fdccc4";
        public const string WebsiteUrl = "https://github.com/woy74237-ship-it/brightness-scheduler";

        public static bool HasSupport { get { return IsWebUrl(SupportUrl); } }
        public static bool HasWebsite { get { return IsWebUrl(WebsiteUrl); } }
        // 応援先（OFUSE / Ko-fi）。OFUSE はファンレターを書く形なので、気軽に応援したい人向けに Ko-fi も選べるようにする
        public const string KofiUrl = "https://ko-fi.com/waltowen404923";
        const string OfuseItem = "OFUSE で応援する（メッセージを添えて・円）";
        const string KofiItem = "Ko-fi で応援する（ワンクリック・ドル）";

        /// タスクトレイ用: 「開発を支援する」のサブメニュー（OFUSE / Ko-fi）
        public static ToolStripMenuItem SupportMenuItem()
        {
            var item = Theme.MenuItem("開発を支援する", null);
            var drop = new ContextMenuStrip();
            Theme.StyleMenu(drop);
            item.DropDown = drop;
            item.DropDownItems.Add(Theme.MenuItem(OfuseItem, delegate { Open(SupportUrl); }));
            item.DropDownItems.Add(Theme.MenuItem(KofiItem, delegate { Open(KofiUrl); }));
            return item;
        }

        /// ボタン用: ボタンの下に OFUSE / Ko-fi を選ぶメニューを出す
        public static void ShowSupportMenu(Control anchor)
        {
            var menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);
            menu.Items.Add(Theme.MenuItem(OfuseItem, delegate { Open(SupportUrl); }));
            menu.Items.Add(Theme.MenuItem(KofiItem, delegate { Open(KofiUrl); }));
            menu.Closed += delegate { anchor.BeginInvoke((MethodInvoker)menu.Dispose); };
            menu.Show(anchor, new System.Drawing.Point(0, anchor.Height));
        }

        public static string Version
        {
            get
            {
                var a = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                    typeof(AppInfo).Assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
                return a != null ? a.InformationalVersion : "";
            }
        }

        static bool IsWebUrl(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps;
        }

        public static void Open(string url)
        {
            if (!IsWebUrl(url)) return;
            try { System.Diagnostics.Process.Start(url); } catch { }
        }
    }

    [DataContract]
    public class ScheduleEntry
    {
        [DataMember(Order = 1)] public string Time { get; set; }       // "HH:mm"
        [DataMember(Order = 2)] public int Brightness { get; set; }    // 0〜100 (%)
        /// true: 前の時刻から少しずつ変化して、この時刻にちょうどこの明るさになる
        /// false: この時刻になったらパッと切り替える
        [DataMember(Order = 3)] public bool Fade { get; set; }

        public bool IsValid
        {
            get
            {
                TimeSpan ts;
                return Brightness >= 0 && Brightness <= 100 &&
                       TimeSpan.TryParseExact(Time ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out ts);
            }
        }

        public TimeSpan TimeOfDay
        {
            get { return TimeSpan.ParseExact(Time, @"hh\:mm", CultureInfo.InvariantCulture); }
        }

        public string ModeLabel { get { return Fade ? "なめらかに変化" : "その時刻に切り替え"; } }

        public static string Format(TimeSpan t)
        {
            return string.Format("{0:00}:{1:00}", t.Hours, t.Minutes);
        }
    }

    /// モニター 1 台分の補正。スケジュールの明るさ（%）を、そのモニターに設定する値に変える
    [DataContract]
    public class MonitorCorrection
    {
        public const string Range = "range", Offset = "offset", Scale = "scale";

        [DataMember(Order = 1)] public string Id { get; set; }      // モニターを見分ける ID（接続端子ごと）
        [DataMember(Order = 2)] public string Name { get; set; }    // 表示用の名前
        [DataMember(Order = 3)] public string Mode { get; set; }    // range / offset / scale
        [DataMember(Order = 4)] public int Min { get; set; }        // 範囲: 0% のときの値
        [DataMember(Order = 5)] public int Max { get; set; }        // 範囲: 100% のときの値
        [DataMember(Order = 6)] public int Shift { get; set; }      // ずらす量（-50〜+50）
        [DataMember(Order = 7)] public int Factor { get; set; }     // 倍率（%、0〜200）

        /// 補正なしと同じ結果になる初期値
        public static MonitorCorrection Default(string id, string name)
        {
            return new MonitorCorrection { Id = id, Name = name, Mode = Range, Min = 0, Max = 100, Shift = 0, Factor = 100 };
        }

        public MonitorCorrection Clone() { return (MonitorCorrection)MemberwiseClone(); }

        /// スケジュールの明るさ percent を、このモニターに設定する値（0〜100）にする
        public int Apply(int percent)
        {
            double v;
            switch (Mode)
            {
                case Offset: v = percent + Shift; break;
                case Scale: v = percent * Factor / 100.0; break;
                default: v = Min + (Max - Min) * percent / 100.0; break;
            }
            return Math.Max(0, Math.Min(100, (int)Math.Round(v)));
        }

        /// 補正なしと同じか
        public bool IsIdentity
        {
            get
            {
                switch (Mode)
                {
                    case Offset: return Shift == 0;
                    case Scale: return Factor == 100;
                    default: return Min == 0 && Max == 100;
                }
            }
        }
    }

    [DataContract]
    public class Settings
    {
        [DataMember(Order = 1)] public List<ScheduleEntry> Entries { get; set; }
        [DataMember(Order = 2)] public bool Paused { get; set; }
        /// true: HDR がオンのモニターには何もしない（Windows の「SDR コンテンツの明るさ」を書き換えない）
        /// 古い設定ファイルに無い項目は false になるので、「対象外にする」の向きで持つ
        [DataMember(Order = 3)] public bool SkipHdr { get; set; }
        /// モニターごとの補正（つながっていないモニターの補正も残しておく）
        [DataMember(Order = 4)] public List<MonitorCorrection> Corrections { get; set; }
        /// 一時的に暗くするときの割合（%）。今の明るさにこの割合を掛ける（75 / 50 / 25）
        [DataMember(Order = 5)] public int DimFactor { get; set; }

        public static readonly int[] DimFactors = { 75, 50, 25 };
        public static readonly string[] DimFactorLabels = { "少し暗く（3/4）", "半分に", "かなり暗く（1/4）" };

        public static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BrightnessScheduler");

        public static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static Settings Load()
        {
            Settings s = null;
            if (File.Exists(FilePath))
            {
                try
                {
                    using (var fs = File.OpenRead(FilePath))
                        s = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(fs);
                }
                catch
                {
                    // 壊れた設定ファイルは退避してから初期化する
                    try { File.Copy(FilePath, FilePath + ".bak", true); } catch { }
                    s = null;
                }
            }
            if (s == null) s = new Settings();
            if (s.Entries == null) s.Entries = new List<ScheduleEntry>();
            s.Entries.RemoveAll(e => e == null || !e.IsValid);
            if (s.Corrections == null) s.Corrections = new List<MonitorCorrection>();
            s.Corrections.RemoveAll(c => c == null || string.IsNullOrEmpty(c.Id));
            if (Array.IndexOf(DimFactors, s.DimFactor) < 0) s.DimFactor = 50;  // 古い設定ファイルには無いので既定の「半分」に
            return s;
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            string tmp = FilePath + ".tmp";
            using (var fs = File.Create(tmp))
                new DataContractJsonSerializer(typeof(Settings)).WriteObject(fs, this);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
    }

    public static class Scheduler
    {
        public static List<ScheduleEntry> Sorted(IEnumerable<ScheduleEntry> entries)
        {
            return entries.Where(e => e != null && e.IsValid).OrderBy(e => e.TimeOfDay).ToList();
        }

        /// 直前の項目（その時刻以前で最も遅いもの。なければ前日の最後の項目）
        public static ScheduleEntry GetActive(IEnumerable<ScheduleEntry> entries, DateTime now)
        {
            return GetActive(Sorted(entries), now.TimeOfDay);
        }

        static ScheduleEntry GetActive(List<ScheduleEntry> list, TimeSpan t)
        {
            if (list.Count == 0) return null;
            ScheduleEntry active = list[list.Count - 1];
            foreach (var e in list)
                if (e.TimeOfDay <= t) active = e;
            return active;
        }

        /// 次の項目
        public static ScheduleEntry GetNext(IEnumerable<ScheduleEntry> entries, DateTime now)
        {
            return GetNext(Sorted(entries), now.TimeOfDay);
        }

        static ScheduleEntry GetNext(List<ScheduleEntry> list, TimeSpan t)
        {
            if (list.Count == 0) return null;
            foreach (var e in list)
                if (e.TimeOfDay > t) return e;
            return list[0];
        }

        /// その時刻の明るさ（%）。スケジュールが空なら -1
        /// 次の項目が「なめらか」なら、直前の項目の時刻から次の項目の時刻までを S 字の曲線でつなぐ
        public static double TargetAt(IEnumerable<ScheduleEntry> entries, TimeSpan t)
        {
            var list = Sorted(entries);
            if (list.Count == 0) return -1;
            if (list.Count == 1) return list[0].Brightness;
            var prev = GetActive(list, t);
            var next = GetNext(list, t);
            if (!next.Fade) return prev.Brightness;

            double span = (next.TimeOfDay - prev.TimeOfDay).TotalMinutes;
            if (span <= 0) span += 1440;
            double elapsed = (t - prev.TimeOfDay).TotalMinutes;
            if (elapsed < 0) elapsed += 1440;
            double f = Math.Max(0, Math.Min(1, elapsed / span));
            // ゆっくり始まって、ゆっくり終わる曲線（smoothstep）。始まりと終わりで急に変わり始めない
            f = f * f * (3 - 2 * f);
            return prev.Brightness + (next.Brightness - prev.Brightness) * f;
        }

        /// 実際に設定する明るさ（整数 %）。スケジュールが空なら -1
        public static int TargetPercent(IEnumerable<ScheduleEntry> entries, DateTime now)
        {
            double v = TargetAt(entries, now.TimeOfDay);
            return v < 0 ? -1 : (int)Math.Round(v);
        }

        /// 一時的に暗くしたときの明るさ。スケジュールが空（target が -1）なら 100% を基準にする
        public static int Dimmed(int target, int factor)
        {
            int baseValue = target < 0 ? 100 : target;
            return Math.Max(0, Math.Min(100, (int)Math.Round(baseValue * factor / 100.0)));
        }

        /// 「3時間12分」のような残り時間表記
        public static string Remaining(TimeSpan target, DateTime now)
        {
            var d = target - now.TimeOfDay;
            if (d <= TimeSpan.Zero) d += TimeSpan.FromDays(1);
            int total = (int)Math.Ceiling(d.TotalMinutes);
            int h = total / 60, m = total % 60;
            return h > 0 ? h + "時間" + m + "分" : m + "分";
        }
    }

    static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "BrightnessScheduler";

        public static bool IsEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(ValueName) != null;
        }

        /// 自動起動で起動されたことを示す引数（このときは設定画面を開かずにトレイに常駐する）
        public const string AutostartArg = "--autostart";

        public static bool IsAutostartLaunch(string[] args)
        {
            return args != null && args.Any(a => string.Equals(a, AutostartArg, StringComparison.OrdinalIgnoreCase));
        }

        /// clearWindowsBlock: 使う人がアプリの画面で自分からオンにしたときは true。
        /// Windows の「スタートアップ アプリ」に記録された「オフ」も消して、確実に有効にする
        public static void SetEnabled(bool enabled, bool clearWindowsBlock = false)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\" " + AutostartArg);
                else k.DeleteValue(ValueName, false);
            }
            if (enabled && clearWindowsBlock) ClearWindowsBlock();
        }

        // Windows の設定（スタートアップ アプリ）やタスク マネージャーでのオン／オフの記録
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        /// 自動起動は登録されているが、Windows の設定でオフにされているか（先頭バイトの最下位ビットが 1 ならオフ）
        public static bool IsBlockedByWindows()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                {
                    var b = k == null ? null : k.GetValue(ValueName) as byte[];
                    return b != null && b.Length > 0 && (b[0] & 1) == 1;
                }
            }
            catch { return false; }
        }

        public static void ClearWindowsBlock()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                    if (k != null) k.DeleteValue(ValueName, false);
            }
            catch { }
        }

        public static void OpenWindowsStartupSettings()
        {
            try { System.Diagnostics.Process.Start("ms-settings:startupapps"); } catch { }
        }

        static string RegisteredValue()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                var v = k == null ? null : k.GetValue(ValueName) as string;
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
        }

        static string RegisteredPath()
        {
            string v = RegisteredValue();
            if (v == null) return null;
            if (v.StartsWith("\""))
            {
                int end = v.IndexOf('"', 1);
                return end > 1 ? v.Substring(1, end - 1) : v.Trim('"');
            }
            return v;
        }

        /// 登録先の exe が移動・削除されて存在しなければ、今動いている exe に登録し直す
        public static bool RepairIfBroken()
        {
            string path = RegisteredPath();
            if (path == null) return false;
            bool exists = false;
            try { exists = File.Exists(path); } catch { }
            if (!exists) { SetEnabled(true); return true; }
            return false;
        }
    }
}
