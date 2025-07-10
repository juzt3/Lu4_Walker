using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using Tesseract;

namespace LU4_Walker
{
    public partial class MainWindow : Window
    {
        //────────────────────────────────────────────────────────────────────────────
        // WinAPI для работы с курсором и глобального хука клавиатуры
        //────────────────────────────────────────────────────────────────────────────
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc proc;
        private IntPtr hookId = IntPtr.Zero;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
                                                      IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk,
                                                                             int nCode,
                                                                             IntPtr wParam,
                                                                             IntPtr lParam);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        //────────────────────────────────────────────────────────────────────────────
        // Поля
        //────────────────────────────────────────────────────────────────────────────
        private SerialPort teensy = null!;      // COM-порт Teensy
        private IntPtr targetHwnd = IntPtr.Zero; // Выбранное окно игры

        private readonly System.Timers.Timer movementTimer = new(400);
        private readonly System.Timers.Timer keyTimer = new(400);
        private readonly System.Timers.Timer pickupTimer = new(400);

        private int cycleCount = 0; // счетчик для поиска мышкой 1 из 10

        //────────────────────────────────────────────────────────────────────────────
        // Конструктор
        //────────────────────────────────────────────────────────────────────────────
        public MainWindow()
        {
            InitializeComponent();
            proc = HookCallback;

            EnsureComPort();
            InitializeTeensy();
            InitializeTimers();
        }

