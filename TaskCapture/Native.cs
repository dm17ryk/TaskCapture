using System.Drawing;
using System.Runtime.InteropServices;

namespace TaskCapture
{
    public static class Native
    {
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }


        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT Point);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);

        const uint INPUT_MOUSE = 0;
        const uint MOUSEEVENTF_WHEEL = 0x0800;

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public MOUSEINPUT mi; }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        public static void SendMouseWheel(int delta)
        {
            var inp = new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { mouseData = (uint)delta, dwFlags = MOUSEEVENTF_WHEEL } };
            SendInput(1, new[] { inp }, Marshal.SizeOf<INPUT>());
        }


        const uint GA_ROOT = 2;
        public static IntPtr GetAncestorTopLevel(IntPtr h) => h == IntPtr.Zero ? h : GetAncestor(h, GA_ROOT);

        public static Rectangle GetClientRectScreenDpiAware(IntPtr hwnd)
        {
            GetClientRect(hwnd, out var rc);
            var pt = new POINT { X = rc.Left, Y = rc.Top };
            ClientToScreen(hwnd, ref pt);
            int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
            // На всякий случай — DPI (в WPF обычно уже корректно, но оставим)
            try
            {
                uint dpi = GetDpiForWindow(hwnd); // физические пиксели
                                                  // WPF координаты уже в физ. пикселях при PerMonitorV2, поэтому масштаб не меняем
                return new Rectangle(pt.X, pt.Y, w, h);
            }
            catch { return new Rectangle(pt.X, pt.Y, w, h); }
        }
    }
}
