// 追加・編集ダイアログ（時刻・明るさ・変化の仕方）
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BrightnessScheduler
{
    class EntryDialog : ThemedDialog
    {
        readonly TimeSelector time;
        readonly BrightnessSlider slider;
        readonly ToggleSwitch previewToggle;
        readonly ChoiceCards mode;
        readonly Action<int> preview;
        readonly Rectangle readoutRect;

        public ScheduleEntry Result { get; private set; }
        /// プレビューで画面の明るさを変えたか（閉じたあとにスケジュールの明るさへ戻すため）
        public bool Previewed { get; private set; }

        /// preview: 画面の明るさを試しに変える処理（null ならプレビューなし）
        public EntryDialog(ScheduleEntry source, bool isNew, Action<int> preview)
            : base(isNew ? "明るさの追加" : "明るさの編集", 720, 470)
        {
            this.preview = preview;
            int pad = Theme.S(24);

            // ---- 左: 時刻 ----
            var timeLabel = Theme.MakeLabel("時刻", Theme.BodySemi, Theme.Fg, Theme.Bg);
            timeLabel.Location = new Point(pad, pad);
            time = new TimeSelector { Location = new Point(pad, pad + Theme.S(36)) };
            TimeSpan ts;
            if (!TimeSpan.TryParseExact(source.Time ?? "", @"hh\:mm", CultureInfo.InvariantCulture, out ts)) ts = new TimeSpan(7, 0, 0);
            time.Value = ts;
            Controls.Add(timeLabel);
            Controls.Add(time);

            string[] names = { "朝", "昼", "夕方", "夜" };
            int[] hours = { 7, 12, 18, 22 };
            int chipW = (time.Width - Theme.S(8)) / 2, chipH = Theme.S(32);
            for (int i = 0; i < names.Length; i++)
            {
                int h = hours[i];
                var chip = new FlatButton { Text = names[i] + "  " + h + ":00", Kind = ButtonKind.Standard, Font = Theme.Small };
                chip.SetBounds(pad + (i % 2) * (chipW + Theme.S(8)), time.Bottom + Theme.S(18) + (i / 2) * (chipH + Theme.S(8)), chipW, chipH);
                chip.Click += delegate { time.Value = new TimeSpan(h, 0, 0); time.Focus(); };
                Controls.Add(chip);
            }

            // ---- 右: 明るさ ----
            int rx = pad + time.Width + Theme.S(40), rw = ClientSize.Width - rx - pad;
            var brightLabel = Theme.MakeLabel("明るさ", Theme.BodySemi, Theme.Fg, Theme.Bg);
            brightLabel.Location = new Point(rx, pad);
            Controls.Add(brightLabel);

            readoutRect = new Rectangle(rx, pad + Theme.S(34), rw, Theme.S(52));
            slider = new BrightnessSlider { Value = source.Brightness };
            slider.SetBounds(rx - Theme.S(4), readoutRect.Bottom + Theme.S(4), rw + Theme.S(8), Theme.S(36));
            slider.ValueChanged += delegate { Invalidate(readoutRect); };
            slider.ValueCommitted += delegate { Preview(); };
            Controls.Add(slider);

            // よく使う明るさ
            int[] presets = { 0, 25, 50, 75, 100 };
            int pw = (rw - Theme.S(8) * (presets.Length - 1)) / presets.Length;
            for (int i = 0; i < presets.Length; i++)
            {
                int p = presets[i];
                var chip = new FlatButton { Text = p + "%", Kind = ButtonKind.Standard, Font = Theme.Small };
                chip.SetBounds(rx + i * (pw + Theme.S(8)), slider.Bottom + Theme.S(6), pw, Theme.S(30));
                chip.Click += delegate { slider.Value = p; slider.Focus(); Preview(); };
                Controls.Add(chip);
            }

            previewToggle = new ToggleSwitch { Text = "変えたら画面で試す", Font = Theme.Small, Checked = preview != null, Visible = preview != null };
            previewToggle.FitWidth();
            previewToggle.SetBounds(rx, slider.Bottom + Theme.S(44), previewToggle.Width, Theme.S(30));
            previewToggle.CheckedChanged += delegate { if (previewToggle.Checked) Preview(); };
            Controls.Add(previewToggle);

            // ---- 下: 変化の仕方 ----
            int my = previewToggle.Bottom + Theme.S(18);
            var modeLabel = Theme.MakeLabel("変化の仕方", Theme.BodySemi, Theme.Fg, Theme.Bg);
            modeLabel.Location = new Point(rx, my);
            Controls.Add(modeLabel);
            mode = new ChoiceCards();
            mode.Titles = new[] { "その時刻に切り替え", "なめらかに変化" };
            mode.Descriptions = new[] { "時刻になったら、この明るさにすぐ変えます", "前の時刻から少しずつ変えて、この時刻にちょうどこの明るさにします" };
            mode.Selected = source.Fade ? 1 : 0;
            mode.SetBounds(rx, my + Theme.S(30), rw, Theme.S(96));
            Controls.Add(mode);

            var save = Footer.Add(isNew ? "追加" : "保存", ButtonKind.Primary, DialogResult.None);
            var cancel = Footer.Add("キャンセル", ButtonKind.Standard, DialogResult.Cancel);
            save.Click += delegate { Accept(); };
            AcceptButton = save;
            CancelButton = cancel;

            Shown += delegate { time.Focus(); };
        }

        void Preview()
        {
            if (preview == null || !previewToggle.Checked) return;
            Previewed = true;
            preview(slider.Value);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var r = readoutRect;
            int icon = Theme.S(40);
            SunIcon.Draw(g, new RectangleF(r.X, r.Y + (r.Height - icon) / 2f, icon, icon), slider.Value, Theme.Accent);
            Theme.DrawText(g, slider.Value + "%", Theme.TimeBig, Theme.Fg, new Rectangle(r.X + icon + Theme.S(14), r.Y, r.Width - icon - Theme.S(14), r.Height), Theme.Left);
        }

        void Accept()
        {
            Result = new ScheduleEntry { Time = ScheduleEntry.Format(time.Value), Brightness = slider.Value, Fade = mode.Selected == 1 };
            DialogResult = DialogResult.OK;
        }
    }
}
