// 画面を暗くするフィルターの濃さを、実際の画面を見ながら調整する小さな画面
using System;
using System.Drawing;
using System.Windows.Forms;

namespace BrightnessScheduler
{
    class FilterDialog : ThemedDialog
    {
        readonly BrightnessSlider slider;
        readonly Rectangle introRect, readoutRect;
        readonly Action<int> preview;

        public int Level { get { return slider.Value; } }

        /// preview: 濃さが変わるたびに呼ばれる（その場でフィルターの濃さを変える）
        public FilterDialog(int level, Action<int> preview) : base("フィルターの濃さ", 520, 256)
        {
            this.preview = preview;
            int pad = Theme.S(24);
            introRect = new Rectangle(pad, pad, ClientSize.Width - pad * 2, Theme.S(44));
            readoutRect = new Rectangle(ClientSize.Width - pad - Theme.S(76), introRect.Bottom + Theme.S(14), Theme.S(76), Theme.S(40));

            slider = new BrightnessSlider { Minimum = Settings.FilterMin, Maximum = Settings.FilterMax };
            slider.Value = level;
            slider.SetBounds(pad - Theme.S(4), readoutRect.Y + Theme.S(2), readoutRect.X - pad - Theme.S(8), Theme.S(36));
            slider.ValueChanged += delegate { Invalidate(readoutRect); if (this.preview != null) this.preview(slider.Value); };
            Controls.Add(slider);

            var close = Footer.Add("閉じる", ButtonKind.Primary, DialogResult.OK);
            AcceptButton = close;
            CancelButton = close;
            Shown += delegate { slider.Focus(); if (this.preview != null) this.preview(slider.Value); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            TextRenderer.DrawText(g, "スライダーを動かすと、その場で画面の暗さが変わります。\n濃くするほど暗くなります（10〜90%）。決めた濃さは保存されます。",
                Theme.Small, introRect, Theme.FgDim,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top);
            Theme.DrawText(g, slider.Value + "%", Theme.Heading, Theme.Fg, readoutRect, Theme.Left);
        }
    }
}
