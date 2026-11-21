// 輝度スケジューラ専用のコントロール（24 時間グラフ・スケジュールのカード・明るさスライダー・2 択の切り替え）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BrightnessScheduler
{
    /// 24 時間の明るさの変化を折れ線（面）グラフで表示
    class BrightnessChart : SmoothControl
    {
        public List<ScheduleEntry> Entries = new List<ScheduleEntry>();

        public BrightnessChart() { Height = Theme.S(110); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, BackColor);
            int labelH = Theme.S(20), axisW = Theme.S(40);
            var plot = new RectangleF(axisW, Theme.S(6), Width - axisW - Theme.S(4), Height - labelH - Theme.S(10));
            if (plot.Width < 20 || plot.Height < 10) return;
            Func<double, float> X = minutes => plot.X + (float)(plot.Width * minutes / 1440.0);
            Func<double, float> Y = pct => plot.Bottom - (float)(plot.Height * pct / 100.0);

            // 背景と目盛り線
            Theme.FillRound(g, Theme.Track, plot, Theme.S(4));
            using (var grid = new Pen(Theme.Blend(Theme.Track, Theme.Surface, 0.5f), 1f))
            {
                foreach (int p in new[] { 50 }) g.DrawLine(grid, plot.X, Y(p), plot.Right, Y(p));
                foreach (int h in new[] { 6, 12, 18 }) g.DrawLine(grid, X(h * 60), plot.Y, X(h * 60), plot.Bottom);
            }
            foreach (int p in new[] { 0, 50, 100 })
                Theme.DrawText(g, p + "%", Theme.Small, Theme.FgDim,
                    new Rectangle(0, (int)Y(p) - Theme.S(10), axisW - Theme.S(8), Theme.S(20)),
                    (Theme.Left & ~TextFormatFlags.Left) | TextFormatFlags.Right);

            if (Entries.Count > 0)
            {
                // 1 分刻みで明るさを計算して面グラフにする
                var pts = new List<PointF>();
                int step = Math.Max(1, (int)(1440 / Math.Max(1f, plot.Width)));
                for (int m = 0; m <= 1440; m += step)
                    pts.Add(new PointF(X(m), Y(Scheduler.TargetAt(Entries, TimeSpan.FromMinutes(Math.Min(m, 1439.99))))));
                using (var area = new GraphicsPath())
                using (var clip = Theme.Round(plot, Theme.S(4)))
                {
                    area.AddLine(plot.X, plot.Bottom, pts[0].X, pts[0].Y);
                    area.AddLines(pts.ToArray());
                    area.AddLine(pts[pts.Count - 1].X, pts[pts.Count - 1].Y, plot.Right, plot.Bottom);
                    area.CloseFigure();
                    g.SetClip(clip);
                    using (var fill = new LinearGradientBrush(plot, Theme.Blend(Theme.Accent, Theme.Track, 0.35f), Theme.Blend(Theme.Accent, Theme.Track, 0.8f), 90f))
                        g.FillPath(fill, area);
                    using (var pen = new Pen(Theme.Accent, Theme.Sf(2)) { LineJoin = LineJoin.Round })
                        g.DrawLines(pen, pts.ToArray());
                    g.ResetClip();
                }
                // 各項目の時刻に点
                using (var dot = new SolidBrush(Theme.Accent))
                using (var ring = new Pen(Theme.Surface, Theme.Sf(2)))
                    foreach (var en in Entries)
                    {
                        float cx = X(en.TimeOfDay.TotalMinutes), cy = Y(en.Brightness), r = Theme.Sf(4.5f);
                        g.FillEllipse(dot, cx - r, cy - r, r * 2, r * 2);
                        g.DrawEllipse(ring, cx - r, cy - r, r * 2, r * 2);
                    }
            }

            // 時刻の目盛り
            int labelY = (int)plot.Bottom + Theme.S(4);
            foreach (int h in new[] { 0, 6, 12, 18, 24 })
            {
                string t = h + ":00";
                var sz = Theme.Measure(t, Theme.Small);
                int x = (int)X(h * 60) - sz.Width / 2;
                x = Math.Max((int)plot.X - Theme.S(4), Math.Min(Width - sz.Width, x));
                Theme.DrawText(g, t, Theme.Small, Theme.FgDim, new Rectangle(x, labelY, sz.Width + 2, labelH), Theme.Left);
            }

            // 現在時刻のマーカー
            float nx = X(DateTime.Now.TimeOfDay.TotalMinutes);
            using (var pen = new Pen(Theme.Fg, Theme.Sf(1.5f)) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, nx, plot.Y, nx, plot.Bottom);
        }
    }

    // ------------------------------------------------------------------
    /// 明るさを表す太陽のアイコン（明るさに応じて光の長さが変わる）
    static class SunIcon
    {
        public static void Draw(Graphics g, RectangleF r, int percent, Color color)
        {
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, s = Math.Min(r.Width, r.Height);
            float core = s * 0.2f;
            float ray = s * (0.08f + 0.2f * percent / 100f);
            using (var b = new SolidBrush(color))
                g.FillEllipse(b, cx - core, cy - core, core * 2, core * 2);
            using (var pen = new Pen(color, Math.Max(1.5f, s * 0.07f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 8; i++)
                {
                    double a = Math.PI * i / 4;
                    float r1 = core + s * 0.08f, r2 = r1 + ray;
                    g.DrawLine(pen, cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1,
                                    cx + (float)Math.Cos(a) * r2, cy + (float)Math.Sin(a) * r2);
                }
        }
    }

    // ------------------------------------------------------------------
    /// スケジュール 1 件分のカード
    class ScheduleCard : SmoothControl
    {
        public readonly ScheduleEntry Entry;
        public bool IsActive;
        public event EventHandler EditClicked, DeleteClicked;
        int hot; // 0: なし 1: 編集 2: 削除

        public ScheduleCard(ScheduleEntry entry)
        {
            Entry = entry;
            Height = Theme.S(72);
            Cursor = Cursors.Hand;
            Margin = new Padding(0, 0, 0, Theme.S(8));
        }

        Rectangle DeleteRect { get { int s = Theme.S(36); return new Rectangle(Width - Theme.S(14) - s, (Height - s) / 2, s, s); } }
        Rectangle EditRect { get { var d = DeleteRect; return new Rectangle(d.X - d.Width - Theme.S(4), d.Y, d.Width, d.Height); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = DeleteRect.Contains(e.Location) ? 2 : EditRect.Contains(e.Location) ? 1 : 0;
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hot = 0; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            if (DeleteRect.Contains(e.Location)) { if (DeleteClicked != null) DeleteClicked(this, EventArgs.Empty); }
            else if (EditClicked != null) EditClicked(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float rad = Theme.S(8);
            Theme.FillRound(g, Theme.Surface, r, rad);
            if (Hover && hot == 0) Theme.FillRound(g, Pressed ? Theme.PressOverlay : Theme.HoverOverlay, r, rad);
            Theme.DrawRound(g, IsActive ? Theme.Blend(Theme.Accent, Theme.Surface, 0.4f) : Theme.Border, r, rad, 1f);
            if (IsActive)
                Theme.FillRound(g, Theme.Accent, new RectangleF(Theme.Sf(4), Height / 2f - Theme.Sf(14), Theme.Sf(3), Theme.Sf(28)), Theme.Sf(1.5f));

            // 時刻
            int x = Theme.S(20);
            var ts = Theme.Measure(Entry.Time, Theme.TimeFont);
            Theme.DrawText(g, Entry.Time, Theme.TimeFont, Theme.Fg, new Rectangle(x, 0, ts.Width + 2, Height), Theme.Left);
            x += Math.Max(ts.Width, Theme.S(70)) + Theme.S(18);

            // 太陽アイコンと明るさ
            int icon = Theme.S(30);
            SunIcon.Draw(g, new RectangleF(x, (Height - icon) / 2f, icon, icon), Entry.Brightness, Theme.Accent);
            x += icon + Theme.S(10);
            string pct = Entry.Brightness + "%";
            Theme.DrawText(g, pct, Theme.Heading, Theme.Fg, new Rectangle(x, Theme.S(10), Theme.S(70), Theme.S(28)), Theme.Left);

            // 明るさのバー
            int right = EditRect.X - Theme.S(16);
            var bar = new RectangleF(x, Theme.S(44), Math.Max(0, Math.Min(Theme.S(200), right - x)), Theme.S(6));
            Theme.FillRound(g, Theme.Track, bar, bar.Height / 2);
            if (Entry.Brightness > 0)
                Theme.FillRound(g, Theme.Accent, new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * Entry.Brightness / 100f), bar.Height), bar.Height / 2);

            // 変化の仕方・表示中バッジ
            int mx = (int)bar.Right + Theme.S(24);
            if (right - mx > Theme.S(60))
            {
                string mode = Entry.Fade ? "前の時刻からなめらかに" : "この時刻に切り替え";
                Theme.DrawText(g, mode, Theme.Small, Theme.FgDim, new Rectangle(mx, Theme.S(36), right - mx, Theme.S(22)), Theme.Left);
                if (IsActive)
                {
                    string badge = "適用中";
                    var bs = Theme.Measure(badge, Theme.Small);
                    var br = new RectangleF(mx, Theme.S(12), bs.Width + Theme.S(16), Theme.S(22));
                    Theme.FillRound(g, Theme.Accent, br, br.Height / 2f);
                    Theme.DrawText(g, badge, Theme.Small, Theme.OnAccent, Rectangle.Round(br), Theme.Center);
                }
            }

            DrawIconButton(g, EditRect, Glyphs.Edit, hot == 1, Theme.Fg);
            DrawIconButton(g, DeleteRect, Glyphs.Delete, hot == 2, Theme.Danger);
        }

        void DrawIconButton(Graphics g, Rectangle r, string glyph, bool isHot, Color hotColor)
        {
            if (isHot) Theme.FillRound(g, Pressed ? Theme.PressOverlay : Theme.HoverOverlay, r, Theme.S(5));
            Theme.DrawText(g, glyph, Theme.IconFont, isHot ? hotColor : Theme.FgDim, r, Theme.Center);
        }
    }

    // ------------------------------------------------------------------
    /// 0〜100% の明るさスライダー（ドラッグ・クリック・ホイール・矢印キー）
    class BrightnessSlider : SmoothControl
    {
        int value = 50;
        bool dragging;
        /// 値が変わるたび
        public event EventHandler ValueChanged;
        /// ドラッグを終えたとき・キーやホイールで変えたとき（プレビューに使う）
        public event EventHandler ValueCommitted;

        public BrightnessSlider()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Height = Theme.S(36);
        }

        /// 範囲（既定は 0〜100）。Value より先に設定する
        public int Minimum = 0, Maximum = 100;
        /// 色を塗り始める値（既定は Minimum。ずらす量のように 0 を中心にしたいときに使う）
        public int? FillFrom;

        public int Value
        {
            get { return value; }
            set
            {
                int v = Math.Max(Minimum, Math.Min(Maximum, value));
                if (v == this.value) return;
                this.value = v;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        void Commit() { if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty); }

        RectangleF Track { get { float k = Theme.Sf(11); return new RectangleF(k, Height / 2f - Theme.Sf(3), Width - k * 2, Theme.Sf(6)); } }

        int ValueAt(int x)
        {
            var t = Track;
            return Minimum + (int)Math.Round((x - t.X) / t.Width * (Maximum - Minimum));
        }

        float XOf(int v)
        {
            var t = Track;
            return t.X + t.Width * (v - Minimum) / Math.Max(1f, Maximum - Minimum);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            Value = ValueAt(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Value = ValueAt(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging) { dragging = false; Commit(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value += e.Delta > 0 ? 5 : -5;
            Commit();
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right: case Keys.Home: case Keys.End: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int step = e.Shift ? 10 : 1;
            switch (e.KeyCode)
            {
                case Keys.Up: case Keys.Right: Value += step; break;
                case Keys.Down: case Keys.Left: Value -= step; break;
                case Keys.PageUp: Value += 10; break;
                case Keys.PageDown: Value -= 10; break;
                case Keys.Home: Value = Minimum; break;
                case Keys.End: Value = Maximum; break;
                default: return;
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            base.OnKeyUp(e);
            switch (e.KeyCode)
            {
                case Keys.Up: case Keys.Right: case Keys.Down: case Keys.Left:
                case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: Commit(); break;
            }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, BackColor);
            var t = Track;
            Theme.FillRound(g, Theme.Track, t, t.Height / 2);
            float kx = XOf(value);
            float fx = XOf(FillFrom ?? Minimum);
            float x1 = Math.Min(fx, kx), x2 = Math.Max(fx, kx);
            if (x2 - x1 > 0.5f || !FillFrom.HasValue)
                Theme.FillRound(g, Hover || dragging ? Theme.AccentHover : Theme.Accent,
                    new RectangleF(x1, t.Y, x2 - x1 + (FillFrom.HasValue ? 0 : t.Height / 2), t.Height), t.Height / 2);
            if (FillFrom.HasValue)  // 中心の目印
                using (var pen = new Pen(Theme.FgDim, Theme.Sf(1.5f))) g.DrawLine(pen, fx, t.Y - Theme.Sf(5), fx, t.Bottom + Theme.Sf(5));
            float outer = Theme.Sf(10), inner = Theme.Sf(Hover || dragging ? 6 : 5);
            using (var b = new SolidBrush(Theme.Surface)) g.FillEllipse(b, kx - outer, Height / 2f - outer, outer * 2, outer * 2);
            using (var pen = new Pen(Focused ? Theme.Accent : Theme.Border, 1f)) g.DrawEllipse(pen, kx - outer, Height / 2f - outer, outer * 2, outer * 2);
            using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, kx - inner, Height / 2f - inner, inner * 2, inner * 2);
        }
    }

    // ------------------------------------------------------------------
    /// 2 つの選択肢から 1 つを選ぶ（説明つきの大きめのボタン 2 つ）
    class ChoiceCards : SmoothControl
    {
        public string[] Titles = new string[2], Descriptions = new string[2];
        int selected, hot = -1;
        public event EventHandler SelectedChanged;

        public ChoiceCards() { Cursor = Cursors.Hand; }

        public int Selected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); }
        }

        Rectangle Item(int i)
        {
            int gap = Theme.S(8), w = (Width - gap) / 2;
            return new Rectangle(i * (w + gap), 0, w, Height);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = Item(0).Contains(e.Location) ? 0 : Item(1).Contains(e.Location) ? 1 : -1;
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            for (int i = 0; i < 2; i++) if (Item(i).Contains(e.Location)) Selected = i;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, BackColor);
            for (int i = 0; i < 2; i++)
            {
                var it = Item(i);
                var r = new RectangleF(it.X + 0.5f, it.Y + 0.5f, it.Width - 1, it.Height - 1);
                bool sel = i == selected;
                Theme.FillRound(g, Theme.Surface, r, Theme.S(8));
                if (hot == i && !sel) Theme.FillRound(g, Theme.HoverOverlay, r, Theme.S(8));
                if (sel) Theme.DrawRound(g, Theme.Accent, new RectangleF(it.X + 1, it.Y + 1, it.Width - 2, it.Height - 2), Theme.S(8), Theme.Sf(2));
                else Theme.DrawRound(g, Theme.Border, r, Theme.S(8), 1f);

                // ラジオボタン風の丸
                float cr = Theme.Sf(8), cx = it.X + Theme.S(22), cy = it.Y + Theme.S(24);
                using (var pen = new Pen(sel ? Theme.Accent : Theme.FgDim, Theme.Sf(1.5f)))
                    g.DrawEllipse(pen, cx - cr, cy - cr, cr * 2, cr * 2);
                if (sel) using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, cx - cr / 2, cy - cr / 2, cr, cr);

                int tx = it.X + Theme.S(40), tw = it.Width - Theme.S(52);
                Theme.DrawText(g, Titles[i], Theme.BodySemi, Theme.Fg, new Rectangle(tx, it.Y + Theme.S(12), tw, Theme.S(24)), Theme.Left);
                TextRenderer.DrawText(g, Descriptions[i], Theme.Small, new Rectangle(tx, it.Y + Theme.S(38), tw, it.Height - Theme.S(44)), Theme.FgDim,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top);
            }
        }
    }
}
