using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Tesseract;
using System.Windows.Interop;
using System.Windows.Forms;



namespace LU4_Walker
{
    public partial class MainWindow : Window
    {
        // WinAPI для работы с окном и курсором
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private IntPtr hookId = IntPtr.Zero;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc proc;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);


        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        // Таймеры
        private readonly DispatcherTimer movementTimer = new();
        private readonly DispatcherTimer keyTimer = new();

        // SerialPort для Teensy
        private SerialPort teensy = null!;

        // Выбранное окно
        private IntPtr targetHwnd = IntPtr.Zero;

        public MainWindow()
        {
            InitializeComponent();
            proc = HookCallback;
            // Проверкa наличия COM4
            var ports = SerialPort.GetPortNames();
            if (!ports.Contains("COM4"))
            {
                MessageBox.Show(
                    "COM4 не найден. Подключите Teensy и перезапустите.",
                    "Ошибка COM-порта",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            

            // Инициализация и открытие Teensy
            try
            {
                teensy = new SerialPort("COM4", 9600);
                teensy.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось открыть COM4:\n{ex.Message}",
                    "Ошибка COM-порта",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Application.Current.Shutdown();
                return;
            }

            // Таймер движения мыши + ЛКМ (каждую секунду)
            movementTimer.Interval = TimeSpan.FromSeconds(1);
            movementTimer.Tick += (_, __) =>
            {
                if (targetHwnd != IntPtr.Zero && !IsTargetVisible(targetHwnd))
                    MoveAndClick(targetHwnd);
            };

            // Таймер нажатия "1" (каждую секунду)
            keyTimer.Interval = TimeSpan.FromSeconds(1);
            keyTimer.Tick += (_, __) =>
            {
                if (targetHwnd != IntPtr.Zero && IsTargetVisible(targetHwnd))
                    teensy.Write("1");
            };
        }

        // ──────────────────────────────────────────────────────────────────────────
        // UI-обработчики. Убедись, что XAML привязан к этим методам:
        // Window_MouseLeftButtonDown, btnClose_Click, cbWindows_DropDownOpened,
        // btnStart_Click, btnStop_Click, btnScreenshot_Click,
        // btnOcrScreenshot_Click, btnGetR_Click
        // ──────────────────────────────────────────────────────────────────────────

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            if (teensy.IsOpen) teensy.Close();
            Application.Current.Shutdown();
            if (hookId != IntPtr.Zero)
                UnhookWindowsHookEx(hookId);

        }

        private void cbWindows_DropDownOpened(object sender, EventArgs e)
        {
            cbWindows.Items.Clear();
            foreach (var proc in Process.GetProcesses())
            {
                if (proc.MainWindowHandle != IntPtr.Zero &&
                    proc.ProcessName.Contains("lu4", StringComparison.OrdinalIgnoreCase))
                {
                    cbWindows.Items.Add(new WindowItem(proc.MainWindowHandle, proc.MainWindowTitle));
                }
            }
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (cbWindows.SelectedItem is not WindowItem wi)
            {
                MessageBox.Show("Выберите окно LU4.");
                return;
            }
            targetHwnd = wi.Hwnd;
            movementTimer.Start();
            keyTimer.Start();
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            movementTimer.Stop();
            keyTimer.Stop();
            targetHwnd = IntPtr.Zero;
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        private void btnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = GetEffectiveHwnd();
            if (hwnd == IntPtr.Zero)
            {
                MessageBox.Show("Окно не выбрано.");
                return;
            }

            if (!GetClientRect(hwnd, out RECT rect)) return;
            var tl = new POINT();
            if (!ClientToScreen(hwnd, ref tl)) return;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(tl.X, tl.Y, 0, 0, bmp.Size);

            string name = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss}.bmp";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);

            MessageBox.Show($"Скриншот сохранён:\n{name}");
        }

