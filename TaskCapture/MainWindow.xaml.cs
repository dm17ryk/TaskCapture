using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;


namespace TaskCapture
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        const int WM_HOTKEY = 0x0312;
        const uint MOD_CONTROL = 0x0002;
        const uint MOD_ALT = 0x0001;
        const uint MOD_NOREPEAT = 0x4000;
        const int HWND_HOTKEY_ID = 42; // hwnd
        const int SCRL_HOTKEY_ID = 43; // scrol

        private bool _isCapturing;

        HwndSource? _src;

        private IntPtr _hwnd = IntPtr.Zero;
        public MainWindow()
        {
            InitializeComponent();
        }

        private void LogLine(string s) => Dispatcher.Invoke(() => Log.AppendText(s + Environment.NewLine));


        private void BtnPickActive_Click(object sender, RoutedEventArgs e)
        {
            _hwnd = Native.GetForegroundWindow();
            LblHwnd.Text = _hwnd != IntPtr.Zero ? $"0x{_hwnd.ToInt64():X}" : "(нет)";
        }

        private async void BtnPickCursor_Click(object sender, RoutedEventArgs e)
        {
            LogLine("Наведите курсор на нужное окно — захват через 3 секунды…");
            await Task.Delay(3000);
            if (Native.GetCursorPos(out var pt))
            {
                _hwnd = Native.WindowFromPoint(pt);
                // поднимемся к верхнему окну-предку, имеющему клиентскую область
                _hwnd = Native.GetAncestorTopLevel(_hwnd);
                LblHwnd.Text = _hwnd != IntPtr.Zero ? $"0x{_hwnd.ToInt64():X}" : "(нет)";
            }
        }

        private async void BtnRun_Click(object s, RoutedEventArgs e) => await CaptureAsync();

        private async Task CaptureAsync()
        {
            if (_isCapturing) { LogLine("Уже идёт захват…"); return; }
            _isCapturing = true;
            try
            {
                if (_hwnd == IntPtr.Zero) { LogLine("Сначала выберите окно."); return; }
                if (!double.TryParse(TbLeft.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var lp)) lp = 0.5;
                if (!double.TryParse(TbRight.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var rp)) rp = 0.5;

                var clientRect = Native.GetClientRectScreenDpiAware(_hwnd);
                var (leftR, rightR) = Utils.SplitRect(clientRect, lp, rp);
                LogLine($"Клиентская область: {clientRect}. Лево={leftR}, Право={rightR}");

                var root = AutomationElement.FromHandle(_hwnd);
                var leftEl = UiaHelper.FindScrollableForRegionAdvanced(root, leftR);
                var rightEl = UiaHelper.FindScrollableForRegionAdvanced(root, rightR);

                var leftTask = UiaHelper.ScrollAndCaptureAsync(leftEl, leftR, overlapPx: 80, settleMs: 120, stayInBrowser: true, log: LogLine);
                var rightTask = UiaHelper.ScrollAndCaptureAsync(rightEl, rightR, overlapPx: 80, settleMs: 120, stayInBrowser: true, log: LogLine);

                var left = await leftTask; var right = await rightTask;

                Directory.CreateDirectory("captures");
                for (int i = 0; i < left.Frames.Count; i++) left.Frames[i].Save($"captures/left_{i:D2}.png");
                for (int i = 0; i < right.Frames.Count; i++) right.Frames[i].Save($"captures/right_{i:D2}.png");
                LogLine($"Готово: left={left.Frames.Count}, right={right.Frames.Count} кадров. См. ./captures");
            }
            catch (Exception ex) { LogLine("ERR: " + ex.Message); }
            finally { _isCapturing = false; }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _src = (HwndSource)PresentationSource.FromVisual(this)!;
            _src.AddHook(WndProc);
            Native.RegisterHotKey(_src.Handle, HWND_HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(Key.H)); // Ctrl+Alt+H
            Native.RegisterHotKey(_src.Handle, SCRL_HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(Key.G)); // Ctrl+Alt+G
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_src != null)
            {
                Native.UnregisterHotKey(_src.Handle, HWND_HOTKEY_ID);
                Native.UnregisterHotKey(_src.Handle, SCRL_HOTKEY_ID);
            }
            base.OnClosed(e);
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HWND_HOTKEY_ID)
            {
                _hwnd = Native.GetForegroundWindow();  // это БРАУЗЕР, т.к. фокус ещё там
                LblHwnd.Text = $"0x{_hwnd.ToInt64():X}";    // сохрани куда нужно
                handled = true;
                LogLine($"HOTKEY: HWND={_hwnd}");
            }
            else if (msg == WM_HOTKEY && wParam.ToInt32() == SCRL_HOTKEY_ID)
            {
                _ = Task.Run(async () =>
                {
                    await EnsureModifiersReleasedAsync();
                    await Dispatcher.InvokeAsync(async () => await CaptureAsync());
                });
                handled = true;
            }

            return IntPtr.Zero;
        }

        private static async Task EnsureModifiersReleasedAsync(int timeoutMs = 500)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                bool ctrl = (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0
                         || (Native.GetAsyncKeyState(Native.VK_LCONTROL) & 0x8000) != 0
                         || (Native.GetAsyncKeyState(Native.VK_RCONTROL) & 0x8000) != 0;
                bool alt = (Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0
                         || (Native.GetAsyncKeyState(Native.VK_LMENU) & 0x8000) != 0
                         || (Native.GetAsyncKeyState(Native.VK_RMENU) & 0x8000) != 0;

                if (!ctrl && !alt) return;
                await Task.Delay(25);
            }
            // timeout — принудительно отпустим
            Native.KeyUp((ushort)Native.VK_LCONTROL);
            Native.KeyUp((ushort)Native.VK_RCONTROL);
            Native.KeyUp((ushort)Native.VK_LMENU);
            Native.KeyUp((ushort)Native.VK_RMENU);
        }
    }
}