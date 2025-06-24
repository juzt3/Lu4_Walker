using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Interop;

namespace LU4_Walker
{
    public partial class MainWindow : Window
    {
        // Импорт функций WinAPI
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, int wParam, int lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight,
                                         IntPtr hdcSrc, int nXSrc, int nYSrc, uint dwRop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // Константы для сообщений Windows
        private const uint WM_KEYDOWN = 0x0100;
        private const int VK_NUM_DIV = 0x6F; // Код клавиши NumPad Divide
        private const uint SRCCOPY = 0x00CC0020; // Код для BitBlt
        private const int HOTKEY_ID_SCREENSHOT = 0x9000; // ID для Ctrl + F12
        private const int HOTKEY_ID_START = 0x9001; // ID для Page Up
        private const int HOTKEY_ID_STOP = 0x9002; // ID для Page Down
        private const uint MOD_CONTROL = 0x0002; // Модификатор Ctrl
        private const uint VK_F12 = 0x7B; // Код клавиши F12
        private const uint VK_PAGE_UP = 0x21; // Код клавиши Page Up
        private const uint VK_PAGE_DOWN = 0x22; // Код клавиши Page Down
        private const int WM_HOTKEY = 0x0312; // Сообщение для горячей клавиши

        // Структура для хранения координат окна
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private IntPtr BSFGhWnd = IntPtr.Zero;
        private DispatcherTimer findMonster;
        private Dictionary<string, IntPtr> lu4Windows;
        private HwndSource hwndSource;

        public MainWindow()
        {
            InitializeComponent();
            InitializeTimer();
            LoadLU4Processes();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Topmost = true; // Окно поверх всех при запуске
        }