        //────────────────────────────────────────────────────────────────────────────
        // Убедиться, что есть COM4
        //────────────────────────────────────────────────────────────────────────────
        private void EnsureComPort()
        {
            var ports = SerialPort.GetPortNames();
            if (!ports.Contains("COM4"))
            {
                MessageBox.Show("COM4 не найден. Подключите Teensy и перезапустите.",
                                "Ошибка COM-порта", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        //────────────────────────────────────────────────────────────────────────────
        // Открыть Teensy
        //────────────────────────────────────────────────────────────────────────────
        private void InitializeTeensy()
        {
            try
            {
                teensy = new SerialPort("COM4", 9600);
                teensy.Open();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть COM4:\n{ex.Message}",
                                "Ошибка COM-порта", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        //────────────────────────────────────────────────────────────────────────────
        // Настроить таймеры (не запускать их сразу)
        //────────────────────────────────────────────────────────────────────────────
        private void InitializeTimers()
        {
            movementTimer.AutoReset = true;
            movementTimer.Elapsed += MovementTimer_Elapsed;

            keyTimer.AutoReset = true;
            keyTimer.Elapsed += KeyTimer_Elapsed;

            pickupTimer.AutoReset = true;
            pickupTimer.Elapsed += PickupTimer_Elapsed;
        }

        //────────────────────────────────────────────────────────────────────────────
        // movementTimer: поиск цели / клик мышкой или F12
        //────────────────────────────────────────────────────────────────────────────
        private void MovementTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Task.Run(() =>
            {
                if (targetHwnd == IntPtr.Zero) return;

                bool hpVisible = IsTargetVisible(targetHwnd);
                bool isCorpse = IsTargetDeadAndLootable();

                if (!hpVisible && !isCorpse)
                {
                    if (cycleCount % 10 == 0)
                        MoveAndClick(targetHwnd);
                    else
                        teensy.Write("L"); // F12 поиск

                    cycleCount++;
                }
            });
        }

        //────────────────────────────────────────────────────────────────────────────
        // keyTimer: атака "1" когда цель жива
        //────────────────────────────────────────────────────────────────────────────
        private void KeyTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Task.Run(() =>
            {
                if (targetHwnd != IntPtr.Zero && IsTargetVisible(targetHwnd))
                    teensy.Write("1");
            });
        }

        //────────────────────────────────────────────────────────────────────────────
        // pickupTimer: лут при мертвом монстре
        //────────────────────────────────────────────────────────────────────────────
        private void PickupTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            Task.Run(async () =>
            {
                if (targetHwnd == IntPtr.Zero) return;
                if (!IsTargetDeadAndLootable()) return;

                for (int i = 0; i < 10; i++)
                {
                    teensy.Write("J");       // F10
                    await Task.Delay(150);
                }
                await Task.Delay(300);
                teensy.Write("x");           // ESC снять цель
            });
        }

        //────────────────────────────────────────────────────────────────────────────
        // UI-обработчики (привязаны в XAML)
        //────────────────────────────────────────────────────────────────────────────
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            using var procModule = Process.GetCurrentProcess().MainModule!;
            hookId = SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                                     GetModuleHandle(procModule.ModuleName), 0);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.PageUp) btnStart_Click(btnStart, null);
            else if (e.Key == Key.PageDown) btnStop_Click(btnStop, null);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragMove();

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            if (teensy.IsOpen) teensy.Close();
            if (hookId != IntPtr.Zero) UnhookWindowsHookEx(hookId);
            Application.Current.Shutdown();
        }

        private void cbWindows_DropDownOpened(object sender, EventArgs e)
        {
            cbWindows.Items.Clear();
            foreach (var p in Process.GetProcesses())
            {
                if (p.MainWindowHandle != IntPtr.Zero &&
                    p.ProcessName.Contains("lu4", StringComparison.OrdinalIgnoreCase))
                {
                    cbWindows.Items.Add(new WindowItem(p.MainWindowHandle, p.MainWindowTitle));
                }
            }
        }

        private void btnStart_Click(object sender, RoutedEventArgs? e)
        {
            if (cbWindows.SelectedItem is not WindowItem wi)
            {
                MessageBox.Show("Выберите окно LU4.");
                return;
            }
            targetHwnd = wi.Hwnd;

            movementTimer.Start();
            keyTimer.Start();
            pickupTimer.Start();

            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        private void btnStop_Click(object sender, RoutedEventArgs? e)
        {
            movementTimer.Stop();
            keyTimer.Stop();
            pickupTimer.Stop();

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

            if (!GetClientRect(hwnd, out var rect)) return;
            var pt = new POINT();
            if (!ClientToScreen(hwnd, ref pt)) return;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(pt.X, pt.Y, 0, 0, bmp.Size);

            string name = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss}.bmp";
            bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name),
                     System.Drawing.Imaging.ImageFormat.Bmp);

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

            if (!GetClientRect(hwnd, out var rect)) return;
            var pt = new POINT();
            if (!ClientToScreen(hwnd, ref pt)) return;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;

            using var bmp = new Bitmap(w, h);
            using var gFull = Graphics.FromImage(bmp);
            gFull.CopyFromScreen(pt.X, pt.Y, 0, 0, bmp.Size);

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
            } while (iter.Next(PageIteratorLevel.Word));

            string name = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss} - OCR.bmp";
            bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name),
                     System.Drawing.Imaging.ImageFormat.Bmp);

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

            using var bmp = new Bitmap(latest.FullName);
            if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height)
            {
                MessageBox.Show("Координаты вне диапазона.");
                return;
            }
            lblR.Text = bmp.GetPixel(x, y).R.ToString();
        }

        //────────────────────────────────────────────────────────────────────────────
        // Глобальный хук клавиатуры
        //────────────────────────────────────────────────────────────────────────────
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == KeyInterop.VirtualKeyFromKey(Key.PageUp))
                    Dispatcher.Invoke(() => btnStart_Click(btnStart, null));
                else if (vk == KeyInterop.VirtualKeyFromKey(Key.PageDown))
                    Dispatcher.Invoke(() => btnStop_Click(btnStop, null));
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        //────────────────────────────────────────────────────────────────────────────
        // Логика поиска и клика
        //────────────────────────────────────────────────────────────────────────────
        private bool IsTargetVisible(IntPtr hwnd)
        {
            if (!GetClientRect(hwnd, out var rect)) return false;
            var pt = new POINT();
            if (!ClientToScreen(hwnd, ref pt)) return false;

            int w = rect.Right - rect.Left;
            using var bmp = new Bitmap(w, 1);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(pt.X, pt.Y, 0, 0, bmp.Size);

            for (int x = 0; x < w; x++)
            {
                var c = bmp.GetPixel(x, 0);
                if (c.R >= 240 && c.G < 30 && c.B < 30)
                    return true;
            }
            return false;
        }

        private bool IsTargetDeadAndLootable()
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(1526, 6, 0, 0, bmp.Size);

            int r = bmp.GetPixel(0, 0).R;
            return (r == 161 && !IsTargetVisible(targetHwnd));
        }

        private void MoveAndClick(IntPtr hwnd)
        {
            if (!GetClientRect(hwnd, out var rect)) return;
            var pt = new POINT();
            if (!ClientToScreen(hwnd, ref pt)) return;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            int centerX = pt.X + w / 2;
            int centerY = pt.Y + h / 2;

            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(pt.X, pt.Y, 0, 0, bmp.Size);

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

                int tx = pt.X + box.X1 + box.Width / 2;
                int ty = pt.Y + box.Y1 + box.Height / 2;
                if (!IsExcludedZone(tx, ty))
                    targets.Add((tx, ty));
            }
            while (iter.Next(PageIteratorLevel.Word));

            if (targets.Count == 0) return;

            var (x, y) = targets
                        .OrderBy(p => Math.Pow(p.X - centerX, 2)
                                     + Math.Pow(p.Y - centerY, 2))
                        .First();

            ClickTargetOnce(x, y);
        }

        private void ClickTargetOnce(int x, int y)
        {
            int tx = x - 10;
            int ty = y + 10;
            SetCursorPos(tx, ty);
            Thread.Sleep(300);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

        private bool IsExcludedZone(int x, int y)
        {
            var zones = new[]
            {
                (0,    0,    250,  110),
                (1040, 0,    1550, 30),
                (2350, 0,    2560, 235),
                (0,    960,  360,  1440),
                (1010, 1270, 1590, 1440),
                (2460, 1040, 2560, 1440)
            };
            return zones.Any(z => x >= z.Item1 && x <= z.Item3
                               && y >= z.Item2 && y <= z.Item4);
        }

        private IntPtr GetEffectiveHwnd()
        {
            if (targetHwnd != IntPtr.Zero) return targetHwnd;
            if (cbWindows.SelectedItem is WindowItem wi) return wi.Hwnd;
            return IntPtr.Zero;
        }

        private class WindowItem
        {
            public IntPtr Hwnd { get; }
            public string Title { get; }
            public WindowItem(IntPtr hwnd, string title)
                => (Hwnd, Title) = (hwnd, title);
            public override string ToString() => Title;
        }
    }
}