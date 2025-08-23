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

        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);

        public const int VK_CONTROL = 0x11, VK_MENU = 0x12;
        public const int VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
        public const int VK_LMENU = 0xA4, VK_RMENU = 0xA5;

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT_KBD { public uint type; public KEYBDINPUT ki; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
        [DllImport("user32.dll")] static extern uint SendInput(uint nInputs, INPUT_KBD[] pInputs, int cbSize);

        public static void KeyUp(ushort vk)
        {
            var up = new INPUT_KBD { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } };
            SendInput(1, new[] { up }, Marshal.SizeOf<INPUT_KBD>());
        }

        //[DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT pt);
        [DllImport("user32.dll")] public static extern IntPtr ChildWindowFromPointEx(IntPtr hwndParent, POINT pt, uint uFlags);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint Msg, UIntPtr wParam, IntPtr lParam);

        public const uint WM_MOUSEWHEEL = 0x020A;
        public const uint CWP_SKIPINVISIBLE = 0x0001;
        public const uint CWP_SKIPDISABLED = 0x0002;
        public const uint CWP_SKIPTRANSPARENT = 0x0004;

        public static void PostMouseWheelToPoint(int xScreen, int yScreen, int delta)
        {
            var pt = new POINT { X = xScreen, Y = yScreen };
            IntPtr hwnd = WindowFromPoint(pt);
            if (hwnd == IntPtr.Zero) return;

            // углубимся до дочернего под указанной точкой
            var clientPt = pt;
            ScreenToClient(hwnd, ref clientPt);
            var child = ChildWindowFromPointEx(hwnd, clientPt, CWP_SKIPDISABLED | CWP_SKIPINVISIBLE | CWP_SKIPTRANSPARENT);
            if (child != IntPtr.Zero) hwnd = child;

            // wParam: LOWORD=0 (нет MK_CONTROL), HIWORD=wheel delta
            uint wParam = (uint)((ushort)delta) << 16; // delta = +/-120 * N
                                                       // lParam: screen coords (x,y) packed: low = x, high = y
            int lParam = (yScreen << 16) | (xScreen & 0xFFFF);

            PostMessage(hwnd, WM_MOUSEWHEEL, (UIntPtr)wParam, (IntPtr)lParam);
        }
    }
}
