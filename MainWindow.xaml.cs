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

            var candidates = new List<(int X, int Y)>();
            do
            {
                var word = iter.GetText(PageIteratorLevel.Word);
                if (string.IsNullOrWhiteSpace(word)) continue;
                if (!iter.TryGetBoundingBox(PageIteratorLevel.Word, out var box)) continue;

                // Центр слова
                int cx = box.X1 + box.Width / 2;
                int cy = box.Y1 + box.Height / 2;

                // Абсолютные экранные координаты
                int screenX = tl.X + cx;
                int screenY = tl.Y + cy;

                if (!IsExcludedZone(screenX, screenY))
                    candidates.Add((screenX, screenY));
            }
            while (iter.Next(PageIteratorLevel.Word));

            if (candidates.Count == 0) return;

            var rnd = new Random();
            var (tx, ty) = candidates[rnd.Next(candidates.Count)];

            SetCursorPos(tx, ty);
            Thread.Sleep(500);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
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