        private void InitializeTimer()
        {
            findMonster = new DispatcherTimer();
            findMonster.Tick += new EventHandler(findMonster_Tick);
            findMonster.Interval = new TimeSpan(0, 0, 0, 0, 200);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Регистрация горячих клавиш
            try
            {
                hwndSource = PresentationSource.FromVisual(this) as HwndSource;
                if (hwndSource != null)
                {
                    hwndSource.AddHook(WndProc);
                    RegisterHotKey(hwndSource.Handle, HOTKEY_ID_SCREENSHOT, MOD_CONTROL, VK_F12); // Ctrl + F12
                    RegisterHotKey(hwndSource.Handle, HOTKEY_ID_START, 0, VK_PAGE_UP); // Page Up
                    RegisterHotKey(hwndSource.Handle, HOTKEY_ID_STOP, 0, VK_PAGE_DOWN); // Page Down
                }
            }
            catch
            {
                // Игнорируем ошибки регистрации
            }
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Снятие регистрации горячих клавиш
            try
            {
                if (hwndSource != null)
                {
                    UnregisterHotKey(hwndSource.Handle, HOTKEY_ID_SCREENSHOT);
                    UnregisterHotKey(hwndSource.Handle, HOTKEY_ID_START);
                    UnregisterHotKey(hwndSource.Handle, HOTKEY_ID_STOP);
                    hwndSource.RemoveHook(WndProc);
                    hwndSource = null;
                }
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                // Активация окна выбранного процесса
                if (ProcessComboBox.SelectedItem != null)
                {
                    string selectedWindow = ProcessComboBox.SelectedItem.ToString();
                    IntPtr hWnd = lu4Windows[selectedWindow];
                    SetForegroundWindow(hWnd);
                }

                if (hotkeyId == HOTKEY_ID_SCREENSHOT)
                {
                    TakeScreenshot();
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_START)
                {
                    StartButton_Click(null, null);
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_ID_STOP)
                {
                    StopButton_Click(null, null);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void LoadLU4Processes()
        {
            lu4Windows = new Dictionary<string, IntPtr>();
            ProcessComboBox.Items.Clear(); // Очистка текущего списка

            Process[] processes = Process.GetProcesses();
            foreach (Process proc in processes)
            {
                if (proc.MainWindowTitle.Contains("LU4") && proc.MainWindowHandle != IntPtr.Zero && proc.MainWindowTitle != "LU4 Walker")
                {
                    string displayName = $"{proc.MainWindowTitle} (PID: {proc.Id})";
                    lu4Windows.Add(displayName, proc.MainWindowHandle);
                    ProcessComboBox.Items.Add(displayName);
                }
            }

            if (lu4Windows.Count > 0)
            {
                ProcessComboBox.SelectedIndex = 0;
                StartButton.IsEnabled = true;
                ScreenshotButton.IsEnabled = true;
            }
            else
            {
                StartButton.IsEnabled = false;
                ScreenshotButton.IsEnabled = false;
            }
        }

        private void findMonster_Tick(object sender, EventArgs e)
        {
            if (BSFGhWnd != IntPtr.Zero)
            {
                PostMessage(BSFGhWnd, WM_KEYDOWN, VK_NUM_DIV, 0);
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessComboBox.SelectedItem != null)
            {
                string selectedWindow = ProcessComboBox.SelectedItem.ToString();
                BSFGhWnd = lu4Windows[selectedWindow];
                findMonster.Start();
                StartButton.Visibility = Visibility.Collapsed;
                StopButton.Visibility = Visibility.Visible;
                Topmost = false; // Окно на задний план
                WindowState = WindowState.Minimized; // Сворачивание окна
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            findMonster.Stop();
            StartButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Collapsed;
            Topmost = true; // Окно поверх всех
            WindowState = WindowState.Normal; // Восстановление окна
        }

        private void ProcessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (findMonster.IsEnabled)
            {
                findMonster.Stop();
                StartButton.Visibility = Visibility.Visible;
                StopButton.Visibility = Visibility.Collapsed;
                Topmost = true; // Окно поверх всех
                WindowState = WindowState.Normal; // Восстановление окна
            }
        }

        private void ProcessComboBox_DropDownOpened(object sender, EventArgs e)
        {
            LoadLU4Processes(); // Обновление списка процессов при открытии ComboBox
        }

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            // Активация окна процесса при нажатии кнопки
            if (ProcessComboBox.SelectedItem != null)
            {
                string selectedWindow = ProcessComboBox.SelectedItem.ToString();
                IntPtr hWnd = lu4Windows[selectedWindow];
                SetForegroundWindow(hWnd);
            }
            TakeScreenshot();
        }

        private void TakeScreenshot()
        {
            if (ProcessComboBox.SelectedItem == null)
            {
                return;
            }

            string selectedWindow = ProcessComboBox.SelectedItem.ToString();
            IntPtr hWnd = lu4Windows[selectedWindow];

            // Получение размеров окна
            if (!GetWindowRect(hWnd, out RECT rect))
            {
                return;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            // Захват скриншота окна
            try
            {
                IntPtr hdcSrc = GetWindowDC(hWnd);
                IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
                IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, width, height);
                IntPtr hOld = SelectObject(hdcDest, hBitmap);

                BitBlt(hdcDest, 0, 0, width, height, hdcSrc, 0, 0, SRCCOPY);

                SelectObject(hdcDest, hOld);
                DeleteDC(hdcDest);
                ReleaseDC(hWnd, hdcSrc);

                using (Bitmap bitmap = Bitmap.FromHbitmap(hBitmap))
                {
                    // Получение пути к папке с .exe
                    string exePath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    string datePrefix = DateTime.Now.ToString("dd.MM.yyyy");
                    int nextNumber = 1;

                    // Поиск следующего доступного номера
                    string[] existingFiles = Directory.GetFiles(exePath, $"{datePrefix} - *.bmp");
                    if (existingFiles.Length > 0)
                    {
                        nextNumber = existingFiles
                            .Select(f => Path.GetFileNameWithoutExtension(f).Split('-').Last().Trim())
                            .Select(n => int.TryParse(n, out int num) ? num : 0)
                            .DefaultIfEmpty(0)
                            .Max() + 1;
                    }

                    // Формирование имени файла
                    string fileName = $"{datePrefix} - {nextNumber:D3}.bmp";
                    string filePath = Path.Combine(exePath, fileName);

                    // Сохранение скриншота
                    bitmap.Save(filePath, ImageFormat.Bmp);
                }

                DeleteObject(hBitmap);
            }
            catch
            {
                // Игнорируем ошибки
            }
        }

        // Метод для остановки таймера при закрытии приложения
        protected override void OnClosed(EventArgs e)
        {
            findMonster.Stop();
            base.OnClosed(e);
        }
    }
}