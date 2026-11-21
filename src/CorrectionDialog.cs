// モニターごとの補正の画面
// 方式（範囲で指定・ずらす量・倍率）をモニターごとに選び、確認用の明るさを動かしながら実際の画面で見比べられる
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace BrightnessScheduler
{
    /// 横に並んだボタンから 1 つを選ぶ
    class SegmentedControl : SmoothControl
    {
        string[] items = new string[0];
        int selected, hot = -1;
        public event EventHandler SelectedChanged;

        public SegmentedControl() { Cursor = Cursors.Hand; Font = Theme.Small; Height = Theme.S(30); }

        public string[] Items { get { return items; } set { items = value ?? new string[0]; Invalidate(); } }

        public int Selected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); }
        }

        Rectangle Item(int i)
        {
            int w = Width / Math.Max(1, items.Length);
            return new Rectangle(i * w, 0, i == items.Length - 1 ? Width - i * w : w, Height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = -1;
            for (int i = 0; i < items.Length; i++) if (Item(i).Contains(e.Location)) h = i;
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = 0; i < items.Length; i++) if (Item(i).Contains(e.Location)) Selected = i;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRound(g, Theme.Bg, r, Theme.S(6));
            Theme.DrawRound(g, Theme.Border, r, Theme.S(6), 1f);
            for (int i = 0; i < items.Length; i++)
            {
                var it = Item(i);
                var inner = new RectangleF(it.X + Theme.S(3), it.Y + Theme.S(3), it.Width - Theme.S(6), it.Height - Theme.S(6));
                if (i == selected) Theme.FillRound(g, Theme.Accent, inner, Theme.S(4));
                else if (i == hot) Theme.FillRound(g, Theme.HoverOverlay, inner, Theme.S(4));
                Theme.DrawText(g, items[i], i == selected ? Theme.BodySemi : Font, i == selected ? Theme.OnAccent : Theme.Fg, it, Theme.Center);
            }
        }
    }

    // ------------------------------------------------------------------
    /// モニター 1 台分の補正のカード
    class CorrectionCard : SmoothControl
    {
        public readonly MonitorCorrection Value;
        readonly bool isHdr;
        readonly SegmentedControl mode;
        readonly BrightnessSlider minSlider, maxSlider, shiftSlider, factorSlider;
        readonly FlatButton guideButton;
        public event EventHandler Committed, GuideRequested, ModeChanged;
        int highlight = -1;

        /// ガイドで目立たせる行（-1: なし、0: 「0% のとき」、1: 「100% のとき」）
        public int Highlight { get { return highlight; } set { highlight = value; Invalidate(); } }

        public void FocusRow(int row) { (row == 0 ? minSlider : maxSlider).Focus(); }
        /// 確認用の明るさ（カード下部の「→ 何 %」の表示に使う）
        public int TestPercent = 50;

        static readonly string[] Modes = { MonitorCorrection.Range, MonitorCorrection.Offset, MonitorCorrection.Scale };

        public CorrectionCard(MonitorCorrection value, bool isHdr)
        {
            Value = value;
            this.isHdr = isHdr;
            BackColor = Theme.Surface;
            Height = Theme.S(176);
            Margin = new Padding(0, 0, 0, Theme.S(10));

            mode = new SegmentedControl { Items = new[] { "範囲で指定", "ずらす", "倍率" }, BackColor = Theme.Surface };
            mode.Selected = Math.Max(0, Array.IndexOf(Modes, value.Mode));
            mode.SelectedChanged += delegate
            {
                Value.Mode = Modes[mode.Selected];
                UpdateVisibility();
                Invalidate();
                if (ModeChanged != null) ModeChanged(this, EventArgs.Empty);
                Commit();
            };

            guideButton = new FlatButton { Text = "ガイドで合わせる", Glyph = Glyphs.Play, Kind = ButtonKind.Subtle, Font = Theme.Small, BackColor = Theme.Surface };
            guideButton.Click += delegate { if (GuideRequested != null) GuideRequested(this, EventArgs.Empty); };

            minSlider = MakeSlider(0, 100, null, value.Min, v => Value.Min = v);
            maxSlider = MakeSlider(0, 100, null, value.Max, v => Value.Max = v);
            shiftSlider = MakeSlider(-50, 50, 0, value.Shift, v => Value.Shift = v);
            factorSlider = MakeSlider(0, 200, 100, value.Factor, v => Value.Factor = v);
            Controls.AddRange(new Control[] { mode, guideButton, minSlider, maxSlider, shiftSlider, factorSlider });
            UpdateVisibility();
        }

        BrightnessSlider MakeSlider(int min, int max, int? fillFrom, int v, Action<int> set)
        {
            var s = new BrightnessSlider { Minimum = min, Maximum = max, FillFrom = fillFrom, BackColor = Theme.Surface };
            s.Value = v;
            s.ValueChanged += delegate { set(s.Value); Invalidate(); };
            s.ValueCommitted += delegate { Commit(); };
            return s;
        }

        void Commit() { if (Committed != null) Committed(this, EventArgs.Empty); }

        public void Reset()
        {
            minSlider.Value = 0; maxSlider.Value = 100; shiftSlider.Value = 0; factorSlider.Value = 100;
            Invalidate();
        }

        void UpdateVisibility()
        {
            minSlider.Visible = maxSlider.Visible = guideButton.Visible = Value.Mode == MonitorCorrection.Range || string.IsNullOrEmpty(Value.Mode);
            shiftSlider.Visible = Value.Mode == MonitorCorrection.Offset;
            factorSlider.Visible = Value.Mode == MonitorCorrection.Scale;
        }

        // 行の位置
        const int LabelW = 104, ValueW = 64;
        int Row(int i) { return Theme.S(58) + i * Theme.S(40); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Theme.S(18);
            if (mode == null) return;
            mode.SetBounds(Width - pad - Theme.S(300), Theme.S(14), Theme.S(300), Theme.S(32));
            int gw = guideButton.PreferredWidth();
            guideButton.SetBounds(mode.Left - Theme.S(8) - gw, Theme.S(15), gw, Theme.S(30));
            int sx = pad + Theme.S(LabelW), sw = Width - sx - pad - Theme.S(ValueW);
            foreach (var s in new[] { minSlider, maxSlider, shiftSlider, factorSlider }) s.Width = sw;
            minSlider.SetBounds(sx, Row(0), sw, Theme.S(36));
            maxSlider.SetBounds(sx, Row(1), sw, Theme.S(36));
            shiftSlider.SetBounds(sx, Row(0), sw, Theme.S(36));
            factorSlider.SetBounds(sx, Row(0), sw, Theme.S(36));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, ParentBack);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Theme.FillRound(g, Theme.Surface, r, Theme.S(8));
            Theme.DrawRound(g, Theme.Border, r, Theme.S(8), 1f);
            int pad = Theme.S(18);

            // 名前
            string name = Value.Name ?? "モニター";
            var ns = Theme.Measure(name, Theme.BodySemi);
            int nameRight = (guideButton.Visible ? guideButton.Left : mode.Left) - Theme.S(60);
            Theme.DrawText(g, name, Theme.BodySemi, Theme.Fg, new Rectangle(pad, Theme.S(14), Math.Max(0, Math.Min(ns.Width + 2, nameRight - pad)), Theme.S(32)), Theme.Left);
            if (isHdr)
            {
                var br = new RectangleF(pad + ns.Width + Theme.S(10), Theme.S(19), Theme.Measure("HDR", Theme.Small).Width + Theme.S(14), Theme.S(22));
                Theme.DrawRound(g, Theme.FgDim, br, br.Height / 2f, 1f);
                Theme.DrawText(g, "HDR", Theme.Small, Theme.FgDim, Rectangle.Round(br), Theme.Center);
            }

            // 各行のラベルと値
            int vx = Width - pad - Theme.S(ValueW) + Theme.S(10);
            // ガイド中の行を枠で目立たせる
            bool rangeMode = Value.Mode != MonitorCorrection.Offset && Value.Mode != MonitorCorrection.Scale;
            if (highlight >= 0 && rangeMode)
                Theme.DrawRound(g, Theme.Accent, new RectangleF(pad - Theme.S(10), Row(highlight) - Theme.S(2), Width - pad * 2 + Theme.S(20), Theme.S(40)), Theme.S(6), Theme.Sf(2));
            Action<int, string, string> rowText = (i, label, val) =>
            {
                bool hl = rangeMode && i == highlight;
                Theme.DrawText(g, label, hl ? Theme.BodySemi : Theme.Body, hl ? Theme.Fg : Theme.FgDim, new Rectangle(pad, Row(i), Theme.S(LabelW), Theme.S(36)), Theme.Left);
                Theme.DrawText(g, val, Theme.BodySemi, Theme.Fg, new Rectangle(vx, Row(i), Theme.S(ValueW), Theme.S(36)), Theme.Left);
            };
            string hint;
            switch (Value.Mode)
            {
                case MonitorCorrection.Offset:
                    rowText(0, "ずらす量", (Value.Shift > 0 ? "+" : "") + Value.Shift + "%");
                    hint = "どの明るさでも同じだけずらします";
                    break;
                case MonitorCorrection.Scale:
                    rowText(0, "倍率", Value.Factor + "%");
                    hint = "明るいときほど差が大きくなります";
                    break;
                default:
                    rowText(0, "0% のとき", Value.Min + "%");
                    rowText(1, "100% のとき", Value.Max + "%");
                    hint = "暗いとき・明るいときの値をそれぞれ決めます";
                    break;
            }

            // 結果の目安
            string result = "確認用 " + TestPercent + "% → " + Value.Apply(TestPercent) + "%　　（0% → " + Value.Apply(0) + "%、100% → " + Value.Apply(100) + "%）";
            int by = Height - Theme.S(38);
            Theme.DrawText(g, result, Theme.Small, Theme.Fg, new Rectangle(pad, by, Width - pad * 2, Theme.S(22)), Theme.Left);
            if (Value.Mode != MonitorCorrection.Range)
                Theme.DrawText(g, hint, Theme.Small, Theme.FgDim, new Rectangle(pad, Row(1), Width - pad * 2, Theme.S(36)), Theme.Left);
        }
    }

    // ------------------------------------------------------------------
    class CorrectionDialog : ThemedDialog
    {
        readonly List<MonitorCorrection> stored;   // 保存済みの補正（つながっていないモニターの分も含む）
        readonly List<CorrectionCard> cards = new List<CorrectionCard>();
        readonly BrightnessSlider testSlider;
        readonly ToggleSwitch previewToggle;
        readonly Action<int, List<MonitorCorrection>> preview;
        readonly Rectangle introRect, readoutRect;
        readonly bool empty;
        readonly FlowLayoutPanel panel;

        // 「範囲で指定」のガイド（0: なし、1: 暗いとき、2: 明るいとき、3: 完了）
        int guideStep;
        CorrectionCard guideCard;
        readonly FlatButton guideNext, guideBack;

        public List<MonitorCorrection> Result { get; private set; }
        /// プレビューで画面の明るさを変えたか（閉じたあとにスケジュールの明るさへ戻すため）
        public bool Previewed { get; private set; }

        public CorrectionDialog(List<DetectedMonitor> monitors, List<MonitorCorrection> current, int testPercent,
                                Action<int, List<MonitorCorrection>> preview)
            : base("モニターごとの補正", 720, 712)
        {
            this.preview = preview;
            stored = current.Select(c => c.Clone()).ToList();
            int pad = Theme.S(24);

            // 上部: ふだんは説明、ガイド中は手順の案内（右にボタン）
            introRect = new Rectangle(pad, Theme.S(16), ClientSize.Width - pad * 2, Theme.S(96));
            guideNext = new FlatButton { Kind = ButtonKind.Primary, Visible = false };
            guideBack = new FlatButton { Kind = ButtonKind.Standard, Visible = false };
            guideNext.Click += delegate { GuideNext(); };
            guideBack.Click += delegate { GuideBack(); };
            Controls.Add(guideNext);
            Controls.Add(guideBack);

            var testLabel = Theme.MakeLabel("確認用の明るさ", Theme.BodySemi, Theme.Fg, Theme.Bg);
            testLabel.Location = new Point(pad, introRect.Bottom + Theme.S(12));
            Controls.Add(testLabel);
            readoutRect = new Rectangle(ClientSize.Width - pad - Theme.S(70), testLabel.Bottom + Theme.S(4), Theme.S(70), Theme.S(36));
            testSlider = new BrightnessSlider { Value = Math.Max(0, testPercent) };
            testSlider.SetBounds(pad - Theme.S(4), readoutRect.Y, readoutRect.X - pad - Theme.S(8), Theme.S(36));
            testSlider.ValueChanged += delegate
            {
                Invalidate(readoutRect);
                foreach (var c in cards) { c.TestPercent = testSlider.Value; c.Invalidate(); }
            };
            testSlider.ValueCommitted += delegate { Preview(); };
            Controls.Add(testSlider);

            previewToggle = new ToggleSwitch { Text = "変えたら画面で試す", Font = Theme.Small, Checked = preview != null, Visible = preview != null };
            previewToggle.FitWidth();
            previewToggle.SetBounds(pad, testSlider.Bottom + Theme.S(4), previewToggle.Width, Theme.S(30));
            previewToggle.CheckedChanged += delegate { if (previewToggle.Checked) Preview(); };
            Controls.Add(previewToggle);

            // モニターのカード
            panel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Bg, AutoScroll = true
            };
            int top = previewToggle.Bottom + Theme.S(14);
            panel.SetBounds(pad, top, ClientSize.Width - pad * 2, ClientSize.Height - Footer.Height - top - Theme.S(12));
            panel.HandleCreated += delegate { Theme.DarkScrollbars(panel); };
            foreach (var m in monitors ?? new List<DetectedMonitor>())
            {
                var existing = stored.FirstOrDefault(c => string.Equals(c.Id, m.Id, StringComparison.OrdinalIgnoreCase));
                var value = existing != null ? existing.Clone() : MonitorCorrection.Default(m.Id, m.Name);
                value.Name = m.Name;
                var card = new CorrectionCard(value, m.Hdr) { TestPercent = testSlider.Value };
                card.Committed += delegate { Preview(); };
                card.GuideRequested += delegate { StartGuide(card); };
                card.ModeChanged += delegate { if (card == guideCard) EndGuide(); };
                cards.Add(card);
                panel.Controls.Add(card);
            }
            empty = cards.Count == 0;
            panel.ClientSizeChanged += delegate { foreach (var c in cards) c.Width = panel.ClientSize.Width; };
            foreach (var c in cards) c.Width = panel.ClientSize.Width;
            Controls.Add(panel);

            var save = Footer.Add("保存", ButtonKind.Primary, DialogResult.None);
            var cancel = Footer.Add("キャンセル", ButtonKind.Standard, DialogResult.Cancel);
            save.Click += delegate { Accept(); };
            save.Enabled = !empty;
            AcceptButton = save;
            CancelButton = cancel;

            // 左下: すべて補正なしに戻す
            var reset = new FlatButton { Text = "すべてリセット", Kind = ButtonKind.Subtle, Glyph = Glyphs.Refresh, Enabled = !empty };
            reset.SetBounds(pad, (Footer.Height - Theme.S(34)) / 2, reset.PreferredWidth(), Theme.S(34));
            reset.Click += delegate { foreach (var c in cards) c.Reset(); Preview(); };
            Footer.Controls.Add(reset);
        }

        // ---- 「範囲で指定」のガイド ----
        void StartGuide(CorrectionCard card)
        {
            if (guideCard != null && guideCard != card) guideCard.Highlight = -1;
            guideCard = card;
            if (preview != null) previewToggle.Checked = true;  // 実際の画面を見ながら合わせるので、試す表示をオンにする
            panel.ScrollControlIntoView(card);
            ShowGuideStep(1);
        }

        void GuideNext()
        {
            if (guideStep == 1) ShowGuideStep(2);
            else if (guideStep == 2) ShowGuideStep(3);
            else EndGuide();
        }

        void GuideBack()
        {
            if (guideStep == 2) ShowGuideStep(1);
            else EndGuide();
        }

        void ShowGuideStep(int step)
        {
            guideStep = step;
            // 手順 1 は 0%、手順 2 は 100%、完了したら途中の明るさで確かめてもらう
            testSlider.Value = step == 1 ? 0 : step == 2 ? 100 : 50;
            guideCard.Highlight = step == 1 ? 0 : step == 2 ? 1 : -1;
            if (step <= 2) guideCard.FocusRow(step - 1);
            Preview();

            guideNext.Text = step == 1 ? "次へ" : step == 2 ? "完了" : "閉じる";
            guideBack.Text = step == 1 ? "やめる" : "戻る";
            guideNext.Visible = true;
            guideBack.Visible = step <= 2;
            LayoutGuideButtons();
            Invalidate(introRect);
        }

        void EndGuide()
        {
            if (guideCard != null) guideCard.Highlight = -1;
            guideCard = null;
            guideStep = 0;
            guideNext.Visible = guideBack.Visible = false;
            Invalidate(introRect);
        }

        void LayoutGuideButtons()
        {
            int h = Theme.S(34), gap = Theme.S(8);
            int nw = Math.Max(Theme.S(96), guideNext.PreferredWidth()), bw = Math.Max(Theme.S(96), guideBack.PreferredWidth());
            int y = introRect.Bottom - h - Theme.S(12);
            int x = introRect.Right - Theme.S(14) - nw;
            guideNext.SetBounds(x, y, nw, h);
            guideBack.SetBounds(x - gap - bw, y, bw, h);
        }

        /// ガイドの見出しと本文
        void GuideText(out string title, out string body)
        {
            string me = guideCard.Value.Name ?? "このモニター";
            var others = cards.Where(c => c != guideCard).Select(c => c.Value.Name).ToList();
            string other = others.Count > 0 ? string.Join("・", others) : null;
            switch (guideStep)
            {
                case 1:
                    title = "手順 1/2　暗いときを合わせる";
                    body = "確認用を 0% にしました。" + (other != null
                        ? me + " が " + other + " と同じ暗さに見えるまで、「0% のとき」を動かします。\n" +
                          "※ 下げきれないときは、" + other + " の「0% のとき」を上げます。"
                        : me + " がちょうどよい暗さになるまで、「0% のとき」を動かします。");
                    break;
                case 2:
                    title = "手順 2/2　明るいときを合わせる";
                    body = "確認用を 100% にしました。" + (other != null
                        ? me + " が " + other + " と同じ明るさに見えるまで、「100% のとき」を動かします。\n" +
                          "※ 上げきれないときは、" + other + " の「100% のとき」を下げます。"
                        : me + " がちょうどよい明るさになるまで、「100% のとき」を動かします。");
                    break;
                default:
                    title = "これで完了です";
                    body = "暗いときと明るいときを合わせたので、その間の明るさも比例して合います。\n" +
                           "確認用の明るさを動かして途中も確かめ、よければ「保存」を押してください。";
                    break;
            }
        }

        /// 編集中の補正（保存済みのうち、この画面に出ていないモニターの分も含める）
        List<MonitorCorrection> Current()
        {
            var list = cards.Select(c => c.Value.Clone()).ToList();
            foreach (var s in stored)
                if (!list.Any(c => string.Equals(c.Id, s.Id, StringComparison.OrdinalIgnoreCase))) list.Add(s.Clone());
            return list;
        }

        void Preview()
        {
            if (preview == null || !previewToggle.Checked || empty) return;
            Previewed = true;
            preview(testSlider.Value, Current());
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;
            if (guideStep > 0 && guideCard != null)
            {
                // ガイドの案内（左にアクセントの線）
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new RectangleF(introRect.X + 0.5f, introRect.Y + 0.5f, introRect.Width - 1, introRect.Height - 1);
                Theme.FillRound(g, Theme.Surface, r, Theme.S(8));
                Theme.DrawRound(g, Theme.Blend(Theme.Accent, Theme.Surface, 0.4f), r, Theme.S(8), 1f);
                Theme.FillRound(g, Theme.Accent, new RectangleF(introRect.X + Theme.Sf(5), introRect.Y + Theme.Sf(14), Theme.Sf(3), introRect.Height - Theme.Sf(28)), Theme.Sf(1.5f));
                string title, body;
                GuideText(out title, out body);
                int tx = introRect.X + Theme.S(20), tw = guideBack.Visible ? guideBack.Left - tx - Theme.S(12) : guideNext.Left - tx - Theme.S(12);
                if (guideStep == 3) tw = introRect.Right - tx - Theme.S(14);  // 完了のときはボタンが右下だけなので幅を広く
                Theme.DrawText(g, title, Theme.BodySemi, Theme.Fg, new Rectangle(tx, introRect.Y + Theme.S(10), introRect.Right - tx - Theme.S(14), Theme.S(24)), Theme.Left);
                TextRenderer.DrawText(g, body, Theme.Small,
                    new Rectangle(tx, introRect.Y + Theme.S(36), tw, introRect.Height - Theme.S(40)), Theme.Fg, flags);
            }
            else
            {
                string intro = empty
                    ? "明るさを変えられるモニターが見つかりません。"
                    : "同じ明るさでもモニターによって見え方が違うときに、モニターごとに設定する値を調整します。\n" +
                      "方式を切り替えながら、確認用の明るさを動かして見比べてください。\n" +
                      "「範囲で指定」は、各モニターの「ガイドで合わせる」を押すと、手順に沿って合わせられます。";
                TextRenderer.DrawText(g, intro, Theme.Small, new Rectangle(introRect.X, introRect.Y + Theme.S(8), introRect.Width, introRect.Height - Theme.S(8)), Theme.FgDim, flags);
            }
            Theme.DrawText(g, testSlider.Value + "%", Theme.Heading, Theme.Fg, readoutRect, Theme.Left);
        }

        void Accept()
        {
            Result = Current();
            DialogResult = DialogResult.OK;
        }
    }
}