        private void btnOcrScreenshot_Click(object sender, RoutedEventArgs e)
        {
            var hwnd = GetEffectiveHwnd();
            if (hwnd == IntPtr.Zero)
            {
                MessageBox.Show("Окно не выбрано.");
                return;
            }

            if (!GetClientRect(hwnd, out RECT rect)) return;
            var tl = new POINT();
            if (!ClientToScreen(hwnd, ref tl)) return;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            // Полный скриншот
            using var bmp = new Bitmap(w, h);
            using var gFull = Graphics.FromImage(bmp);
            gFull.CopyFromScreen(tl.X, tl.Y, 0, 0, bmp.Size);

            // OCR по всему окну
            using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            ms.Position = 0;
            using var pix = Pix.LoadFromMemory(ms.ToArray());
            using var page = engine.Process(pix);
            using var iter = page.GetIterator();
            iter.Begin();

            using var pen = new Pen(Color.LimeGreen, 2);
            do
            {
                if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var box))
                    gFull.DrawRectangle(pen, box.X1, box.Y1, box.Width, box.Height);
            }
            while (iter.Next(PageIteratorLevel.Word));

            string name = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss} - OCR.bmp";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);

            MessageBox.Show($"OCR-скрин сохранён:\n{name}");
        }

        private void btnGetR_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(tbX.Text, out int x) ||
                !int.TryParse(tbY.Text, out int y))
            {
                MessageBox.Show("Введите корректные X и Y.");
                return;
            }

            var latest = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
                .GetFiles("*.bmp")
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            if (latest == null)
            {
                MessageBox.Show("Нет BMP-файлов.");
                return;
            }

            try
            {
                using var bmp = new Bitmap(latest.FullName);
                if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height)
                {
                    MessageBox.Show("Координаты вне диапазона.");
                    return;
                }
                lblR.Text = bmp.GetPixel(x, y).R.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка чтения BMP:\n" + ex.Message);
            }
        }

        // Проверяет, есть ли красный пиксель HP на верхней строке окна
        private bool IsTargetVisible(IntPtr hwnd)
        {
            if (!GetClientRect(hwnd, out RECT rect)) return false;
            var tl = new POINT();
            if (!ClientToScreen(hwnd, ref tl)) return false;

            int width = rect.Right - rect.Left;
            using var bmp = new Bitmap(width, 1);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(tl.X, tl.Y, 0, 0, bmp.Size);

            for (int x = 0; x < width; x++)
            {
                var c = bmp.GetPixel(x, 0);
                if (c.R >= 240 && c.G < 30 && c.B < 30)
                    return true;
            }
            return false;
        }

        // Определяет, попадает ли точка в запрещённую зону интерфейса
        private bool IsExcludedZone(int x, int y)
        {
            var zones = new List<(int x1, int y1, int x2, int y2)>
            {
                (0,    0,    250,  110),
                (1040, 0,    1550, 30),
                (2350, 0,    2560, 235),
                (0,    960,  360,  1440),
                (1010, 1270, 1590, 1440),
                (2460, 1040, 2560, 1440)
            };
            return zones.Any(z => x >= z.x1 && x <= z.x2 && y >= z.y1 && y <= z.y2);
        }

        // Делает OCR по всему окну, находит слова и кликает ЛКМ вне запрещённых зон
        private void MoveAndClick(IntPtr hwnd)
        {
            if (!GetClientRect(hwnd, out RECT rect)) return;
            var tl = new POINT();
            if (!ClientToScreen(hwnd, ref tl)) return;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            int screenCenterX = tl.X + w / 2;
            int screenCenterY = tl.Y + h / 2;

            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(tl.X, tl.Y, 0, 0, bmp.Size);

            using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            ms.Position = 0;

            using var pix = Pix.LoadFromMemory(ms.ToArray());
            using var page = engine.Process(pix);
            using var iter = page.GetIterator();
            iter.Begin();

            var targets = new List<(int X, int Y)>();
            do
            {
                var word = iter.GetText(PageIteratorLevel.Word);
                if (string.IsNullOrWhiteSpace(word)) continue;
                if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var box)) continue;

                int cx = box.X1 + box.Width / 2;
                int cy = box.Y1 + box.Height / 2;

                int screenX = tl.X + cx;
                int screenY = tl.Y + cy;

                if (!IsExcludedZone(screenX, screenY))
                    targets.Add((screenX, screenY));
            }
            while (iter.Next(PageIteratorLevel.Word));

            if (targets.Count == 0) return;

            var sorted = targets
                .OrderBy(p => Math.Sqrt(Math.Pow(p.X - screenCenterX, 2) + Math.Pow(p.Y - screenCenterY, 2)))
                .ToList();
            var (tx, ty) = sorted[0];
            ClickTargetTwice(tx, ty);

        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            proc = HookCallback;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            hookId = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            const int WM_KEYDOWN = 0x0100;

            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);

                if (vkCode == KeyInterop.VirtualKeyFromKey(Key.PageUp))
                    Dispatcher.Invoke(() => btnStart_Click(btnStart, new RoutedEventArgs()));

                else if (vkCode == KeyInterop.VirtualKeyFromKey(Key.PageDown))
                    Dispatcher.Invoke(() => btnStop_Click(btnStop, new RoutedEventArgs()));
            }

            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }



        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.PageUp)
            {
                btnStart_Click(btnStart, new RoutedEventArgs());
            }
            else if (e.Key == Key.PageDown)
            {
                btnStop_Click(btnStop, new RoutedEventArgs());
            }
        }

        private void ClickTargetTwice(int x, int y)
        {
            var clicks = new List<(int dx, int dy)>
    {
        (0, 10),    // ниже
        (-10, 0)    // влево
    };

            foreach (var (dx, dy) in clicks)
            {
                SetCursorPos(x + dx, y + dy);
                Thread.Sleep(300);
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
                Thread.Sleep(100);
            }
        }

        // Вспомогательный: конвертация Bitmap в байты для Tesseract
        private byte[] BitmapToBytes(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            return ms.ToArray();
        }

        // Обёртка для ComboBox
        private class WindowItem
        {
            public IntPtr Hwnd { get; }
            public string Title { get; }
            public WindowItem(IntPtr hwnd, string title)
            {
                Hwnd = hwnd;
                Title = title;
            }
            public override string ToString() => Title;
        }

        // Возвращает текущее выбранное окно
        private IntPtr GetEffectiveHwnd()
        {
            if (targetHwnd != IntPtr.Zero)
                return targetHwnd;
            if (cbWindows.SelectedItem is WindowItem wi)
                return wi.Hwnd;
            return IntPtr.Zero;
        }
    }
}
