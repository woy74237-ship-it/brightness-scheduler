// メイン画面（現在の明るさ・24 時間グラフ・スケジュール一覧）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BrightnessScheduler
{
    /// 上部の「現在の明るさ」カード
    class HeaderPanel : SmoothControl
    {
        public int Target = -1;           // スケジュール上の今の明るさ
        public ScheduleEntry Next;
        public bool Paused, Fading;
        public string Status = "";        // 画面への反映状況
        public bool StatusIsError;
        public readonly FlatButton ApplyButton;
        public readonly ToggleSwitch AutoToggle;
        public readonly BrightnessChart Chart;

        public HeaderPanel()
        {
            BackColor = Theme.Surface;
            ApplyButton = new FlatButton { Text = "今すぐ適用", Glyph = Glyphs.Refresh, Kind = ButtonKind.Standard };
            AutoToggle = new ToggleSwitch { Text = "自動調整" };
            Chart = new BrightnessChart();
            Controls.AddRange(new Control[] { ApplyButton, AutoToggle, Chart });
        }

        Rectangle IconRect { get { return new Rectangle(Theme.S(20), Theme.S(20), Theme.S(84), Theme.S(84)); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int h = Theme.S(34);
            AutoToggle.FitWidth();
            AutoToggle.SetBounds(Width - Theme.S(20) - AutoToggle.Width, Theme.S(18), AutoToggle.Width, h);
            ApplyButton.SetBounds(Width - Theme.S(20) - ApplyButton.PreferredWidth(), Theme.S(70), ApplyButton.PreferredWidth(), h);
            int cy = IconRect.Bottom + Theme.S(28);
            Chart.SetBounds(Theme.S(12), cy, Width - Theme.S(32), Height - cy - Theme.S(12));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, ParentBack);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRound(g, Theme.Surface, r, Theme.S(8));
            Theme.DrawRound(g, Theme.Border, r, Theme.S(8), 1f);

            var ir = IconRect;
            Theme.FillRound(g, Theme.Track, ir, Theme.S(10));
            SunIcon.Draw(g, new RectangleF(ir.X + Theme.S(14), ir.Y + Theme.S(14), ir.Width - Theme.S(28), ir.Height - Theme.S(28)),
                Math.Max(0, Target), Target < 0 ? Theme.FgDim : Theme.Accent);

            int tx = ir.Right + Theme.S(20), tw = AutoToggle.Left - tx - Theme.S(12);
            Theme.DrawText(g, Paused ? "現在の明るさ  ·  自動調整は停止中" : "現在の明るさ", Theme.Small, Theme.FgDim,
                new Rectangle(tx, Theme.S(16), tw, Theme.S(20)), Theme.Left);

            string big = Target < 0 ? "スケジュールがありません" : Target + "%";
            var font = Target < 0 ? Theme.Title : Theme.TimeBig;
            Theme.DrawText(g, big, font, Theme.Fg, new Rectangle(tx, Theme.S(36), tw, Theme.S(46)), Theme.Left);

            string sub;
            var now = DateTime.Now;
            if (Target < 0) sub = "「追加」から時刻と明るさを登録してください";
            else if (Next == null) sub = "毎日この明るさにします";
            else if (Fading) sub = Next.Time + " に " + Next.Brightness + "% へ向けて変化中（あと " + Scheduler.Remaining(Next.TimeOfDay, now) + "）";
            else sub = "次は " + Next.Time + " に " + Next.Brightness + "%（あと " + Scheduler.Remaining(Next.TimeOfDay, now) + "）";
            int subW = ApplyButton.Left - tx - Theme.S(12);
            Theme.DrawText(g, sub, Theme.Body, Theme.FgDim, new Rectangle(tx, Theme.S(82), subW, Theme.S(22)), Theme.Left);
            if (!string.IsNullOrEmpty(Status))
                Theme.DrawText(g, Status, Theme.Small, StatusIsError ? Theme.Danger : Theme.FgDim,
                    new Rectangle(tx, Theme.S(104), Width - tx - Theme.S(20), Theme.S(18)), Theme.Left);
        }
    }

    class MainForm : Form
    {
        readonly Settings settings;
        readonly Func<string> statusProvider;
        readonly Action<int> preview;
        readonly HeaderPanel header;
        readonly Label sectionTitle, countLabel, monitorLabel;
        readonly FlatButton addButton;
        readonly FlowLayoutPanel cardPanel;
        readonly ToggleSwitch startupToggle;
        readonly FlatButton startupWarning;
        readonly FlatButton aboutButton;
        readonly Timer clock;
        readonly ToolTip tips;
        bool updating, updatingStartup, showStartupWarning;

        public event EventHandler SettingsChanged, ApplyNowRequested;

        /// statusProvider: 画面への反映状況の文字列（先頭が "!" ならエラー）
        public MainForm(Settings settings, Icon icon, Func<string> statusProvider, Action<int> preview)
        {
            this.settings = settings;
            this.statusProvider = statusProvider;
            this.preview = preview;
            Text = AppInfo.Name;
            Icon = icon;
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Theme.S(780), Theme.S(720));
            MinimumSize = SizeFromClientSize(new Size(Theme.S(640), Theme.S(600)));

            tips = new ToolTip();

            header = new HeaderPanel();
            header.ApplyButton.Click += delegate { if (ApplyNowRequested != null) ApplyNowRequested(this, EventArgs.Empty); };
            tips.SetToolTip(header.ApplyButton, "モニターのボタンなどで明るさを変えたときに、スケジュールの明るさへ戻します");
            header.AutoToggle.CheckedChanged += delegate
            {
                if (updating) return;
                settings.Paused = !header.AutoToggle.Checked;
                Changed(false);
            };

            sectionTitle = Theme.MakeLabel("スケジュール", Theme.Heading, Theme.Fg, Theme.Bg);
            countLabel = Theme.MakeLabel("", Theme.Body, Theme.FgDim, Theme.Bg);
            addButton = new FlatButton { Text = "追加", Glyph = Glyphs.Add, Kind = ButtonKind.Primary };
            addButton.Click += delegate { AddEntry(); };

            cardPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0)
            };
            cardPanel.HorizontalScroll.Maximum = 0;
            cardPanel.AutoScroll = false;
            cardPanel.HorizontalScroll.Visible = false;
            cardPanel.AutoScroll = true;
            cardPanel.Paint += PaintEmptyState;
            cardPanel.ClientSizeChanged += delegate { FitCards(); cardPanel.Invalidate(); };
            cardPanel.HandleCreated += delegate { Theme.DarkScrollbars(cardPanel); };

            monitorLabel = Theme.MakeLabel("モニターを確認中…", Theme.Small, Theme.FgDim, Theme.Bg);

            startupToggle = new ToggleSwitch { Text = "Windows の起動時に開始" };
            startupToggle.CheckedChanged += delegate
            {
                if (updatingStartup) return;
                try { Startup.SetEnabled(startupToggle.Checked, true); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "自動起動の設定に失敗しました。\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                RefreshStartupState();
            };

            startupWarning = new FlatButton
            {
                Text = "Windows の設定で自動起動がオフになっています（クリックで設定を開く）",
                Glyph = Glyphs.Warning, Kind = ButtonKind.Subtle, Font = Theme.Small,
                TextColorOverride = Theme.Danger, Visible = false
            };
            startupWarning.Click += delegate { Startup.OpenWindowsStartupSettings(); };
            tips.SetToolTip(startupWarning, "「Windows の起動時に開始」のスイッチをオンにしても解除できます");
            Activated += delegate { RefreshStartupState(); };

            aboutButton = new FlatButton { Glyph = AppInfo.HasSupport ? Glyphs.Heart : Glyphs.Info, Kind = ButtonKind.Subtle };
            aboutButton.Click += delegate { AboutDialog.ShowFor(this); };
            tips.SetToolTip(aboutButton, AppInfo.HasSupport ? "このアプリについて・開発の支援" : "このアプリについて");

            Controls.AddRange(new Control[] { header, sectionTitle, countLabel, addButton, cardPanel, monitorLabel, startupToggle, startupWarning, aboutButton });
            RefreshStartupState();

            clock = new Timer { Interval = 15000 };
            clock.Tick += delegate { RefreshStatus(); };
            clock.Start();

            RebuildCards();
            DoLayout();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindow(this);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            DoLayout();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            clock.Stop();
            clock.Dispose();
            tips.Dispose();
            base.OnFormClosed(e);
        }

        void DoLayout()
        {
            if (header == null || startupWarning == null) return;
            int pad = Theme.S(24), w = ClientSize.Width - pad * 2, btnH = Theme.S(34);
            header.SetBounds(pad, pad, w, Theme.S(270));

            int y = header.Bottom + Theme.S(28);
            addButton.SetBounds(pad + w - addButton.PreferredWidth(), y, addButton.PreferredWidth(), btnH);
            sectionTitle.Location = new Point(pad, y + (btnH - sectionTitle.Height) / 2);
            countLabel.Location = new Point(sectionTitle.Right + Theme.S(4), sectionTitle.Bottom - countLabel.Height - Theme.S(2));
            y += btnH + Theme.S(14);

            int footerY = ClientSize.Height - pad - btnH;
            int listBottom = footerY - Theme.S(20);
            if (showStartupWarning)
            {
                int ww = Math.Min(w, startupWarning.PreferredWidth());
                int wh = Theme.S(30);
                startupWarning.SetBounds(pad + w - ww, footerY - Theme.S(8) - wh, ww, wh);
                listBottom = startupWarning.Top - Theme.S(10);
            }
            cardPanel.SetBounds(pad, y, w, Math.Max(Theme.S(60), listBottom - y));

            aboutButton.SetBounds(pad + w - btnH, footerY, btnH, btnH);
            startupToggle.FitWidth();
            startupToggle.SetBounds(aboutButton.Left - Theme.S(16) - startupToggle.Width, footerY, startupToggle.Width, btnH);
            monitorLabel.MaximumSize = new Size(Math.Max(Theme.S(60), startupToggle.Left - pad - Theme.S(16)), 0);
            monitorLabel.Location = new Point(pad, footerY + (btnH - monitorLabel.Height) / 2);
            FitCards();
        }

        void RefreshStartupState()
        {
            if (startupToggle == null || startupWarning == null) return;
            bool registered = false, blocked = false;
            try { registered = Startup.IsEnabled(); blocked = registered && Startup.IsBlockedByWindows(); } catch { }
            updatingStartup = true;
            startupToggle.Checked = registered && !blocked;
            updatingStartup = false;
            if (showStartupWarning != blocked)
            {
                showStartupWarning = blocked;
                startupWarning.Visible = blocked;
                DoLayout();
            }
        }

        void FitCards()
        {
            int cw = cardPanel.ClientSize.Width;
            foreach (Control c in cardPanel.Controls)
                if (c.Width != cw) c.Width = cw;
        }

        /// 見つかったモニターを下部に表示
        public void SetMonitors(List<string> names)
        {
            if (names == null) return;
            monitorLabel.Text = names.Count == 0
                ? "明るさを変えられるモニターが見つかりません（モニターの設定で DDC/CI をオンにしてください）"
                : "対象: " + string.Join("、", names);
            monitorLabel.ForeColor = names.Count == 0 ? Theme.Danger : Theme.FgDim;
            tips.SetToolTip(monitorLabel, monitorLabel.Text);
            DoLayout();
        }

        // ---- 表示の更新 ----
        void RebuildCards()
        {
            cardPanel.SuspendLayout();
            var old = cardPanel.Controls.Cast<Control>().ToList();
            cardPanel.Controls.Clear();
            foreach (var c in old) c.Dispose();
            foreach (var en in Scheduler.Sorted(settings.Entries))
            {
                var entry = en;
                var card = new ScheduleCard(entry);
                card.EditClicked += delegate { BeginInvoke((MethodInvoker)delegate { EditEntry(entry); }); };
                card.DeleteClicked += delegate { BeginInvoke((MethodInvoker)delegate { DeleteEntry(entry); }); };
                cardPanel.Controls.Add(card);
            }
            cardPanel.ResumeLayout();
            FitCards();
            countLabel.Text = settings.Entries.Count > 0 ? settings.Entries.Count + " 件" : "";
            cardPanel.Invalidate();
            RefreshStatus();
        }

        public void RefreshStatus()
        {
            var now = DateTime.Now;
            var active = Scheduler.GetActive(settings.Entries, now);
            var next = Scheduler.GetNext(settings.Entries, now);
            header.Target = Scheduler.TargetPercent(settings.Entries, now);
            header.Next = next == active ? null : next;
            header.Fading = next != null && next != active && next.Fade && next.Brightness != active.Brightness;
            header.Paused = settings.Paused;
            header.Chart.Entries = Scheduler.Sorted(settings.Entries);
            string status = statusProvider != null ? statusProvider() : "";
            header.StatusIsError = status.StartsWith("!");
            header.Status = status.TrimStart('!');

            updating = true;
            header.AutoToggle.Checked = !settings.Paused;
            updating = false;

            header.Invalidate();
            header.Chart.Invalidate();
            foreach (Control c in cardPanel.Controls)
            {
                var card = c as ScheduleCard;
                if (card == null) continue;
                bool isActive = card.Entry == active;
                if (card.IsActive != isActive) { card.IsActive = isActive; card.Invalidate(); }
            }
        }

        void PaintEmptyState(object sender, PaintEventArgs e)
        {
            if (cardPanel.Controls.Count > 0) return;
            int w = cardPanel.ClientSize.Width, cy = cardPanel.ClientSize.Height / 2;
            Theme.DrawText(e.Graphics, Glyphs.Brightness, Theme.IconLarge, Theme.FgDim, new Rectangle(0, cy - Theme.S(70), w, Theme.S(50)), Theme.Center);
            Theme.DrawText(e.Graphics, "まだスケジュールがありません", Theme.BodySemi, Theme.Fg, new Rectangle(0, cy - Theme.S(12), w, Theme.S(26)), Theme.Center);
            Theme.DrawText(e.Graphics, "「追加」を押して、時刻と明るさを登録してください（例: 朝 7:00 に 80%、夜 22:00 に 30%）", Theme.Small, Theme.FgDim,
                new Rectangle(0, cy + Theme.S(16), w, Theme.S(22)), Theme.Center);
        }

        void Changed(bool rebuild)
        {
            if (rebuild) RebuildCards(); else RefreshStatus();
            if (SettingsChanged != null) SettingsChanged(this, EventArgs.Empty);
        }

        // ---- 追加・編集・削除 ----
        void AddEntry()
        {
            var t = SuggestTime();
            int b = Scheduler.TargetPercent(settings.Entries, DateTime.Today.Add(t));
            var draft = new ScheduleEntry { Time = ScheduleEntry.Format(t), Brightness = b < 0 ? 70 : b };
            RunDialog(null, draft, true);
        }

        void EditEntry(ScheduleEntry entry) { RunDialog(entry, entry, false); }

        void RunDialog(ScheduleEntry original, ScheduleEntry source, bool isNew)
        {
            using (var dlg = new EntryDialog(source, isNew, preview))
            {
                bool ok = dlg.ShowDialog(this) == DialogResult.OK;
                if (ok) Upsert(original, dlg.Result);
                // プレビューで変えた明るさを、スケジュールの明るさに戻す
                if (dlg.Previewed && ApplyNowRequested != null) ApplyNowRequested(this, EventArgs.Empty);
            }
        }

        void DeleteEntry(ScheduleEntry entry)
        {
            string msg = entry.Time + "（" + entry.Brightness + "%）を削除しますか？";
            using (var dlg = new ConfirmDialog("明るさの削除", msg, "削除"))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                settings.Entries.Remove(entry);
                Changed(true);
            }
        }

        void Upsert(ScheduleEntry original, ScheduleEntry result)
        {
            if (original != null) settings.Entries.Remove(original);
            settings.Entries.RemoveAll(x => x.Time == result.Time);
            settings.Entries.Add(result);
            Changed(true);
        }

        TimeSpan SuggestTime()
        {
            if (settings.Entries.Count == 0) return new TimeSpan(7, 0, 0);
            var t = settings.Entries.Max(x => x.TimeOfDay).Add(TimeSpan.FromHours(1));
            if (t.TotalHours >= 24) t = t.Subtract(TimeSpan.FromHours(24));
            return t;
        }
    }
}
