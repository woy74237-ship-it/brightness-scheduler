// タスクトレイ常駐部とエントリポイント
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BrightnessScheduler
{
    class TrayApp : ApplicationContext
    {
        readonly Settings settings;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer, delayed;
        readonly ToolStripMenuItem autoItem;
        readonly Control invoker;
        readonly Icon appIcon, trayIcon;
        readonly BrightnessWorker worker;
        MainForm form;
        int lastRequested = -1;
        string status = "";
        bool lastFailed, lastHdrFailed;
        DetectResult monitors;
        string hdrSignature;
        bool skipHdr;
        /// 一時的に暗くしている間は、終わる時刻（「元に戻すまで」のときは DateTime.MaxValue）。暗くしていなければ null
        DateTime? dimUntil;
        readonly ToolStripMenuItem dimItem;

        public TrayApp(bool launchedAtStartup)
        {
            settings = Settings.Load();
            try { Startup.RepairIfBroken(); } catch { }
            appIcon = Theme.LoadAppIcon(Size.Empty);
            trayIcon = Theme.LoadAppIcon(SystemInformation.SmallIconSize);

            invoker = new Control();
            var forceHandle = invoker.Handle; // 他スレッドのイベントを UI スレッドへ戻すため

            skipHdr = settings.SkipHdr;
            worker = new BrightnessWorker { SkipHdr = skipHdr, Corrections = CopyCorrections() };
            worker.Applied += r => RunOnUi(delegate { OnApplied(r); });
            worker.Detected += found => RunOnUi(delegate
            {
                monitors = found;
                if (form != null && !form.IsDisposed) form.SetMonitors(found);
            });

            menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);
            var openItem = Theme.MenuItem("設定を開く", delegate { ShowSettings(); });
            openItem.Font = Theme.BodySemi;
            menu.Items.Add(openItem);
            menu.Items.Add(Theme.MenuItem("今すぐ適用", delegate { Check(true); }));
            autoItem = Theme.MenuItem("明るさの自動変更", delegate { TogglePause(); });
            autoItem.Checked = !settings.Paused;
            menu.Items.Add(autoItem);
            // 一時的に暗くする（開くたびに、今の状態に合わせて中身を作り直す）
            dimItem = Theme.MenuItem("一時的に暗くする", null);
            dimItem.DropDown = new ContextMenuStrip();
            Theme.StyleMenu((ContextMenuStrip)dimItem.DropDown);
            dimItem.DropDownOpening += delegate { FillDimMenu(dimItem.DropDownItems); };
            FillDimMenu(dimItem.DropDownItems);
            menu.Items.Add(dimItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Theme.MenuItem("このアプリについて", delegate { ShowAbout(); }));
            if (AppInfo.HasSupport)
                menu.Items.Add(AppInfo.SupportMenuItem());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Theme.MenuItem("終了", delegate { ExitThread(); }));

            tray = new NotifyIcon { Icon = trayIcon, ContextMenuStrip = menu, Text = AppInfo.Name, Visible = true };
            tray.MouseClick += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); };

            // なめらかに変化させる途中でも 1% ずつ変わるよう、短めの間隔で確認する
            timer = new System.Windows.Forms.Timer { Interval = 15000 };
            timer.Tick += delegate { Check(false); };
            timer.Start();

            // 復帰・ロック解除・モニターの接続直後は、モニター側の準備ができるまで少し待ってから設定し直す
            delayed = new System.Windows.Forms.Timer { Interval = 3000 };
            delayed.Tick += delegate
            {
                delayed.Stop();
                worker.RequestDetect();
                // 停止中は明るさに触れない（スリープ復帰などでモニター側の値が戻っていても上書きしない）
                if (!settings.Paused) Check(true);
            };

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.TimeChanged += OnTimeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            worker.RequestDetect();
            Check(false);   // 起動直後は前回の値がないので、停止中でなければ必ず適用される
            if (!launchedAtStartup || settings.Entries.Count == 0) ShowSettings();
        }

        // ---- システムイベント ----
        void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume) RunOnUi(ReapplyLater);
        }

        void OnTimeChanged(object sender, EventArgs e) { RunOnUi(delegate { Check(false); }); }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionUnlock) RunOnUi(ReapplyLater);
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e) { RunOnUi(ReapplyLater); }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            RunOnUi(ReloadThemeIfChanged);
        }

        void ReapplyLater()
        {
            delayed.Stop();
            delayed.Start();
        }

        void RunOnUi(MethodInvoker action)
        {
            if (invoker.IsHandleCreated && !invoker.IsDisposed) invoker.BeginInvoke(action);
        }

        void ReloadThemeIfChanged()
        {
            bool dark = Theme.Dark;
            Color accent = Theme.Accent;
            Theme.Init();
            if (dark == Theme.Dark && accent == Theme.Accent) return;
            menu.BackColor = Theme.MenuBg;
            menu.ForeColor = Theme.Fg;
            if (form != null && !form.IsDisposed)
            {
                var bounds = form.Bounds;
                form.Close();
                ShowSettings();
                form.Bounds = bounds;
            }
        }

        // ---- 明るさの適用 ----
        void TogglePause()
        {
            settings.Paused = !settings.Paused;
            TrySave();
            autoItem.Checked = !settings.Paused;
            if (form != null && !form.IsDisposed) form.RefreshStatus();
            Check(false);
        }

        /// force: 前回と同じ明るさでも設定し直す（モニターのボタンで変えられた場合などに戻すため）
        void Check(bool force)
        {
            // HDR のオン／オフが変わったら、明るさの変え方も変わるので設定し直す
            string sig = Hdr.Signature();
            if (hdrSignature != null && sig != hdrSignature)
            {
                worker.RequestDetect();
                if (!settings.Paused) force = true;
            }
            hdrSignature = sig;

            // 一時的に暗くする時間が終わったら、スケジュールの明るさに戻す
            if (dimUntil.HasValue && DateTime.Now >= dimUntil.Value)
            {
                dimUntil = null;
                force = true;
                tray.ShowBalloonTip(4000, "元の明るさに戻しました", "一時的に暗くする時間が終わりました。", ToolTipIcon.Info);
                if (form != null && !form.IsDisposed) form.SetDim(null, settings.DimFactor);
            }

            // 暗くしている間は、明るさの自動変更が停止中でも暗くした明るさにする（自分で暗くしたため）
            bool dimming = dimUntil.HasValue;
            if (!settings.Paused || force || dimming)
            {
                int target = Scheduler.TargetPercent(settings.Entries, DateTime.Now);
                if (dimming) target = Scheduler.Dimmed(target, settings.DimFactor);
                if (target >= 0 && (force || target != lastRequested))
                {
                    lastRequested = target;
                    worker.Request(target);
                }
            }
            UpdateTooltip();
        }

        // ---- 一時的に暗くする ----
        static readonly int[] DimMinutes = { 30, 60, 120 };
        static readonly string[] DimLabels = { "30 分", "1 時間", "2 時間" };

        /// 暗くし始める（minutes が 0 以下なら「元に戻すまで」）
        void StartDim(int minutes)
        {
            dimUntil = minutes > 0 ? DateTime.Now.AddMinutes(minutes) : DateTime.MaxValue;
            if (form != null && !form.IsDisposed) form.SetDim(dimUntil, settings.DimFactor);
            Check(true);
        }

        void StopDim()
        {
            dimUntil = null;
            if (form != null && !form.IsDisposed) form.SetDim(null, settings.DimFactor);
            Check(true);
        }

        void SetDimFactor(int factor)
        {
            settings.DimFactor = factor;
            TrySave();
            if (form != null && !form.IsDisposed) form.SetDim(dimUntil, settings.DimFactor);
            if (dimUntil.HasValue) Check(true);
        }

        /// 「一時的に暗くする」のメニューの中身（トレイのメニューと、設定画面のボタンで共通）
        void FillDimMenu(ToolStripItemCollection items)
        {
            items.Clear();
            if (dimUntil.HasValue)
            {
                var stop = Theme.MenuItem("元の明るさに戻す", delegate { StopDim(); });
                stop.Font = Theme.BodySemi;
                items.Add(stop);
                items.Add(new ToolStripSeparator());
            }
            for (int i = 0; i < DimMinutes.Length; i++)
            {
                int m = DimMinutes[i];
                items.Add(Theme.MenuItem(DimLabels[i] + (dimUntil.HasValue ? "（今から）" : ""), delegate { StartDim(m); }));
            }
            var forever = Theme.MenuItem("元に戻すまで", delegate { StartDim(0); });
            forever.Checked = dimUntil == DateTime.MaxValue;
            items.Add(forever);
            items.Add(new ToolStripSeparator());
            for (int i = 0; i < Settings.DimFactors.Length; i++)
            {
                int f = Settings.DimFactors[i];
                var item = Theme.MenuItem(Settings.DimFactorLabels[i], delegate { SetDimFactor(f); });
                item.Checked = settings.DimFactor == f;
                items.Add(item);
            }
        }

        /// 設定画面のボタンから「一時的に暗くする」のメニューを開く
        void ShowDimMenu(Control anchor)
        {
            var m = new ContextMenuStrip();
            Theme.StyleMenu(m);
            FillDimMenu(m.Items);
            m.Closed += delegate { invoker.BeginInvoke((MethodInvoker)delegate { m.Dispose(); }); };
            m.Show(anchor, new Point(0, anchor.Height + Theme.S(4)));
        }

        static string DimUntilText(DateTime until)
        {
            return until == DateTime.MaxValue ? "元に戻すまで" : until.ToString("H:mm") + " まで";
        }

        void Preview(int percent)
        {
            worker.Request(percent);
        }

        void OnApplied(ApplyResult r)
        {
            string time = DateTime.Now.ToString("H:mm");
            if (r.Succeeded > 0)
            {
                status = time + " に " + r.Succeeded + " 台の画面を " + r.Requested + "% にしました";
                if (r.HdrFailed > 0) status = "!" + status + "（HDR のモニターは変更できませんでした）";
                else if (r.Failed > 0) status += "（" + r.Failed + " 台は変更できませんでした）";
                lastFailed = false;
            }
            else
            {
                status = "!" + time + " に明るさを変更できませんでした。モニターの設定で DDC/CI がオンになっているか確認してください";
                if (!lastFailed)
                    tray.ShowBalloonTip(5000, "明るさを変更できませんでした",
                        "モニターの設定（OSD メニュー）で DDC/CI がオンになっているか確認してください。", ToolTipIcon.Warning);
                lastFailed = true;
            }
            // HDR の画面だけ失敗したとき（Windows の更新で仕組みが変わった場合など）は、続けて失敗している間は 1 回だけ知らせる
            if (r.HdrFailed > 0 && r.Succeeded > 0 && !lastHdrFailed)
                tray.ShowBalloonTip(5000, "HDR のモニターの明るさを変更できませんでした",
                    "Windows の更新で仕組みが変わった可能性があります。設定画面で「HDR のモニターも変える」をオフにすると、HDR のモニターには何もしなくなります。",
                    ToolTipIcon.Warning);
            lastHdrFailed = r.HdrFailed > 0;
            if (form != null && !form.IsDisposed) form.RefreshStatus();
        }

        void UpdateTooltip()
        {
            string text = AppInfo.Name;
            var now = DateTime.Now;
            int target = Scheduler.TargetPercent(settings.Entries, now);
            if (dimUntil.HasValue)
                text += "\n一時的に暗くしています（" + DimUntilText(dimUntil.Value) + "）";
            else if (settings.Paused) text += "\n明るさの自動変更は停止中";
            else if (target < 0) text += "\nスケジュール未設定";
            else
            {
                text += "\n現在 " + target + "%";
                var next = Scheduler.GetNext(settings.Entries, now);
                if (next != null && next != Scheduler.GetActive(settings.Entries, now))
                    text += "  ·  " + next.Time + " に " + next.Brightness + "%";
            }
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        // ---- 設定画面 ----
        public void RequestShow() { RunOnUi(ShowSettings); }

        void ShowSettings()
        {
            if (form != null && !form.IsDisposed)
            {
                if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
                form.Activate();
                return;
            }
            form = new MainForm(settings, appIcon, () => status, Preview);
            if (monitors != null) form.SetMonitors(monitors);
            form.SetDim(dimUntil, settings.DimFactor);
            form.DimMenuRequested += delegate(object s, EventArgs e)
            {
                // 暗くしている間はボタンが「元の明るさに戻す」になっているので、そのまま戻す
                if (dimUntil.HasValue) StopDim();
                else ShowDimMenu((Control)s);
            };
            form.SettingsChanged += delegate
            {
                TrySave();
                autoItem.Checked = !settings.Paused;
                if (settings.SkipHdr != skipHdr)
                {
                    // HDR のモニターを対象にした／外したときは、画面の一覧を読み直して設定し直す
                    skipHdr = settings.SkipHdr;
                    worker.SkipHdr = skipHdr;
                    worker.RequestDetect();
                    if (!settings.Paused) { Check(true); return; }
                }
                Check(false);
            };
            form.ApplyNowRequested += delegate { Check(true); };
            form.CorrectionsRequested += delegate { ShowCorrections(); };
            var thisForm = form;
            form.FormClosed += delegate { if (form == thisForm) form = null; thisForm.Dispose(); };
            form.Show();
            form.Activate();
        }

        List<MonitorCorrection> CopyCorrections()
        {
            var list = new List<MonitorCorrection>();
            foreach (var c in settings.Corrections) list.Add(c.Clone());
            return list;
        }

        /// モニターごとの補正の画面
        void ShowCorrections()
        {
            if (form == null || form.IsDisposed) return;
            // 画面の一覧がまだ無ければ、その場で調べる（DDC/CI の読み取りで少し時間がかかる）
            DetectResult found = monitors;
            if (found == null)
            {
                Cursor.Current = Cursors.WaitCursor;
                try { found = Display.Detect(skipHdr); } catch { found = new DetectResult(); }
                Cursor.Current = Cursors.Default;
            }
            int target = Scheduler.TargetPercent(settings.Entries, DateTime.Now);
            using (var dlg = new CorrectionDialog(found.Monitors, settings.Corrections, target < 0 ? 50 : target,
                (pct, corrections) => { worker.Corrections = corrections; worker.Request(pct); }))
            {
                bool ok = dlg.ShowDialog(form) == DialogResult.OK;
                if (ok)
                {
                    settings.Corrections = dlg.Result;
                    TrySave();
                }
                worker.Corrections = CopyCorrections();
                // プレビューで変えた明るさを戻す／保存した補正をすぐ反映する
                if (dlg.Previewed) Check(true);
                else if (ok && !settings.Paused) Check(true);
            }
        }

        bool aboutOpen;

        void ShowAbout()
        {
            if (aboutOpen) return;
            aboutOpen = true;
            try { AboutDialog.ShowFor(form != null && !form.IsDisposed && form.Visible ? form : null); }
            finally { aboutOpen = false; }
        }

        void TrySave()
        {
            try { settings.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show("設定を保存できませんでした。\n" + ex.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        protected override void ExitThreadCore()
        {
            timer.Stop();
            delayed.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.TimeChanged -= OnTimeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (form != null && !form.IsDisposed) form.Close();
            tray.Visible = false;
            tray.Dispose();
            worker.Dispose();
            invoker.Dispose();
            base.ExitThreadCore();
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, @"Local\BrightnessScheduler.SingleInstance", out createdNew))
            using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BrightnessScheduler.Show"))
            {
                if (!createdNew)
                {
                    Native.AllowSetForegroundWindow(-1);
                    showEvent.Set();
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Theme.Init();
                var app = new TrayApp(Startup.IsAutostartLaunch(args));
                var wait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { app.RequestShow(); }, null, -1, false);
                Application.Run(app);
                wait.Unregister(null);
            }
        }
    }
}
