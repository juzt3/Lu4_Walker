// MainWindow.xaml.cs

using System;
using System.Diagnostics;
using System.Drawing;               // Bitmap, Point, Color
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
        // ─── P/Invoke: курсор, окно, глобальный хук ───

        [DllImport("user32.dll")]
        static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int X, int Y);

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern IntPtr SetWindowsHookEx(int idHook,
            LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        // ─── ПОЛЯ ───

        private readonly SerialPort teensy;
        private readonly DispatcherTimer attackTimer = new DispatcherTimer();
        private readonly DispatcherTimer targetScanTimer = new DispatcherTimer();

        // глобальный хук
        private readonly LowLevelKeyboardProc hookProc;
        private IntPtr hookId = IntPtr.Zero;

        // выбранное окно LU4
        private IntPtr targetHwnd = IntPtr.Zero;

        // для циклического обхода целей (пока не используется)
        private int lastTargetIndex = -1;

        // ─── КОНСТРУКТОР ───

        public MainWindow()
        {
            InitializeComponent();

            // Teensy на COM3
            teensy = new SerialPort("COM3", 9600);
            try
            {
                teensy.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось открыть COM3:\n{ex.Message}",
                    "Ошибка COM-порта",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            // Таймер атаки: 1 сек
            attackTimer.Interval = TimeSpan.FromSeconds(1);
            attackTimer.Tick += AttackTimer_Tick;

            // Таймер поиска цели: 2 сек (пока пустой метод)
            targetScanTimer.Interval = TimeSpan.FromSeconds(2);
            targetScanTimer.Tick += TargetScanTimer_Tick;

            // Глобальный хук PageUp / PageDown
            hookProc = HookCallback;
            Loaded += (_, __) =>
            {
                using var curMod = Process.GetCurrentProcess().MainModule!;
                hookId = SetWindowsHookEx(
                    WH_KEYBOARD_LL,
                    hookProc,
                    GetModuleHandle(curMod.ModuleName),
                    0
                );
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            if (hookId != IntPtr.Zero)
                UnhookWindowsHookEx(hookId);
            base.OnClosed(e);
        }

        // ─── ГЛОБАЛЬНЫЙ ХУК КЛАВИШ ───

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == KeyInterop.VirtualKeyFromKey(Key.PageUp))
                    Dispatcher.Invoke(StartTimers);
                else if (vk == KeyInterop.VirtualKeyFromKey(Key.PageDown))
                    Dispatcher.Invoke(StopTimers);
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        // ─── UI: перетаскивание и кнопки ───

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();

        private void btnClose_Click(object sender, RoutedEventArgs e)
            => Application.Current.Shutdown();

        private void cbWindows_DropDownOpened(object sender, EventArgs e)
        {
            cbWindows.Items.Clear();
            foreach (var p in Process.GetProcesses()
                                      .Where(p => p.MainWindowHandle != IntPtr.Zero
                                               && p.ProcessName
                                                     .Contains("lu4", StringComparison.OrdinalIgnoreCase)))
            {
                cbWindows.Items.Add(new WindowItem(p.MainWindowHandle, p.MainWindowTitle));
            }
        }

        private void btnStart_Click(object sender, RoutedEventArgs e) => StartTimers();
        private void btnStop_Click(object sender, RoutedEventArgs e) => StopTimers();

        // ─── Start / Stop ───

        private void StartTimers()
        {
            if (cbWindows.SelectedItem is not WindowItem wi)
            {
                MessageBox.Show("Выберите окно LU4", "Ошибка", MessageBoxButton.OK);
                return;
            }

            targetHwnd = wi.Hwnd;
            lastTargetIndex = -1;

            attackTimer.Start();
            targetScanTimer.Start();

            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        private void StopTimers()
        {
            attackTimer.Stop();
            targetScanTimer.Stop();

            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        // ─── Attack: каждые 1 сек при R==255 кликаем ───

        private void AttackTimer_Tick(object? sender, EventArgs e)
        {
            const int px = 1107, py = 0;
            if (!IsRedPixelAt(px, py)) return;

            SetCursorPos(px, py);
            teensy.Write("[");
            Thread.Sleep(80);
            teensy.Write("]");
        }

        // ─── Поиск цели: каждую 2 сек (метод пока пуст) ───

        private void TargetScanTimer_Tick(object? sender, EventArgs e)
        {
            // TODO: здесь логика поиска целей (OCR / цветовые маркеры и т.д.)
        }

        // ─── Скриншот выбранного окна ───

        private void btnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (cbWindows.SelectedItem is not WindowItem wi)
            {
                MessageBox.Show("Сначала выберите окно", "Ошибка", MessageBoxButton.OK);
                return;
            }

            var hwnd = wi.Hwnd;
            GetClientRect(hwnd, out var rc);
            var org = new POINT();
            ClientToScreen(hwnd, ref org);

            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;
            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(org.X, org.Y, 0, 0, bmp.Size);

            string fn = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss}.bmp";
            bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fn));
            MessageBox.Show($"Скриншот сохранён:\n{fn}", "Готово");
        }

        // ─── OCR-скриншот (обводка слов) ───

        private void btnOcrScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (cbWindows.SelectedItem is not WindowItem wi)
            {
                MessageBox.Show("Сначала выберите окно", "Ошибка", MessageBoxButton.OK);
                return;
            }

            var hwnd = wi.Hwnd;
            GetClientRect(hwnd, out var rc);
            var org = new POINT();
            ClientToScreen(hwnd, ref org);

            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;
            using var bmp = new Bitmap(w, h);
            using var gFull = Graphics.FromImage(bmp);
            gFull.CopyFromScreen(org.X, org.Y, 0, 0, bmp.Size);

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

            string fn = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss} - OCR.bmp";
            bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fn));
            MessageBox.Show($"OCR-скрин сохранён:\n{fn}", "Готово");
        }

        // ─── Проверка R-канала в последнем скриншоте ───

        private void btnGetR_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(tbX.Text, out int x) ||
                !int.TryParse(tbY.Text, out int y))
            {
                MessageBox.Show("Введите корректные X и Y", "Ошибка", MessageBoxButton.OK);
                return;
            }

            var latest = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
                         .GetFiles("*.bmp")
                         .OrderByDescending(f => f.LastWriteTime)
                         .FirstOrDefault();

            if (latest == null)
            {
                MessageBox.Show("Нет BMP-файлов", "Ошибка", MessageBoxButton.OK);
                return;
            }

            using var bmp = new Bitmap(latest.FullName);
            if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height)
            {
                MessageBox.Show("Координаты вне диапазона", "Ошибка", MessageBoxButton.OK);
                return;
            }

            lblR.Text = bmp.GetPixel(x, y).R.ToString();
        }

        // ─── Проверка красного пикселя на экране ───

        private bool IsRedPixelAt(int x, int y)
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(x, y, 0, 0, bmp.Size);
            return bmp.GetPixel(0, 0).R == 255;
        }

        // ─── Класс для списка окон в ComboBox ───

        private class WindowItem
        {
            public IntPtr Hwnd { get; }
            public string Title { get; }

            public WindowItem(IntPtr hwnd, string title) =>
                (Hwnd, Title) = (hwnd, title);

            public override string ToString() => Title;
        }
    }
}
