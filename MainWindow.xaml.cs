// MainWindow.xaml.cs

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Tesseract;

namespace LU4_Walker
{
    public partial class MainWindow : Window
    {
        #region WinAPI

        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string lpModuleName);

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        #endregion

        private readonly DispatcherTimer attackTimer = new DispatcherTimer();
        private readonly DispatcherTimer searchTimer = new DispatcherTimer();
        private readonly LowLevelKeyboardProc hookProc;
        private readonly SerialPort teensy;
        private IntPtr hookId = IntPtr.Zero;
        private IntPtr targetHwnd = IntPtr.Zero;

        public MainWindow()
        {
            InitializeComponent();

            teensy = new SerialPort("COM3", 9600);
            try { teensy.Open(); }
            catch (Exception ex)
            {
                MessageBox.Show($"Не удалось открыть COM3:\n{ex.Message}", "COM-порт", MessageBoxButton.OK);
            }

            attackTimer.Interval = TimeSpan.FromSeconds(1);
            searchTimer.Interval = TimeSpan.FromSeconds(2);
            attackTimer.Tick += AttackTimer_Tick;
            searchTimer.Tick += SearchTimer_Tick;

            hookProc = HookCallback;
            Loaded += (_, __) =>
            {
                using var mod = Process.GetCurrentProcess().MainModule!;
                hookId = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(mod.ModuleName), 0);
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            if (hookId != IntPtr.Zero) UnhookWindowsHookEx(hookId);
            base.OnClosed(e);
        }

        // ── Обработчики XAML ──

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void cbWindows_DropDownOpened(object sender, EventArgs e)
        {
            cbWindows.Items.Clear();
            foreach (var p in Process.GetProcesses()
                                      .Where(p => p.MainWindowHandle != IntPtr.Zero
                                               && p.ProcessName.Contains("lu4", StringComparison.OrdinalIgnoreCase)))
            {
                cbWindows.Items.Add(new WindowItem(p.MainWindowHandle, p.MainWindowTitle));
            }
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (cbWindows.SelectedItem is not WindowItem wi)
            {
                MessageBox.Show("Выберите окно LU4", "Ошибка", MessageBoxButton.OK);
                return;
            }

            targetHwnd = wi.Hwnd;
            attackTimer.Start();
            searchTimer.Start();
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            attackTimer.Stop();
            searchTimer.Stop();
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        // ── Горячие клавиши ──

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
            {
                int vk = Marshal.ReadInt32(lParam);
                if (vk == KeyInterop.VirtualKeyFromKey(Key.PageUp))
                    Dispatcher.Invoke(() => btnStart_Click(null!, null!));
                else if (vk == KeyInterop.VirtualKeyFromKey(Key.PageDown))
                    Dispatcher.Invoke(() => btnStop_Click(null!, null!));
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        // ── Таймеры: работа через Teensy ──

        private async void AttackTimer_Tick(object? sender, EventArgs e)
        {
            await Task.Run(() =>
            {
                teensy.Write("1");       // жмёт клавишу '1'
                System.Threading.Thread.Sleep(80);
            });
        }

        private async void SearchTimer_Tick(object? sender, EventArgs e)
        {
            await Task.Run(() =>
            {
                teensy.Write("J");       // жмёт F10
                System.Threading.Thread.Sleep(80);
            });
        }

        // ── Скриншот по HWND ──

        private void btnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (targetHwnd == IntPtr.Zero)
            {
                MessageBox.Show("Выберите окно LU4");
                return;
            }

            GetClientRect(targetHwnd, out var rc);
            var org = new POINT();
            ClientToScreen(targetHwnd, ref org);

            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;

            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(org.X, org.Y, 0, 0, bmp.Size);

            string file = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss}.bmp";
            bmp.Save(file);
            MessageBox.Show($"Скриншот сохранён:\n{file}", "Готово");
        }

        // ── OCR скрин по HWND ──

        private void btnOcrScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (targetHwnd == IntPtr.Zero)
            {
                MessageBox.Show("Выберите окно LU4");
                return;
            }

            GetClientRect(targetHwnd, out var rc);
            var org = new POINT();
            ClientToScreen(targetHwnd, ref org);

            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;

            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(org.X, org.Y, 0, 0, bmp.Size);

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
                    g.DrawRectangle(pen, box.X1, box.Y1, box.Width, box.Height);
            }
            while (iter.Next(PageIteratorLevel.Word));

            string file = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss} - OCR.bmp";
            bmp.Save(file);
            MessageBox.Show($"OCR-скрин сохранён:\n{file}", "Готово");
        }

        // ── Обёртка для ComboBox ──

        private class WindowItem
        {
            public IntPtr Hwnd { get; }
            public string Title { get; }
            public WindowItem(IntPtr hwnd, string title) => (Hwnd, Title) = (hwnd, title);
            public override string ToString() => Title;
        }
    }
}
