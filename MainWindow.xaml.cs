using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int X, int Y);


        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        const int WH_KEYBOARD_LL = 13;
        const int WM_KEYDOWN = 0x0100;
        delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        #endregion

        private readonly DispatcherTimer attackTimer = new();
        private readonly DispatcherTimer searchTimer = new();
        private readonly DispatcherTimer pickUpTimer = new();
        private readonly DispatcherTimer findHelper = new();

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

            attackTimer.Interval = TimeSpan.FromMilliseconds(800);
            searchTimer.Interval = TimeSpan.FromMilliseconds(1200);
            pickUpTimer.Interval = TimeSpan.FromMilliseconds(1000);
            findHelper.Interval = TimeSpan.FromMilliseconds(5000);
            attackTimer.Tick += AttackTimer_Tick;
            searchTimer.Tick += SearchTimer_Tick;
            pickUpTimer.Tick += PickUpTimer_Tick;
            findHelper.Tick += findHelper_Tick;

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

        // 🎯 Поиск цели (J = F10)
        private async void SearchTimer_Tick(object? sender, EventArgs e)
        {
            if (targetHwnd == IntPtr.Zero) return;

            bool targetVisible = await Task.Run(() => HP_Target_Monster.Scan(targetHwnd));
            bool chosen = await Task.Run(() => Target_Chosen.IsSelected(targetHwnd));

            if (!targetVisible && !chosen)
            {
                // 🔍 Цель не найдена — ищем
                await Task.Run(() =>
                {
                    teensy.Write("J");
                    System.Threading.Thread.Sleep(80);
                });
            }
        }


        // 🗡️ Атака (1)
        private async void AttackTimer_Tick(object? sender, EventArgs e)
        {
            if (targetHwnd == IntPtr.Zero) return;

            bool targetVisible = await Task.Run(() => HP_Target_Monster.Scan(targetHwnd));


            if (targetVisible)
           {
                // 🎯 Цель найдена — атакуем
                await Task.Run(() =>
                {
                    teensy.Write("1");
                    System.Threading.Thread.Sleep(80);
                });
            }
        }

        private async void PickUpTimer_Tick(object? sender, EventArgs e)
        {
            if (targetHwnd == IntPtr.Zero) return;

            bool chosen = await Task.Run(() => Target_Chosen.IsSelected(targetHwnd));
            bool targetVisible = await Task.Run(() => HP_Target_Monster.Scan(targetHwnd));

            if (chosen && !targetVisible)
            {
                teensy.Write("L");                         // Зажим клавиши L (F12)
                await Task.Delay(1500);                    // Держим 2 секунды
                teensy.Write("X");                         // Нажимаем клавишу X (Escape)
                
            }
        }

        private async void findHelper_Tick(object? sender, EventArgs e)
        {
            if (targetHwnd == IntPtr.Zero) return;

            var point = await Task.Run(() => FindNameOfMonster.FindTargetPixel(targetHwnd));
            if (point.HasValue)
            {
                SetCursorPos(point.Value.X, point.Value.Y);
            }
        }







        // 🧠 Глобальный хук клавиш
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

        // ⏯️ Старт / Стоп
        private void StartTimers()
        {
            if (targetHwnd == IntPtr.Zero)
            {
                MessageBox.Show("Выберите окно LU4", "Ошибка");
                return;
            }

            attackTimer.Start();
            searchTimer.Start();
            pickUpTimer.Start();
            findHelper.Start();
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        private void StopTimers()
        {
            attackTimer.Stop();
            searchTimer.Stop();
            pickUpTimer.Stop();
            findHelper.Stop();
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        // 📸 Скриншот по HWND
        private void btnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (targetHwnd == IntPtr.Zero)
            {
                MessageBox.Show("Выберите окно LU4");
                return;
            }

            GetClientRect(targetHwnd, out var rc);
            var origin = new POINT();
            ClientToScreen(targetHwnd, ref origin);

            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;

            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(origin.X, origin.Y, 0, 0, bmp.Size);

            string file = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss}.bmp";
            bmp.Save(file);
            MessageBox.Show($"Скриншот сохранён:\n{file}");
        }

        // 🔍 OCR скрин по HWND
        private void btnOcrScreenshot_Click(object sender, RoutedEventArgs e)
        {
            if (targetHwnd == IntPtr.Zero)
            {
                MessageBox.Show("Выберите окно LU4");
                return;
            }

            GetClientRect(targetHwnd, out var rc);
            var origin = new POINT();
            ClientToScreen(targetHwnd, ref origin);

            int w = rc.Right - rc.Left;
            int h = rc.Bottom - rc.Top;

            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(origin.X, origin.Y, 0, 0, bmp.Size);

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
            MessageBox.Show($"OCR-скрин сохранён:\n{file}");
        }

        // 🪟 Комбо-бокс выбора окна
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

        private void cbWindows_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cbWindows.SelectedItem is WindowItem wi)
                targetHwnd = wi.Hwnd;
        }

        // ❌ Закрытие
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // 🖱️ Перетаскивание окна
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        // 🎁 Класс для ComboBox
        private class WindowItem
        {
            public IntPtr Hwnd { get; }
            public string Title { get; }
            public WindowItem(IntPtr hwnd, string title) => (Hwnd, Title) = (hwnd, title);
            public override string ToString() => Title;
        }
        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            StartTimers();
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            StopTimers();
        }

    }
}
