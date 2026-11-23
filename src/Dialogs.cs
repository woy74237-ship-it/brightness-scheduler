// 追加・編集ダイアログと確認ダイアログ
using System;
using System.Drawing;
using System.Globalization;

using System.Windows.Forms;

namespace BrightnessScheduler
{
    class ThemedDialog : Form
    {
        protected readonly FooterBand Footer;

        public ThemedDialog(string title, int width, int height)
        {
            Text = title;
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Body;
            BackColor = Theme.Bg;
            ForeColor = Theme.Fg;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;
            ClientSize = new Size(Theme.S(width), Theme.S(height));
            Footer = new FooterBand { Dock = DockStyle.Bottom };
            Controls.Add(Footer);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.ApplyWindow(this);
        }
    }

    class AboutDialog : ThemedDialog
    {
        readonly Icon bigIcon;

        public AboutDialog() : base("このアプリについて", 520, AppInfo.HasSupport ? 286 : 250)
        {
            bigIcon = Theme.LoadAppIcon(new Size(Theme.S(72), Theme.S(72)));
            if (AppInfo.HasSupport)
            {
                var support = Footer.Add("開発を支援する", ButtonKind.Primary, DialogResult.None, Glyphs.Heart);
                support.Click += delegate { AppInfo.ShowSupportMenu(support); };
            }
            if (AppInfo.HasWebsite)
            {
                var web = Footer.Add("公式ページ", ButtonKind.Standard, DialogResult.None, Glyphs.OpenInNew);
                web.Click += delegate { AppInfo.Open(AppInfo.WebsiteUrl); };
            }
            var close = Footer.Add("閉じる", ButtonKind.Standard, DialogResult.Cancel);
            AcceptButton = close;
            CancelButton = close;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            int pad = Theme.S(24), iconSize = Theme.S(72);
            g.DrawIcon(bigIcon, new Rectangle(pad, pad, iconSize, iconSize));

            int x = pad + iconSize + Theme.S(20), w = ClientSize.Width - x - pad, y = pad - Theme.S(2);
            Theme.DrawText(g, AppInfo.Name, Theme.Title, Theme.Fg, new Rectangle(x, y, w, Theme.S(36)), Theme.Left);
            y += Theme.S(38);
            Theme.DrawText(g, "バージョン " + AppInfo.Version, Theme.Body, Theme.FgDim, new Rectangle(x, y, w, Theme.S(22)), Theme.Left);
            y += Theme.S(32);
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top;
            TextRenderer.DrawText(g, "指定した時刻にディスプレイの明るさを自動で変えるアプリです。",
                Theme.Body, new Rectangle(x, y, w, Theme.S(48)), Theme.Fg, flags);
            if (AppInfo.HasSupport)
            {
                y += Theme.S(34);
                TextRenderer.DrawText(g, "気に入っていただけたら、開発を支援していただけるとうれしいです。",
                    Theme.Small, new Rectangle(x, y, w, Theme.S(44)), Theme.FgDim, flags);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            bigIcon.Dispose();
            base.OnFormClosed(e);
        }

        /// 親ウィンドウがあればその中央、なければ画面中央に表示
        public static void ShowFor(IWin32Window owner)
        {
            using (var d = new AboutDialog())
            {
                if (owner == null) d.StartPosition = FormStartPosition.CenterScreen;
                d.ShowDialog(owner);
            }
        }
    }

    class ConfirmDialog : ThemedDialog
    {
        readonly string message;

        public ConfirmDialog(string title, string message, string okText) : base(title, 420, 196)
        {
            this.message = message;
            Footer.Add(okText, ButtonKind.Danger, DialogResult.OK);
            var cancel = Footer.Add("キャンセル", ButtonKind.Standard, DialogResult.Cancel);
            AcceptButton = cancel;
            CancelButton = cancel;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int pad = Theme.S(24);
            var r = new Rectangle(pad, pad, ClientSize.Width - pad * 2, ClientSize.Height - Footer.Height - pad * 2);
            TextRenderer.DrawText(e.Graphics, message, Theme.Body, r, Theme.Fg,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top);
        }
    }
}
