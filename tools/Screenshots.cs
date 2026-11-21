// 紹介ページ・記事用のスクリーンショットを撮るツール（tools\make-screenshots.ps1 から src\*.cs と一緒にコンパイル）
// サンプルのスケジュールと架空のモニター名で画面を開いて撮影する。モニターの明るさは変えない。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using BrightnessScheduler;

static class Screenshots
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    static string outDir;

    static List<ScheduleEntry> Sample()
    {
        return new List<ScheduleEntry>
        {
            new ScheduleEntry { Time = "07:00", Brightness = 80 },
            new ScheduleEntry { Time = "12:00", Brightness = 100, Fade = true },
            new ScheduleEntry { Time = "22:00", Brightness = 30, Fade = true },
            new ScheduleEntry { Time = "23:30", Brightness = 15 },
        };
    }

    static DetectResult Monitors()
    {
        var r = new DetectResult();
        r.Names.AddRange(new[] { "メインモニター（HDR）", "サブモニター" });
        r.Monitors.Add(new DetectedMonitor { Id = "main", Name = "メインモニター", Hdr = true });
        r.Monitors.Add(new DetectedMonitor { Id = "sub", Name = "サブモニター" });
        return r;
    }

    /// ウィンドウを PNG に保存（Windows 11 の角丸の外側の黒い部分も含めてそのまま撮る）
    static void Capture(Form f, string name)
    {
        RECT r; GetWindowRect(f.Handle, out r);
        using (var bmp = new Bitmap(r.Right - r.Left, r.Bottom - r.Top, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dc = g.GetHdc();
                PrintWindow(f.Handle, dc, 2);  // PW_RENDERFULLCONTENT
                g.ReleaseHdc(dc);
            }
            bmp.Save(System.IO.Path.Combine(outDir, name), ImageFormat.Png);
        }
        Console.WriteLine("撮影しました: " + name + " (" + (r.Right - r.Left) + "x" + (r.Bottom - r.Top) + ")");
    }

    /// 表示して描画が落ち着いてから撮り、閉じる
    static void Shoot(Form f, string name, Action afterShown)
    {
        f.StartPosition = FormStartPosition.CenterScreen;
        f.ShowInTaskbar = true;
        var t = new Timer { Interval = 1500 };
        t.Tick += delegate { t.Stop(); Capture(f, name); f.Close(); };
        f.Shown += delegate
        {
            Cursor.Position = new Point(0, 0);  // マウスが重なってハイライトされないよう、画面の隅へ
            if (afterShown != null) afterShown();
            f.Refresh();
            t.Start();
        };
        Application.Run(f);
    }

    [STAThread]
    static void Main(string[] args)
    {
        outDir = args[0];
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Theme.Init();
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;

        // 1. メイン画面
        var settings = new Settings { Entries = Sample(), Corrections = new List<MonitorCorrection>() };
        int now = Scheduler.TargetPercent(settings.Entries, DateTime.Now);
        var main = new MainForm(settings, Theme.LoadAppIcon(Size.Empty),
            () => DateTime.Now.ToString("H:mm") + " に 2 台の画面を " + now + "% にしました", v => { });
        main.SetMonitors(Monitors());
        Shoot(main, "main.png", null);

        // 2. 明るさの編集（夜にかけてなめらかに暗くする）
        Shoot(new EntryDialog(Sample()[2], false, v => { }), "edit.png", null);

        // 3. モニターごとの補正（「範囲で指定」のガイドの手順 1）
        var sub = MonitorCorrection.Default("sub", "サブモニター"); sub.Min = 12; sub.Max = 78;
        var main2 = MonitorCorrection.Default("main", "メインモニター"); main2.Mode = MonitorCorrection.Offset; main2.Shift = -10;
        var dlg = new CorrectionDialog(Monitors().Monitors, new List<MonitorCorrection> { main2, sub }, 50, (p, c) => { });
        Shoot(dlg, "correction.png", delegate
        {
            var cards = (System.Collections.IList)typeof(CorrectionDialog).GetField("cards", flags).GetValue(dlg);
            typeof(CorrectionDialog).GetMethod("StartGuide", flags).Invoke(dlg, new[] { cards[1] });
        });
    }
}
