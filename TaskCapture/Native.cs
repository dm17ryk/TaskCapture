using System.Drawing;
using System.Runtime.InteropServices;

namespace TaskCapture
{
    public static class Native
    {
        // --------- базовые типы ----------
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        // --------- user32: геометрия ----------
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        // Переводит координаты прямоугольника из окна (hWndFrom) в окно (hWndTo).
        // Для перевода в координаты экрана hWndTo = IntPtr.Zero.
        [DllImport("user32.dll")] public static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref RECT lpPoints, int cPoints);

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);
        public const uint GA_ROOT = 2;
        public static IntPtr GetAncestorTopLevel(IntPtr h) => h == IntPtr.Zero ? h : GetAncestor(h, GA_ROOT);

        // DPI for window (пер-мониторная)
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);

        /// <summary>
        /// Клиентская область окна в КООРДИНАТАХ ЭКРАНА **в ФИЗИЧЕСКИХ ПИКСЕЛЯХ**.
        /// 1) GetClientRect даёт DIP-координаты (0..W,0..H)
        /// 2) MapWindowPoints переводит в экран, но всё ещё в DIPs
        /// 3) Домножаем всё на (dpi/96.0), чтобы получить физические пиксели для CopyFromScreen
        /// </summary>
        public static Rectangle GetClientRectScreenDpiAware(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return Rectangle.Empty;

            // (0,0)-(W,H) в клиентских DIPs
            GetClientRect(hwnd, out var rcClient);

            // В экранные DIPs
            MapWindowPoints(hwnd, IntPtr.Zero, ref rcClient, 2);

            // В физические пиксели
            double scale = 1.0;
            try { scale = GetDpiForWindow(hwnd) / 96.0; } catch { /* на всякий */ }

            int left = (int)Math.Round(rcClient.Left * scale);
            int top = (int)Math.Round(rcClient.Top * scale);
            int right = (int)Math.Round(rcClient.Right * scale);
            int bottom = (int)Math.Round(rcClient.Bottom * scale);

            return new Rectangle(left, top, right - left, bottom - top);
        }

        // --------- хоткеи ----------
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // --------- состояние клавиш / отпускание ----------
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

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT_KBD[] pInputs, int cbSize);

        public static void KeyUp(ushort vk)
        {
            var up = new INPUT_KBD { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } };
            SendInput(1, new[] { up }, Marshal.SizeOf<INPUT_KBD>());
        }
    }
}
