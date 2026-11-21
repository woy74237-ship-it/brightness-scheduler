// 自前描画のコントロール群（ボタン・トグル・ドロップダウン・時刻選択・ダイアログ下部のボタン帯）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace BrightnessScheduler
{
    class SmoothControl : Control
    {
        protected bool Hover, Pressed;
        protected Point MousePos = new Point(-1, -1);

        public SmoothControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
        }

        protected Graphics Begin(PaintEventArgs e, Color background)
        {
            var g = e.Graphics;
            g.Clear(background);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            return g;
        }

        protected Color ParentBack { get { return Parent != null ? Parent.BackColor : Theme.Bg; } }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Pressed = false; MousePos = new Point(-1, -1); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e) { MousePos = e.Location; base.OnMouseMove(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    }

    // ------------------------------------------------------------------
    enum ButtonKind { Standard, Primary, Subtle, Danger }

    class FlatButton : SmoothControl, IButtonControl
    {
        public string Glyph { get; set; }
        public ButtonKind Kind { get; set; }
        public DialogResult DialogResult { get; set; }
        /// 文字色を指定したいとき（Subtle・Standard のみ。空なら通常の色）
        public Color TextColorOverride { get; set; }

        public FlatButton()
        {
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            Size = new Size(Theme.S(110), Theme.S(34));
        }

        public void NotifyDefault(bool value) { }
        public void PerformClick() { if (Enabled) OnClick(EventArgs.Empty); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (DialogResult != DialogResult.None)
            {
                var f = FindForm();
                if (f != null) f.DialogResult = DialogResult;
            }
        }

        public int PreferredWidth()
        {
            int tw = Theme.Measure(Text, Font).Width;
            int gw = string.IsNullOrEmpty(Glyph) ? 0 : Theme.S(16);
            int gap = gw > 0 && tw > 0 ? Theme.S(8) : 0;
            return Math.Max(Theme.S(36), tw + gw + gap + Theme.S(36));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float rad = Theme.S(5);
            Color fg = Theme.Fg;
            switch (Kind)
            {
                case ButtonKind.Primary:
                    Theme.FillRound(g, !Enabled ? Theme.Track : Pressed ? Theme.AccentPressed : Hover ? Theme.AccentHover : Theme.Accent, r, rad);
                    fg = Theme.OnAccent;
                    break;
                case ButtonKind.Danger:
                    Theme.FillRound(g, Pressed || Hover ? Theme.DangerHover : Theme.Danger, r, rad);
                    fg = Theme.OnDanger;
                    break;
                case ButtonKind.Standard:
                    Theme.FillRound(g, Theme.Surface, r, rad);
                    if (Hover) Theme.FillRound(g, Pressed ? Theme.PressOverlay : Theme.HoverOverlay, r, rad);
                    Theme.DrawRound(g, Theme.Border, r, rad, 1f);
                    break;
                case ButtonKind.Subtle:
                    if (Hover) Theme.FillRound(g, Pressed ? Theme.PressOverlay : Theme.HoverOverlay, r, rad);
                    break;
            }
            if (!TextColorOverride.IsEmpty && (Kind == ButtonKind.Subtle || Kind == ButtonKind.Standard)) fg = TextColorOverride;
            if (!Enabled) fg = Theme.FgDim;
            if (Pressed && Kind != ButtonKind.Primary && Kind != ButtonKind.Danger) fg = Theme.FgDim;

            int tw = Theme.Measure(Text, Font).Width;
            int gw = string.IsNullOrEmpty(Glyph) ? 0 : Theme.S(16);
            int gap = gw > 0 && tw > 0 ? Theme.S(8) : 0;
            int x = (Width - (gw + gap + tw)) / 2;
            if (gw > 0) Theme.DrawText(g, Glyph, Theme.IconFont, fg, new Rectangle(x, 0, gw, Height), Theme.Center);
            if (tw > 0) Theme.DrawText(g, Text, Font, fg, new Rectangle(x + gw + gap, 0, tw + 2, Height), Theme.Center);
        }
    }

    // ------------------------------------------------------------------
    class ToggleSwitch : SmoothControl
    {
        bool on;
        public event EventHandler CheckedChanged;

        public ToggleSwitch()
        {
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            Height = Theme.S(34);
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        /// true: ラベルの後ろに今の状態（オン／オフ）を表示する
        public bool ShowState { get; set; }

        const string OnText = "オン", OffText = "オフ";

        Font StateFont { get { return Font.Size >= Theme.Body.Size ? Theme.BodySemi : Font; } }

        int StateWidth
        {
            get
            {
                if (!ShowState) return 0;
                return Theme.S(8) + Math.Max(Theme.Measure(OnText, StateFont).Width, Theme.Measure(OffText, StateFont).Width);
            }
        }

        public void FitWidth()
        {
            // オン／オフで幅が変わらないよう、長いほうに合わせる
            Width = Theme.S(40) + Theme.S(12) + Theme.Measure(Text, Font).Width + StateWidth + Theme.S(4);
        }

        protected override void OnClick(EventArgs e)
        {
            Checked = !Checked;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, BackColor);
            int tw = Theme.S(40), th = Theme.S(20), ty = (Height - th) / 2;
            var track = new RectangleF(0.5f, ty + 0.5f, tw - 1, th - 1);
            float kd = Theme.Sf(Hover ? 14 : 12);
            float cx;
            if (on)
            {
                Theme.FillRound(g, Hover ? Theme.AccentHover : Theme.Accent, track, th / 2f);
                cx = tw - th / 2f;
                using (var b = new SolidBrush(Theme.OnAccent))
                    g.FillEllipse(b, cx - kd / 2, ty + th / 2f - kd / 2, kd, kd);
            }
            else
            {
                if (Hover) Theme.FillRound(g, Theme.HoverOverlay, track, th / 2f);
                Theme.DrawRound(g, Theme.FgDim, track, th / 2f, Math.Max(1f, Theme.Scale));
                cx = th / 2f;
                using (var b = new SolidBrush(Theme.FgDim))
                    g.FillEllipse(b, cx - kd / 2, ty + th / 2f - kd / 2, kd, kd);
            }
            int lx = tw + Theme.S(12);
            Theme.DrawText(g, Text, Font, Theme.Fg, new Rectangle(lx, 0, Width - lx - StateWidth, Height), Theme.Left);
            if (ShowState)
            {
                int sx = lx + Theme.Measure(Text, Font).Width + Theme.S(8);
                Theme.DrawText(g, on ? OnText : OffText, StateFont, on ? Theme.Fg : Theme.FgDim,
                    new Rectangle(sx, 0, Math.Max(0, Width - sx), Height), Theme.Left);
            }
        }
    }

    // ------------------------------------------------------------------
    class DropdownButton : SmoothControl
    {
        string[] items = new string[0];
        int selected = -1;
        public event EventHandler SelectedIndexChanged;

        public DropdownButton()
        {
            Cursor = Cursors.Hand;
            Font = Theme.Body;
        }

        public string[] Items { get { return items; } set { items = value ?? new string[0]; Invalidate(); } }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (selected == value) return;
                selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var menu = new ContextMenuStrip();
            Theme.StyleMenu(menu);
            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                var item = Theme.MenuItem(items[i], delegate { SelectedIndex = index; });
                item.Checked = i == selected;
                menu.Items.Add(item);
            }
            menu.MinimumSize = new Size(Width, 0);
            menu.Closed += delegate { BeginInvoke((MethodInvoker)delegate { menu.Dispose(); }); };
            menu.Show(this, new Point(0, Height + Theme.S(4)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            float rad = Theme.S(5);
            Theme.FillRound(g, Theme.Surface, r, rad);
            if (Hover) Theme.FillRound(g, Pressed ? Theme.PressOverlay : Theme.HoverOverlay, r, rad);
            Theme.DrawRound(g, Theme.Border, r, rad, 1f);
            string text = selected >= 0 && selected < items.Length ? items[selected] : "";
            int arrow = Theme.S(36);
            Theme.DrawText(g, text, Font, Theme.Fg, new Rectangle(Theme.S(12), 0, Width - Theme.S(12) - arrow, Height), Theme.Left);
            Theme.DrawText(g, Glyphs.ChevronDown, Theme.IconSmall, Theme.FgDim, new Rectangle(Width - arrow, 0, arrow - Theme.S(4), Height), Theme.Center);
        }
    }



    // ------------------------------------------------------------------
    /// 時・分を大きく表示し、矢印・ホイール・キー入力で変更できる時刻選択
    class TimeSelector : SmoothControl
    {
        int hour = 7, minute, part;
        string buffer = "";
        public event EventHandler ValueChanged;

        public TimeSelector()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Size = new Size(Theme.S(180), Theme.S(136));
        }

        public TimeSpan Value
        {
            get { return new TimeSpan(hour, minute, 0); }
            set { hour = value.Hours; minute = value.Minutes; buffer = ""; Changed(); }
        }

        Rectangle Box(int p) { int bw = Theme.S(76); return new Rectangle(p == 0 ? 0 : Width - bw, Theme.S(36), bw, Theme.S(64)); }
        Rectangle Up(int p) { var b = Box(p); return new Rectangle(b.X, 0, b.Width, Theme.S(30)); }
        Rectangle Down(int p) { var b = Box(p); return new Rectangle(b.X, b.Bottom + Theme.S(6), b.Width, Theme.S(30)); }

        void Changed()
        {
            Invalidate();
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        void Step(int p, int dir, bool coarse)
        {
            if (p == 0) hour = (hour + dir + 24) % 24;
            else if (coarse)
            {
                int m = dir > 0 ? (minute / 5 + 1) * 5 : ((minute + 4) / 5 - 1) * 5;
                minute = ((m % 60) + 60) % 60;
            }
            else minute = (minute + dir + 60) % 60;
            buffer = "";
            Changed();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            for (int p = 0; p < 2; p++)
            {
                if (Up(p).Contains(e.Location)) { part = p; Step(p, 1, true); return; }
                if (Down(p).Contains(e.Location)) { part = p; Step(p, -1, true); return; }
                if (Box(p).Contains(e.Location)) { part = p; buffer = ""; Invalidate(); return; }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int p = e.X > Width / 2 ? 1 : 0;
            part = p;
            Step(p, e.Delta > 0 ? 1 : -1, true);
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.Left: case Keys.Right: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Up: Step(part, 1, false); break;
                case Keys.Down: Step(part, -1, false); break;
                case Keys.Left: part = 0; buffer = ""; Invalidate(); break;
                case Keys.Right: part = 1; buffer = ""; Invalidate(); break;
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (!char.IsDigit(e.KeyChar)) return;
            int max = part == 0 ? 23 : 59;
            int d = e.KeyChar - '0';
            if (buffer.Length == 0)
            {
                SetPart(d);
                // 2 桁目が来ても範囲外になる数字なら、その場で確定して次へ
                if (d * 10 > max) { buffer = ""; if (part == 0) part = 1; }
                else buffer = d.ToString();
            }
            else
            {
                SetPart(Math.Min(max, int.Parse(buffer) * 10 + d));
                buffer = "";
                if (part == 0) part = 1;
            }
            Changed();
            e.Handled = true;
        }

        void SetPart(int v) { if (part == 0) hour = v; else minute = v; }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); buffer = ""; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, BackColor);
            for (int p = 0; p < 2; p++)
            {
                var b = Box(p);
                var rf = new RectangleF(b.X + 0.5f, b.Y + 0.5f, b.Width - 1, b.Height - 1);
                Theme.FillRound(g, Theme.Surface, rf, Theme.S(8));
                bool focused = Focused && part == p;
                if (focused)
                    Theme.DrawRound(g, Theme.Accent, new RectangleF(b.X + 1, b.Y + 1, b.Width - 2, b.Height - 2), Theme.S(8), Theme.Sf(2));
                else
                    Theme.DrawRound(g, Theme.Border, rf, Theme.S(8), 1f);
                string text = (p == 0 ? hour : minute).ToString("00");
                Theme.DrawText(g, text, Theme.TimeBig, Theme.Fg, b, Theme.Center);

                DrawArrow(g, Up(p), Glyphs.ChevronUp);
                DrawArrow(g, Down(p), Glyphs.ChevronDown);
            }
            var b0 = Box(0); var b1 = Box(1);
            Theme.DrawText(g, ":", Theme.TimeBig, Theme.FgDim, new Rectangle(b0.Right, b0.Y - Theme.S(4), b1.X - b0.Right, b0.Height), Theme.Center);
        }

        void DrawArrow(Graphics g, Rectangle r, string glyph)
        {
            bool hot = r.Contains(MousePos);
            if (hot) Theme.FillRound(g, Pressed ? Theme.PressOverlay : Theme.HoverOverlay, r, Theme.S(5));
            Theme.DrawText(g, glyph, Theme.IconSmall, hot ? Theme.Fg : Theme.FgDim, r, Theme.Center);
        }
    }

    // ------------------------------------------------------------------
    /// ダイアログ下部のボタン帯
    class FooterBand : SmoothControl
    {
        public readonly List<FlatButton> Buttons = new List<FlatButton>();

        public FooterBand()
        {
            BackColor = Theme.Surface;
            Height = Theme.S(76);
        }

        public FlatButton Add(string text, ButtonKind kind, DialogResult result, string glyph = null)
        {
            var b = new FlatButton { Text = text, Kind = kind, DialogResult = result, Glyph = glyph };
            Buttons.Add(b);
            Controls.Add(b);
            PerformLayout();
            return b;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int pad = Theme.S(24), gap = Theme.S(8), h = Theme.S(34);
            int total = 0;
            foreach (var b in Buttons) { b.Width = Math.Max(Theme.S(120), b.PreferredWidth()); total += b.Width + gap; }
            int x = Width - pad - (total - gap);
            foreach (var b in Buttons)
            {
                b.SetBounds(x, (Height - h) / 2, b.Width, h);
                x += b.Width + gap;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Begin(e, Theme.Surface);
            using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, 0, 0, Width, 0);
        }
    }
}
