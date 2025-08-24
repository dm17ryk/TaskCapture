using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TaskCapture
{
    public partial class MainWindow : Window
    {
        // Hotkeys
        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
        const int HWND_HOTKEY_ID = 100;   // Ctrl+Alt+H
        const int SNAP_HOTKEY_ID = 101;   // Ctrl+Alt+G
        const int EXEC_HOTKEY_ID = 102;   // Ctrl+Alt+E

        private HwndSource? _src;
        private IntPtr _hwnd = IntPtr.Zero;
        private bool _busy;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _src = (HwndSource)PresentationSource.FromVisual(this)!;
            _src.AddHook(WndProc);

            Native.RegisterHotKey(_src.Handle, HWND_HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
                (uint)KeyInterop.VirtualKeyFromKey(Key.H));
            Native.RegisterHotKey(_src.Handle, SNAP_HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
                (uint)KeyInterop.VirtualKeyFromKey(Key.G));
            Native.RegisterHotKey(_src.Handle, EXEC_HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
                (uint)KeyInterop.VirtualKeyFromKey(Key.E));

            LogLine("Готово. В браузере: Ctrl+Alt+H → затем крутить и жать Ctrl+Alt+G для кадров. Ctrl+Alt+E — отправить.");
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_src != null)
            {
                Native.UnregisterHotKey(_src.Handle, HWND_HOTKEY_ID);
                Native.UnregisterHotKey(_src.Handle, SNAP_HOTKEY_ID);
                Native.UnregisterHotKey(_src.Handle, EXEC_HOTKEY_ID);
            }
            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HWND_HOTKEY_ID)
                {
                    _hwnd = Native.GetForegroundWindow();
                    _hwnd = Native.GetAncestorTopLevel(_hwnd);
                    LblHwnd.Text = _hwnd != IntPtr.Zero ? $"0x{_hwnd.ToInt64():X}" : "(нет)";
                    LogLine($"HWND выбран: {LblHwnd.Text}");
                    handled = true;
                }
                else if (id == SNAP_HOTKEY_ID)
                {
                    _ = SnapOnceAsync();
                    handled = true;
                }
                else if (id == EXEC_HOTKEY_ID)
                {
                    _ = SendToOpenAIAsync();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void LogLine(string s) => Dispatcher.Invoke(() =>
        {
            Log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}{Environment.NewLine}");
            Log.ScrollToEnd();
        });

        // ====== СНИМОК БЕЗ СКРОЛЛА: РОВНО 50/50 ПО КЛИЕНТСКОЙ ОБЛАСТИ (в ФИЗ. пикселях) ======
        private async Task SnapOnceAsync()
        {
            if (_busy) { LogLine("Занят… подожди."); return; }
            if (_hwnd == IntPtr.Zero) { LogLine("Сначала выбери окно (Ctrl+Alt+H)."); return; }

            _busy = true;
            try
            {
                await EnsureModifiersReleasedAsync();

                var clientRect = Native.GetClientRectScreenDpiAware(_hwnd); // уже физпиксели
                var (leftR, rightR) = Utils.SplitRect(clientRect, 0.5, 0.5);
                LogLine($"Клиентская область (phys): {clientRect}. Лево={leftR}, Право={rightR}");

                using var leftBmp = Utils.CaptureRegion(leftR);
                using var rightBmp = Utils.CaptureRegion(rightR);

                Directory.CreateDirectory("captures");
                var (lPath, rPath) = NextPairPaths("captures");
                leftBmp.Save(lPath);
                rightBmp.Save(rPath);

                LogLine($"Снято: {Path.GetFileName(lPath)}, {Path.GetFileName(rPath)}");
            }
            catch (Exception ex) { LogLine("ERR Snap: " + ex.Message); }
            finally { _busy = false; }
        }

        private static (string leftPath, string rightPath) NextPairPaths(string dir)
        {
            int idx = Directory.EnumerateFiles(dir, "left_*.png")
                               .Select(f => Path.GetFileNameWithoutExtension(f))
                               .Select(n => int.TryParse(n.AsSpan(5), out var v) ? v : -1)
                               .DefaultIfEmpty(-1).Max() + 1;

            string li = Path.Combine(dir, $"left_{idx:D4}.png");
            string ri = Path.Combine(dir, $"right_{idx:D4}.png");
            return (li, ri);
        }

        private async Task SendToOpenAIAsync()
        {
            if (_busy) { LogLine("Занят… подожди."); return; }
            _busy = true;
            try
            {
                await EnsureModifiersReleasedAsync();

                string dir = "captures";
                if (!Directory.Exists(dir)) { LogLine("Папка captures пуста."); return; }

                var pairs = Directory.EnumerateFiles(dir, "left_*.png")
                    .Select(p => new
                    {
                        idx = int.TryParse(Path.GetFileNameWithoutExtension(p).AsSpan(5), out var v) ? v : -1,
                        left = p
                    })
                    .Where(x => x.idx >= 0)
                    .OrderBy(x => x.idx)
                    .Select(x => (left: x.left, right: Path.Combine(dir, $"right_{x.idx:D4}.png")))
                    .Where(x => File.Exists(x.right))
                    .ToList();

                if (pairs.Count == 0) { LogLine("Нет пар картинок left_*.png/right_*.png."); return; }

                LogLine($"Отправляю {pairs.Count * 2} изображений в OpenAI…");

                var html = await OpenAiClient.SolveFromImagesAsync(
                    pairs.SelectMany(p => new[] { p.left, p.right }).ToList(),
                    LogLine);

                var win = new ResultWindow(html) { Owner = this };
                win.Show();
                LogLine("Готово: ответ отображён.");
            }
            catch (Exception ex) { LogLine("ERR OpenAI: " + ex.Message); }
            finally { _busy = false; }
        }

        // дождаться отпускания модификаторов
        private static async Task EnsureModifiersReleasedAsync(int timeoutMs = 500)
        {
            var sw = Stopwatch.StartNew();
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
            // подстрахуемся
            Native.KeyUp((ushort)Native.VK_LCONTROL);
            Native.KeyUp((ushort)Native.VK_RCONTROL);
            Native.KeyUp((ushort)Native.VK_LMENU);
            Native.KeyUp((ushort)Native.VK_RMENU);
        }

        // Кнопки UI
        private async void BtnSnap_Click(object sender, RoutedEventArgs e) => await SnapOnceAsync();
        private async void BtnSend_Click(object sender, RoutedEventArgs e) => await SendToOpenAIAsync();

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = Path.GetFullPath("captures");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }

        private void BtnClearFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = "captures";
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, "*.png")) File.Delete(f);
            LogLine("Папка captures очищена.");
        }
    }
}
