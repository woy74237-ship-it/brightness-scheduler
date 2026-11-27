// 紹介ページ・記事・SNS 用の画像（tools\make-promo.ps1 から MakeIcon.cs と一緒にコンパイルして実行）
//   --cover <out.png> <幅> <高さ>   … 昼から夜へ暗くなっていく空（ページの背景など、文字なし）
//   --ogp   <out.png> <幅> <高さ>   … アイコン＋アプリ名＋キャッチコピー（シェア時のサムネイル・note の見出し画像）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

partial class MakeIcon
{
    static partial void RunPromo(string[] args, ref bool handled)
    {
        if (args.Length < 4 || (args[0] != "--cover" && args[0] != "--ogp")) return;
        handled = true;
        int w = int.Parse(args[2]), h = int.Parse(args[3]);
        using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                DrawDimmingSky(g, w, h);
                // 5 つ目・6 つ目の引数で、見出しとキャッチコピーを変えられる（記事ごとの見出し画像用）
                if (args[0] == "--ogp") DrawOgpCard(g, w, h, args.Length > 4 ? args[4] : "輝度スケジューラ", args.Length > 5 ? args[5] : "時刻で、画面の明るさが自動で変わる。");
            }
            bmp.Save(args[1], ImageFormat.Png);
        }
    }

    // 左（昼）は明るい空色、右（夜）へ向かって暗い紺色に
    static readonly Color[] SkyColors = { C(0x8E, 0xD1, 0xFF), C(0x5E, 0xA6, 0xF2), C(0x6A, 0x63, 0xD6), C(0x2E, 0x2C, 0x7A), C(0x12, 0x14, 0x3C) };
    static readonly float[] SkyPositions = { 0f, 0.28f, 0.55f, 0.78f, 1f };

    /// 昼から夜へ暗くなっていく空に、アプリのグラフのような明るさの曲線を重ねる
    static void DrawDimmingSky(Graphics g, int w, int h)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var area = new Rectangle(-2, -2, w + 4, h + 4);
        using (var sky = new LinearGradientBrush(area, Color.Black, Color.Black, 0f))
        {
            sky.InterpolationColors = new ColorBlend { Colors = SkyColors, Positions = SkyPositions };
            g.FillRectangle(sky, area);
        }
        using (var depth = new LinearGradientBrush(area, Color.FromArgb(40, 10, 10, 40), Color.FromArgb(0, 255, 255, 255), 90f))
            g.FillRectangle(depth, area);

        float u = Math.Min(w, h * 1.8f);
        // 星（夜側）
        var rnd = new Random(11);
        for (int i = 0; i < 70; i++)
        {
            float x = (float)(0.62 + rnd.NextDouble() * 0.38), y = (float)(rnd.NextDouble() * 0.55);
            float r = (float)(0.0015 + rnd.NextDouble() * 0.003) * u;
            int alpha = (int)(255 * Math.Min(1.0, (x - 0.62) / 0.2) * (0.5 + rnd.NextDouble() * 0.5));
            using (var b = new SolidBrush(Color.FromArgb(alpha, 255, 255, 240)))
                g.FillEllipse(b, x * w - r, y * h - r, r * 2, r * 2);
        }
        // 太陽（昼側）と光
        float sx = w * 0.16f, sy = h * 0.30f, sr = u * 0.07f;
        using (var gp = new GraphicsPath())
        {
            gp.AddEllipse(sx - sr * 2.6f, sy - sr * 2.6f, sr * 5.2f, sr * 5.2f);
            using (var glow = new PathGradientBrush(gp))
            {
                glow.CenterColor = Color.FromArgb(150, 0xFF, 0xF1, 0xB8);
                glow.SurroundColors = new[] { Color.FromArgb(0, 0xFF, 0xF1, 0xB8) };
                g.FillPath(glow, gp);
            }
        }
        using (var sun = new SolidBrush(C(0xFF, 0xDF, 0x7E))) g.FillEllipse(sun, sx - sr, sy - sr, sr * 2, sr * 2);
        // 月（夜側、半分だけ照らされた円で「暗くした太陽」のように）
        float mx = w * 0.86f, my = h * 0.26f, mr = u * 0.045f;
        using (var moon = new SolidBrush(C(0xF4, 0xF1, 0xD0))) g.FillEllipse(moon, mx - mr, my - mr, mr * 2, mr * 2);
        using (var half = new GraphicsPath())
        using (var shade = new SolidBrush(Color.FromArgb(170, 0x12, 0x14, 0x3C)))
        {
            half.AddPie(mx - mr, my - mr, mr * 2, mr * 2, -45, 180);
            g.FillPath(shade, half);
        }

        // 明るさの曲線（朝に上がり、昼に最も明るく、夜へなめらかに下がる）
        var pts = new PointF[121];
        for (int i = 0; i <= 120; i++)
        {
            double t = i / 120.0;
            double v = t < 0.22 ? Smooth(t / 0.22) * 0.85 : 0.85 - Smooth((t - 0.22) / 0.78) * 0.6;  // 0〜1 の明るさ
            pts[i] = new PointF((float)(t * w), (float)(h * (0.92 - v * 0.5)));
        }
        using (var fill = new GraphicsPath())
        {
            fill.AddLine(0, h + 2, pts[0].X, pts[0].Y);
            fill.AddLines(pts);
            fill.AddLine(pts[pts.Length - 1].X, pts[pts.Length - 1].Y, w, h + 2);
            fill.CloseFigure();
            using (var b = new LinearGradientBrush(new RectangleF(0, h * 0.4f, w, h * 0.6f), Color.FromArgb(70, 255, 255, 255), Color.FromArgb(10, 255, 255, 255), 90f))
                g.FillPath(b, fill);
        }
        using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), Math.Max(2f, u * 0.004f)) { LineJoin = LineJoin.Round })
            g.DrawLines(pen, pts);
    }

    static double Smooth(double f) { f = Math.Max(0, Math.Min(1, f)); return f * f * (3 - 2 * f); }

    static void DrawOgpCard(Graphics g, int w, int h, string titleText, string catchText)
    {
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        float k = h / 630f;  // 1200x630 を基準に拡大縮小
        var card = new RectangleF(80 * k, 95 * k, w - 160 * k, h - 190 * k);
        using (var shadowPath = Round(new RectangleF(card.X, card.Y + 12 * k, card.Width, card.Height), 36 * k))
        using (var shadow = new SolidBrush(Color.FromArgb(60, 10, 10, 40)))
            g.FillPath(shadow, shadowPath);
        using (var cardPath = Round(card, 36 * k))
        using (var white = new SolidBrush(C(0xFA, 0xFA, 0xFD)))
            g.FillPath(white, cardPath);

        int iconSize = (int)(240 * k);
        using (var icon = Draw(iconSize))
            g.DrawImage(icon, card.X + 60 * k, card.Y + (card.Height - iconSize) / 2f, iconSize, iconSize);

        float x = card.X + 60 * k + iconSize + 56 * k;
        var fmt = StringFormat.GenericTypographic;
        using (var title = new Font("Yu Gothic UI Semibold", 64 * k, GraphicsUnit.Pixel))
        using (var body = new Font("Yu Gothic UI", 30 * k, GraphicsUnit.Pixel))
        using (var chip = new Font("Yu Gothic UI Semibold", 22 * k, GraphicsUnit.Pixel))
        using (var small = new Font("Yu Gothic UI", 22 * k, GraphicsUnit.Pixel))
        using (var ink = new SolidBrush(C(0x1B, 0x1F, 0x4A)))
        using (var sub = new SolidBrush(C(0x4A, 0x4F, 0x6A)))
        using (var dim = new SolidBrush(C(0x8A, 0x8F, 0xA8)))
        using (var chipBg = new SolidBrush(C(0xEC, 0xEE, 0xFB)))
        using (var chipFg = new SolidBrush(C(0x2E, 0x2C, 0x7A)))
        {
            float y = card.Y + (card.Height - 268 * k) / 2f;
            g.DrawString(titleText, title, ink, x, y, fmt);
            y += 90 * k;
            g.DrawString(catchText, body, sub, x, y, fmt);
            y += 70 * k;
            float cx = x;
            foreach (var label in new[] { "Windows 10 / 11", "無料", "インストール不要" })
            {
                var size = g.MeasureString(label, chip, PointF.Empty, fmt);
                var pill = new RectangleF(cx, y, size.Width + 36 * k, 44 * k);
                using (var pp = Round(pill, 22 * k)) g.FillPath(chipBg, pp);
                g.DrawString(label, chip, chipFg, cx + 18 * k, y + (44 * k - size.Height) / 2f, fmt);
                cx = pill.Right + 12 * k;
            }
            y += 44 * k + 34 * k;
            g.DrawString("by Walt-Owen", small, dim, x, y, fmt);
        }
    }
}
