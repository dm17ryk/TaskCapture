using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace TaskCapture
{
    public partial class MainWindow : Window
    {
        // Hotkeys
        const int WM_HOTKEY = 0x0312;
        const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
        const int HWND_HOTKEY_ID = 100;   // Ctrl+Alt+H (выбрать окно)
        const int SNAP_HOTKEY_ID = 101;   // Ctrl+Alt+G (снять кадр)
        const int EXEC_HOTKEY_ID = 102;   // Ctrl+Alt+E (отправить)
        const int TASK_HOTKEY_ID = 103;   // Ctrl+Alt+Z (новое задание)

        private HwndSource? _src;
        private IntPtr _hwnd = IntPtr.Zero;
        private bool _busy;

        // Workflow: задания
        private int _taskIndex = 0;              // 0 = не создано
        private string? _taskDir;                // "TaskN"
        private string? _capturesDir;            // "TaskN/captures"
        private string? _resultDir;              // "TaskN/result"

        // Окно результата — чтобы закрывать по Ctrl+Alt+Z
        private ResultWindow? _resultWindow;

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
            Native.RegisterHotKey(_src.Handle, TASK_HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
                (uint)KeyInterop.VirtualKeyFromKey(Key.Z));

            // По умолчанию покажем, что задания нет
            UpdateTaskLabel();
            LogLine("Готово. В браузере: Ctrl+Alt+H → затем крутить и жать Ctrl+Alt+G для кадров. Ctrl+Alt+E — отправить. Ctrl+Alt+Z — новое задание.");
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_src != null)
            {
                Native.UnregisterHotKey(_src.Handle, HWND_HOTKEY_ID);
                Native.UnregisterHotKey(_src.Handle, SNAP_HOTKEY_ID);
                Native.UnregisterHotKey(_src.Handle, EXEC_HOTKEY_ID);
                Native.UnregisterHotKey(_src.Handle, TASK_HOTKEY_ID);
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
                else if (id == TASK_HOTKEY_ID)
                {
                    _ = NewTaskAsync();
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

        // ================== ВОРКФЛОУ: НОВОЕ ЗАДАНИЕ ==================
        private async Task NewTaskAsync()
        {
            if (_busy) { LogLine("Занят… подожди."); return; }

            // Закроем окно результата, если открыто
            if (_resultWindow != null)
            {
                try { _resultWindow.Close(); } catch { /* ignore */ }
                _resultWindow = null;
            }

            // Находим следующий индекс
            int nextIndex = Math.Max(_taskIndex, FindMaxTaskIndex()) + 1;

            _taskIndex = nextIndex;
            _taskDir = Path.Combine(Environment.CurrentDirectory, $"Task{_taskIndex}");
            _capturesDir = Path.Combine(_taskDir, "captures");
            _resultDir = Path.Combine(_taskDir, "result");

            Directory.CreateDirectory(_taskDir);
            Directory.CreateDirectory(_capturesDir);
            Directory.CreateDirectory(_resultDir);

            UpdateTaskLabel();
            LogLine($"Создано и выбрано задание: Task{_taskIndex}");
            await Task.CompletedTask;
        }

        private int FindMaxTaskIndex()
        {
            try
            {
                var dirs = Directory.EnumerateDirectories(Environment.CurrentDirectory, "Task*")
                                    .Select(d => Path.GetFileName(d))
                                    .Where(name => name != null && name.StartsWith("Task", StringComparison.OrdinalIgnoreCase))
                                    .Select(name =>
                                    {
                                        if (int.TryParse(name!.Substring(4), out var n)) return n;
                                        return 0;
                                    })
                                    .DefaultIfEmpty(0);
                return dirs.Max();
            }
            catch { return 0; }
        }

        private void UpdateTaskLabel()
        {
            LblTaskFolder.Text = _taskIndex > 0 ? $"Task{_taskIndex}" : "(нет)";
        }

        private string RequireCapturesDirOrThrow()
        {
            if (string.IsNullOrEmpty(_capturesDir) || !Directory.Exists(_capturesDir))
                throw new InvalidOperationException("Сначала создайте задание (Ctrl+Alt+Z).");
            return _capturesDir!;
        }

        private string RequireResultDirOrThrow()
        {
            if (string.IsNullOrEmpty(_resultDir) || !Directory.Exists(_resultDir))
                throw new InvalidOperationException("Сначала создайте задание (Ctrl+Alt+Z).");
            return _resultDir!;
        }

        // ====== СНИМОК БЕЗ СКРОЛЛА: РОВНО 50/50 ПО КЛИЕНТСКОЙ ОБЛАСТИ (в ФИЗ. пикселях) ======
        private async Task SnapOnceAsync()
        {
            if (_busy) { LogLine("Занят… подожди."); return; }
            if (_hwnd == IntPtr.Zero) { LogLine("Сначала выбери окно (Ctrl+Alt+H)."); return; }

            _busy = true;
            try
            {
                await EnsureModifiersReleasedAsync();

                string capDir = RequireCapturesDirOrThrow();

                var clientRect = Native.GetClientRectScreenDpiAware(_hwnd); // уже физпиксели
                var (leftR, rightR) = Utils.SplitRect(clientRect, 0.5, 0.5);
                LogLine($"Клиентская область (phys): {clientRect}. Лево={leftR}, Право={rightR}");

                using var leftBmp = Utils.CaptureRegion(leftR);
                using var rightBmp = Utils.CaptureRegion(rightR);

                Directory.CreateDirectory(capDir);
                var (lPath, rPath) = NextPairPaths(capDir);
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

        // ================== ОТПРАВКА В OPENAI (с фильтром правых кадров) ==================
        private async Task SendToOpenAIAsync()
        {
            if (_busy) { LogLine("Занят… подожди."); return; }

            _busy = true;
            try
            {
                await EnsureModifiersReleasedAsync();

                string capDir = RequireCapturesDirOrThrow();
                string resDir = RequireResultDirOrThrow();

                // Собираем пары (left/right) в порядке индексов
                var pairs = Directory.EnumerateFiles(capDir, "left_*.png")
                    .Select(p => new
                    {
                        idx = int.TryParse(Path.GetFileNameWithoutExtension(p).AsSpan(5), out var v) ? v : -1,
                        left = p
                    })
                    .Where(x => x.idx >= 0)
                    .OrderBy(x => x.idx)
                    .Select(x => (left: x.left, right: Path.Combine(capDir, $"right_{x.idx:D4}.png")))
                    .Where(x => File.Exists(x.right))
                    .ToList();

                if (pairs.Count == 0) { LogLine("Нет пар картинок left_*.png/right_*.png в текущем задании."); return; }

                // Модель и температура из UI
                if (ModelComboBox.SelectedItem is not ComboBoxItem sel)
                {
                    LogLine("Модель не выбрана — используем gpt-5-nano.");
                    // на всякий
                    var defHtml = "<article><p>Модель не выбрана. Выбери в комбобоксе.</p></article>";
                    var win0 = new ResultWindow(OpenAiClient.WrapHtml(defHtml)) { Owner = this };
                    win0.Show(); _resultWindow = win0;
                    return;
                }
                string modelId = sel.Tag?.ToString() ?? "gpt-5-nano";
                double? temperature = modelId.Equals("gpt-5-nano", StringComparison.OrdinalIgnoreCase) ? null : 0.2;

                // Фильтрация правых картинок: оставляем только первое появление каждого уникального изображения
                var imagePaths = new System.Collections.Generic.List<string>();
                byte[]? lastRightHash = null;
                int skippedRights = 0;

                foreach (var (leftPath, rightPath) in pairs)
                {
                    imagePaths.Add(leftPath); // левую всегда добавляем

                    var hash = TryGetFileHash(rightPath);
                    bool sameAsLast = lastRightHash != null && hash != null && lastRightHash.SequenceEqual(hash);

                    if (!sameAsLast)
                    {
                        imagePaths.Add(rightPath);
                        lastRightHash = hash;
                    }
                    else
                    {
                        skippedRights++;
                    }
                }

                LogLine($"Отправляю {imagePaths.Count} изображений (левая: {pairs.Count}, правая (уникальная): {imagePaths.Count - pairs.Count}, пропущено правых дубликатов: {skippedRights}).");

                var html = await OpenAiClient.SolveFromImagesAsync(imagePaths, modelId, temperature, LogLine);

                // Сохраняем в TaskN/result/result.html
                Directory.CreateDirectory(resDir);
                string outFile = Path.Combine(resDir, "result.html");
                File.WriteAllText(outFile, html);
                LogLine($"Результат сохранён: {outFile}");

                // Показать окно
                var win = new ResultWindow(html) { Owner = this };
                win.Show();
                _resultWindow = win;
                LogLine("Готово: ответ отображён.");
            }
            catch (Exception ex) { LogLine("ERR OpenAI: " + ex.Message); }
            finally { _busy = false; }
        }

        private static byte[]? TryGetFileHash(string path)
        {
            try
            {
                using var sha = SHA256.Create();
                using var fs = File.OpenRead(path);
                return sha.ComputeHash(fs);
            }
            catch { return null; }
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

        // Кнопки UI (работают в рамках текущего задания)
        private async void BtnSnap_Click(object sender, RoutedEventArgs e) => await SnapOnceAsync();
        private async void BtnSend_Click(object sender, RoutedEventArgs e) => await SendToOpenAIAsync();

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string capDir = RequireCapturesDirOrThrow();
                var dir = Path.GetFullPath(capDir);
                Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch (Exception ex) { LogLine("ERR OpenFolder: " + ex.Message); }
        }

        private void BtnClearFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string capDir = RequireCapturesDirOrThrow();
                if (!Directory.Exists(capDir)) return;
                foreach (var f in Directory.EnumerateFiles(capDir, "*.png")) File.Delete(f);
                LogLine($"Папка {Path.GetFileName(Path.GetDirectoryName(capDir))}/captures очищена.");
            }
            catch (Exception ex) { LogLine("ERR ClearFolder: " + ex.Message); }
        }
    }
}
