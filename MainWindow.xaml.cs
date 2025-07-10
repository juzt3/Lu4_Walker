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
using System.Threading.Tasks;
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
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")] static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        private const int WH_KEYBOARD_LL = 13;

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

        private int cycleCount = 0;

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        private readonly DispatcherTimer movementTimer = new();
        private readonly DispatcherTimer keyTimer = new();
        private readonly DispatcherTimer pickupTimer = new();

        private SerialPort teensy = null!;
        private IntPtr targetHwnd = IntPtr.Zero;

        private bool IsTargetDeadAndLootable()
        {
            using var bmp = new Bitmap(1, 1);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(1526, 6, 0, 0, bmp.Size);

            var r = bmp.GetPixel(0, 0).R;
            return (r == 161 && !IsTargetVisible(targetHwnd));
        }

        private void InitializePickupTimer()
        {
            pickupTimer.Interval = TimeSpan.FromMilliseconds(400);
            pickupTimer.Tick += (_, __) =>
            {
                _ = Task.Run(async () =>
                {
                    if (targetHwnd == IntPtr.Zero) return;
                    if (IsTargetDeadAndLootable())
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            teensy.Write("J");
                            await Task.Delay(150);
                        }
                        await Task.Delay(300);
                        teensy.Write("x");
                    }
                });
            };
        }

        public MainWindow()
        {
            InitializeComponent();
            InitializePickupTimer();

            proc = HookCallback;
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

            movementTimer.Interval = TimeSpan.FromMilliseconds(400);
            movementTimer.Tick += (_, __) =>
            {
                _ = Task.Run(() =>
                {
                    if (targetHwnd == IntPtr.Zero) return;

                    if (!IsTargetVisible(targetHwnd) && !IsTargetDeadAndLootable())
                    {
                        if (cycleCount % 10 == 0)
                        {
                            MoveAndClick(targetHwnd);
                        }
                        else
                        {
                            teensy.Write("L");
                        }

                        cycleCount++;
                    }
                });
            };

            keyTimer.Interval = TimeSpan.FromMilliseconds(400);
            keyTimer.Tick += (_, __) =>
            {
                _ = Task.Run(() =>
                {
                    if (targetHwnd != IntPtr.Zero && IsTargetVisible(targetHwnd))
                        teensy.Write("1");
                });
            };
        }

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
            pickupTimer.Start();
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            movementTimer.Stop();
            keyTimer.Stop();
            pickupTimer.Stop();
            targetHwnd = IntPtr.Zero;
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
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

        private void ClickTargetOnce(int x, int y)
        {
            int tx = x - 10;
            int ty = y + 10;

            SetCursorPos(tx, ty);
            Thread.Sleep(100);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

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

        private void MoveAndClick(IntPtr hwnd)
        {
            // Упрощаем, чтобы бот работал стабильно
            SetCursorPos(100, 100);
            Thread.Sleep(100);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
        }

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

        private IntPtr GetEffectiveHwnd()
        {
            if (targetHwnd != IntPtr.Zero)
                return targetHwnd;
            if (cbWindows.SelectedItem is WindowItem wi)
                return wi.Hwnd;
            return IntPtr.Zero;
        }

        // ==== Заглушки для устранения ошибок ====

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Заглушка, можно добавить хоткеи если нужно
        }

        private void btnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Функция скриншота временно отключена.");
        }

        private void btnOcrScreenshot_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Функция OCR временно отключена.");
        }

        private void btnGetR_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Функция проверки R временно отключена.");
        }
    }
}
