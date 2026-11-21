// アプリアイコン (assets\app.ico) を生成するツール。tools\make-icon.ps1 から実行します。
// 小さいサイズは BMP 形式、256px は PNG 形式で格納した複数サイズの .ico を出力します。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

partial class MakeIcon
{
    /// 宣伝用の画像（tools\Promo.cs）。make-icon.ps1 では Promo.cs を含めないので、何もしない
    static partial void RunPromo(string[] args, ref bool handled);

    static void Main(string[] args)
    {
        bool handled = false;
        RunPromo(args, ref handled);
        if (handled) return;
        if (args.Length < 1) { Console.Error.WriteLine("usage: MakeIcon <out.ico> [preview.png]"); Environment.Exit(1); }
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
        var images = new List<byte[]>();
        foreach (int s in sizes)
            using (var bmp = Draw(s))
                images.Add(s >= 256 ? Png(bmp) : Dib(bmp));

        using (var fs = File.Create(args[0]))
        using (var w = new BinaryWriter(fs))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                byte dim = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
                w.Write(dim); w.Write(dim); w.Write((byte)0); w.Write((byte)0);
                w.Write((short)1); w.Write((short)32);
                w.Write(images[i].Length); w.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) w.Write(img);
        }
        if (args.Length > 1)
            using (var b = Draw(256)) b.Save(args[1], ImageFormat.Png);
    }

    static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// 昼（左上・明るい空色）から夜（右下・紺）へ移る角丸タイルに、半分が欠けた太陽（明るさ）を描く
    static Bitmap Draw(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            float m = s * (s <= 24 ? 0.03f : 0.06f);
            var tile = new RectangleF(m, m, s - 2 * m, s - 2 * m);
            using (var path = Round(tile, s * 0.2f))
            using (var bg = new LinearGradientBrush(tile, Color.Black, Color.Black, 45f))
            {
                bg.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { C(0x4F, 0xA8, 0xF0), C(0x5B, 0x5F, 0xD6), C(0x1C, 0x1F, 0x5E) },
                    Positions = new[] { 0f, 0.5f, 1f }
                };
                g.FillPath(bg, path);
            }

            float cx = s * 0.5f, cy = s * 0.5f;
            float core = s * 0.17f;
            var sun = C(0xFF, 0xD3, 0x5C);
            // 光（左上ほど長く、右下ほど短い = 明るさの段階）
            using (var pen = new Pen(sun, Math.Max(1.2f, s * 0.065f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                for (int i = 0; i < 8; i++)
                {
                    double a = Math.PI * i / 4 + Math.PI / 4 * 0;
                    // 角度 225°（左上）を最長にする
                    double diff = Math.Abs(((a * 180 / Math.PI) - 225 + 540) % 360 - 180) / 180.0; // 0〜1
                    float r1 = core + s * 0.07f, len = s * (0.17f - 0.11f * (float)diff);
                    g.DrawLine(pen, cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1,
                                    cx + (float)Math.Cos(a) * (r1 + len), cy + (float)Math.Sin(a) * (r1 + len));
                }
            // 本体（右下半分は暗い = 明るさを下げた状態）
            using (var b = new SolidBrush(sun)) g.FillEllipse(b, cx - core, cy - core, core * 2, core * 2);
            if (s >= 24)
                using (var half = new GraphicsPath())
                using (var dim = new SolidBrush(Color.FromArgb(140, 0x1C, 0x1F, 0x5E)))
                {
                    half.AddPie(cx - core, cy - core, core * 2, core * 2, -45, 180);
                    g.FillPath(dim, half);
                }
        }
        return bmp;
    }

    static byte[] Png(Bitmap bmp)
    {
        using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    /// .ico 内の BMP 形式（BITMAPINFOHEADER + 下から上への BGRA + AND マスク）
    static byte[] Dib(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        int maskStride = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(w * h * 4 + maskStride * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                }
            for (int y = h - 1; y >= 0; y--)
            {
                var row = new byte[maskStride];
                for (int x = 0; x < w; x++)
                    if (bmp.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                bw.Write(row);
            }
            return ms.ToArray();
        }
    }
}
