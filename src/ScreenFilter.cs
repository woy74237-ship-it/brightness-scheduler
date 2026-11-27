// 画面を暗くするフィルター
// モニターの明るさを 0% にしてもまぶしいとき用に、すべての画面に半透明の黒い窓を重ねて、表示そのものを暗くする。
//  ・クリックやキー入力はすべて下の窓に素通りする（WS_EX_TRANSPARENT）
//  ・フォーカスを奪わず、タスクバーや Alt+Tab にも出ない（WS_EX_NOACTIVATE / WS_EX_TOOLWINDOW）
//  ・スクリーンショットや画面共有には写らない（SetWindowDisplayAffinity。Windows 10 2004 以降）
//  ・HDR がオンの画面にも効く（色の出し方ではなく、窓を重ねているため）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BrightnessScheduler
{
    class ScreenFilter : IDisposable
    {
        readonly List<FilterWindow> windows = new List<FilterWindow>();
        int strength;   // 濃さ（%）。0 なら表示しない

        public bool Visible { get { return strength > 0; } }

        /// 濃さ（%）を指定して表示する。0 なら消す
        public void Show(int percent)
        {
            percent = Math.Max(0, Math.Min(Settings.FilterMax, percent));   // 真っ黒にはしない（操作できなくならないように）
            if (percent == 0) { Hide(); return; }
            if (windows.Count == Screen.AllScreens.Length && windows.Count > 0)
            {
                // 窓はそのまま、濃さだけ変える（スライダーを動かしている間もちらつかないように）
                if (percent != strength)
                {
                    strength = percent;
                    foreach (var w in windows) w.Opacity = percent / 100.0;
                }
                BringToFront();
                return;
            }
            strength = percent;
            Rebuild();
        }

        public void Hide()
        {
            strength = 0;
            foreach (var w in windows) { w.Close(); w.Dispose(); }
            windows.Clear();
        }

        /// 画面の構成（モニターの数・位置・解像度）が変わったときに作り直す
        public void Rebuild()
        {
            foreach (var w in windows) { w.Close(); w.Dispose(); }
            windows.Clear();
            if (strength <= 0) return;
            foreach (var screen in Screen.AllScreens)
            {
                var w = new FilterWindow(screen.Bounds, strength / 100.0);
                w.Show();
                windows.Add(w);
            }
        }

        /// ほかの「最前面」の窓に隠されないよう、ときどき前に出し直す
        public void BringToFront()
        {
            foreach (var w in windows) w.KeepOnTop();
        }

        public void Dispose() { Hide(); }

        // ------------------------------------------------------------------
        class FilterWindow : Form
        {
            [DllImport("user32.dll")] static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
            [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

            const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000, WS_EX_TOPMOST = 0x8;
            const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
            const uint SWP_NOMOVE = 0x2, SWP_NOSIZE = 0x1, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
            static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

            public FilterWindow(Rectangle bounds, double opacity)
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.Black;
                Opacity = opacity;
                TopMost = true;
                Bounds = bounds;
                Text = "";
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                    return cp;
                }
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                // スクリーンショット・画面共有・録画に写らないようにする（古い Windows では失敗するが、そのまま続ける）
                try { SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE); } catch { }
            }

            public void KeepOnTop()
            {
                if (IsHandleCreated && !IsDisposed)
                    SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }

            // 誤ってフォーカスが来ても、マウスで何かされることはない（WS_EX_TRANSPARENT）が、念のため
            protected override void WndProc(ref Message m)
            {
                const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
                if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)MA_NOACTIVATE; return; }
                base.WndProc(ref m);
            }
        }
    }
}
