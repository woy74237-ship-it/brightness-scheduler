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
        bool lastFailed;
        List<string> monitors;

        public TrayApp(bool launchedAtStartup)
        {
            settings = Settings.Load();
            try { Startup.RepairIfBroken(); } catch { }
            appIcon = Theme.LoadAppIcon(Size.Empty);
            trayIcon = Theme.LoadAppIcon(SystemInformation.SmallIconSize);

            invoker = new Control();
            var forceHandle = invoker.Handle; // 他スレッドのイベントを UI スレッドへ戻すため

            worker = new BrightnessWorker();
            worker.Applied += r => RunOnUi(delegate { OnApplied(r); });
            worker.Detected += names => RunOnUi(delegate
            {
                monitors = names;
                if (form != null && !form.IsDisposed) form.SetMonitors(names);
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
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Theme.MenuItem("このアプリについて", delegate { ShowAbout(); }));
            if (AppInfo.HasSupport)
                menu.Items.Add(Theme.MenuItem("開発を支援する", delegate { AppInfo.Open(AppInfo.SupportUrl); }));
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
            if (!settings.Paused || force)
            {
                int target = Scheduler.TargetPercent(settings.Entries, DateTime.Now);
                if (target >= 0 && (force || target != lastRequested))
                {
                    lastRequested = target;
                    worker.Request(target);
                }
            }
            UpdateTooltip();
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
                if (r.Failed > 0) status += "（" + r.Failed + " 台は変更できませんでした）";
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
            if (form != null && !form.IsDisposed) form.RefreshStatus();
        }

        void UpdateTooltip()
        {
            string text = AppInfo.Name;
            var now = DateTime.Now;
            int target = Scheduler.TargetPercent(settings.Entries, now);
            if (settings.Paused) text += "\n明るさの自動変更は停止中";
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
            form.SettingsChanged += delegate
            {
                TrySave();
                autoItem.Checked = !settings.Paused;
                Check(false);
            };
            form.ApplyNowRequested += delegate { Check(true); };
            var thisForm = form;
            form.FormClosed += delegate { if (form == thisForm) form = null; thisForm.Dispose(); };
            form.Show();
            form.Activate();
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
