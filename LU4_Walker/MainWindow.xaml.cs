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
using System.Threading;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace LU4_Walker
{
    public partial class MainWindow : Window
    {
        // Импорт функций WinAPI
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

        // Константы для сообщений Windows
        private const uint WM_KEYDOWN = 0x0100;
        private const int HOTKEY_ID_SCREENSHOT = 0x9000; // ID для Ctrl + F12
        private const int HOTKEY_ID_START = 0x9001; // ID для Page Up
        private const int HOTKEY_ID_STOP = 0x9002; // ID для Page Down
        private const int HOTKEY_ID_CHECK_PIXEL = 0x9003; // ID для End
        private const uint MOD_CONTROL = 0x0002; // Модификатор Ctrl
        private const int WM_HOTKEY = 0x0312; // Сообщение для горячей клавиши
        private const int SW_RESTORE = 9; // Флаг для восстановления окна

        // Константы для keybd_event
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // Виртуальные коды клавиш и сообщения мыши
        private static ushort VK_F1 = 0x70;
        private static ushort VK_F2 = 0x71;
        private static ushort VK_F3 = 0x72;
        private static ushort VK_F4 = 0x73;
        private static ushort VK_F5 = 0x74;
        private static ushort VK_F6 = 0x75;
        private static ushort VK_F7 = 0x76;
        private static ushort VK_F8 = 0x77;
        private static ushort VK_F9 = 0x78;
        private static ushort VK_F10 = 0x79;
        private static ushort VK_F11 = 0x7A;
        private static ushort VK_F12 = 0x7B;
        private static ushort VK_ESC = 0x1B;
        private static ushort VK_PAGE_UP = 0x21;
        private static ushort VK_PAGE_DOWN = 0x22;
        private static ushort VK_END = 0x23;
        private static ushort WM_LBUTTONDOWN = 0x0201;
        private static ushort WM_LBUTTONUP = 0x0202;
        private const uint MK_LBUTTON = 0x0001;
        private const uint MK_RBUTTON = 0x0002;
        private static ushort WM_RBUTTONDOWN = 0x0204;
        private static ushort WM_RBUTTONUP = 0x0205;
        private static ushort VK_1 = 0x31;
        private static ushort VK_2 = 0x32;
        private static ushort VK_3 = 0x33;
        private static ushort VK_4 = 0x34;
        private static ushort VK_5 = 0x35;
        private static ushort VK_6 = 0x36;
        private static ushort VK_7 = 0x37;
        private static ushort VK_8 = 0x38;
        private static ushort VK_9 = 0x39;
        private static ushort VK_0 = 0x30;
        private static ushort VK_MINUS = 0xBD;
        private static ushort VK_PLUS = 0xBB;
        private static ushort VK_NUM1 = 0x61;
        private static ushort VK_NUM2 = 0x62;
        private static ushort VK_NUM3 = 0x63;
        private static ushort VK_NUM4 = 0x64;
        private static ushort VK_NUM5 = 0x65;
        private static ushort VK_NUM6 = 0x66;
        private static ushort VK_NUM7 = 0x67;
        private static ushort VK_NUM8 = 0x68;
        private static ushort VK_NUM9 = 0x69;
        private static ushort VK_NUM0 = 0x60;
        private static ushort VK_NUM_MULT = 0x6A;
        private static ushort VK_NUM_DIV = 0x6F;

        // Структура для хранения координат окна
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        // Структура для преобразования координат
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private DispatcherTimer checkRedPixelTimer;
        private Dictionary<string, IntPtr> lu4Windows;
        private HwndSource hwndSource;
        private IntPtr selectedHwnd = IntPtr.Zero; // Хранит дескриптор второго окна
        private readonly object hwndLock = new object(); // Для потокобезопасного доступа к selectedHwnd

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
            checkRedPixelTimer = new DispatcherTimer();
            checkRedPixelTimer.Tick += new EventHandler(CheckRedPixelTimer_Tick);
            checkRedPixelTimer.Interval = TimeSpan.FromMilliseconds(1000); // Задержка 1000 мс
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
                    RegisterHotKey(hwndSource.Handle, HOTKEY_ID_CHECK_PIXEL, 0, VK_END); // End
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
                    UnregisterHotKey(hwndSource.Handle, HOTKEY_ID_CHECK_PIXEL);
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
                else if (hotkeyId == HOTKEY_ID_CHECK_PIXEL)
                {
                    CheckPixelColorButton_Click(null, null);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void LoadLU4Processes()
        {
            lu4Windows = new Dictionary<string, IntPtr>();
            ProcessComboBox.Items.Clear(); // Очистка текущего списка

            Process[] processes = Process.GetProcesses()
                .Where(proc => proc.MainWindowTitle.Contains("LU4") && proc.MainWindowHandle != IntPtr.Zero && proc.MainWindowTitle != "LU4 Walker")
                .OrderBy(proc => proc.Id) // Сортировка по PID для предсказуемости
                .ToArray();

            if (processes.Length > 1) // Выбираем второй процесс LU4
            {
                Process secondProcess = processes[1];
                string displayName = $"{secondProcess.MainWindowTitle} (PID: {secondProcess.Id})";
                lu4Windows.Add(displayName, secondProcess.MainWindowHandle);
                ProcessComboBox.Items.Add(displayName);
                ProcessComboBox.SelectedIndex = 0;
                StartButton.IsEnabled = true;
                ScreenshotButton.IsEnabled = true;
                CheckPixelColorButton.IsEnabled = true;
            }
            else
            {
                StartButton.IsEnabled = false;
                ScreenshotButton.IsEnabled = false;
                CheckPixelColorButton.IsEnabled = false;
                PixelColorTextBox.Text = "Второй процесс LU4 не найден";
            }
        }

        private void SendKeyPress(byte virtualKey)
        {
            // Нажатие клавиши
            keybd_event((byte)virtualKey, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
            Thread.Sleep(10); // Короткая задержка для обработки нажатия
            // Отпускание клавиши
            keybd_event((byte)virtualKey, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        }

        private async void CheckRedPixelTimer_Tick(object sender, EventArgs e)
        {
            IntPtr currentHwnd;
            lock (hwndLock)
            {
                currentHwnd = selectedHwnd;
            }

            if (currentHwnd == IntPtr.Zero)
            {
                return;
            }

            // Проверка пикселя в фоновом потоке
            var (found, x, y) = await Task.Run(() => PixelColorChecker.FindFirstRedPixel(currentHwnd));

            // Отправка клавиш в основном потоке
            await Dispatcher.InvokeAsync(() =>
            {
                // Проверяем, минимизировано ли окно, и восстанавливаем
                if (IsIconic(currentHwnd))
                {
                    ShowWindow(currentHwnd, SW_RESTORE);
                }

                // Активируем окно перед отправкой клавиш
                SetForegroundWindow(currentHwnd);
                Thread.Sleep(50); // Даём время на фокусировку

                if (found)
                {
                    // Красный пиксель найден, отправляем 1–5
                    PixelColorTextBox.Text = $"Red pixel found at ({x}, {y}), sending 1–5";
                    SendKeyPress((byte)VK_1);
                    Thread.Sleep(100); // Задержка 100 мс
                    SendKeyPress((byte)VK_2);
                    Thread.Sleep(100);
                    SendKeyPress((byte)VK_3);
                    Thread.Sleep(100);
                    SendKeyPress((byte)VK_4);
                    Thread.Sleep(100);
                    SendKeyPress((byte)VK_5);
                }
                else
                {
                    // Красный пиксель не найден, отправляем 6–=
                    PixelColorTextBox.Text = "No red pixel found, sending 6–=";
                    SendKeyPress((byte)VK_6);
                    Thread.Sleep(100);
                    SendKeyPress((byte)VK_7);
                    Thread.Sleep(100);
                    SendKeyPress((byte)VK_8);
                    Thread.Sleep(100);
                    SendKeyPress((byte)VK_9);
                    Thread.Sleep(100);
                    SendKeyPress((byte)VK_0);
                    Thread.Sleep(100);
                    SendKeyPress((byte)VK_MINUS);
                    Thread.Sleep(100);
                    SendKeyPress((byte)VK_PLUS);
                }
            });
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessComboBox.SelectedItem != null)
            {
                string selectedWindow = ProcessComboBox.SelectedItem.ToString();
                lock (hwndLock)
                {
                    selectedHwnd = lu4Windows[selectedWindow];
                }
                ProcessComboBox.IsEnabled = false; // Блокировка ComboBox
                checkRedPixelTimer.Start();
                StartButton.Visibility = Visibility.Collapsed;
                StopButton.Visibility = Visibility.Visible;
                Topmost = false; // Окно на задний план
                WindowState = WindowState.Minimized; // Сворачивание окна
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            checkRedPixelTimer.Stop();
            lock (hwndLock)
            {
                selectedHwnd = IntPtr.Zero; // Очистка дескриптора
            }
            ProcessComboBox.IsEnabled = true; // Разблокировка ComboBox
            StartButton.Visibility = Visibility.Visible;
            StopButton.Visibility = Visibility.Collapsed;
            Topmost = true; // Окно поверх всех
            WindowState = WindowState.Normal; // Восстановление окна
            PixelColorTextBox.Text = ""; // Очистка поля цвета
        }

        private void ProcessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (checkRedPixelTimer.IsEnabled)
            {
                checkRedPixelTimer.Stop();
                lock (hwndLock)
                {
                    selectedHwnd = IntPtr.Zero; // Очистка дескриптора
                }
                ProcessComboBox.IsEnabled = true; // Разблокировка ComboBox
                StartButton.Visibility = Visibility.Visible;
                StopButton.Visibility = Visibility.Collapsed;
                Topmost = true; // Окно поверх всех
                WindowState = WindowState.Normal; // Восстановление окна
                PixelColorTextBox.Text = "";
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

        private void CheckPixelColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessComboBox.SelectedItem == null)
            {
                PixelColorTextBox.Text = "";
                return;
            }

            string selectedWindow = ProcessComboBox.SelectedItem.ToString();
            IntPtr hWnd = lu4Windows[selectedWindow];

            // Проверка введённых координат
            if (!int.TryParse(XTextBox.Text, out int x) || !int.TryParse(YTextBox.Text, out int y))
            {
                PixelColorTextBox.Text = "";
                return;
            }

            // Проверка всех компонент цвета (R, G, B)
            int r = PixelColorChecker.CheckPixelColor("R", hWnd, x, y);
            int g = PixelColorChecker.CheckPixelColor("G", hWnd, x, y);
            int b = PixelColorChecker.CheckPixelColor("B", hWnd, x, y);

            if (r == -1 || g == -1 || b == -1)
            {
                PixelColorTextBox.Text = "";
                return;
            }

            PixelColorTextBox.Text = $"R: {r}, G: {g}, B: {b}";
        }

        private void TakeScreenshot()
        {
            if (ProcessComboBox.SelectedItem == null)
            {
                return;
            }

            string selectedWindow = ProcessComboBox.SelectedItem.ToString();
            IntPtr hWnd = lu4Windows[selectedWindow];

            // Проверка, минимизировано ли окно, и восстановление
            if (IsIconic(hWnd))
            {
                ShowWindow(hWnd, SW_RESTORE);
            }

            // Активация окна для корректного рендеринга
            SetForegroundWindow(hWnd);

            // Задержка для рендеринга окна
            Thread.Sleep(50);

            // Получение размеров клиентской области
            if (!GetClientRect(hWnd, out RECT clientRect))
            {
                return;
            }

            // Преобразование координат клиентской области в экранные
            POINT topLeft = new POINT { X = clientRect.Left, Y = clientRect.Top };
            if (!ClientToScreen(hWnd, ref topLeft))
            {
                return;
            }

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;

            if (width <= 0 || height <= 0)
            {
                return;
            }

            // Захват скриншота клиентской области
            try
            {
                using (Bitmap bitmap = new Bitmap(width, height))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        // Учёт DPI-шкалирования
                        float dpiScaleX = graphics.DpiX / 96.0f;
                        float dpiScaleY = graphics.DpiY / 96.0f;
                        int scaledX = (int)(topLeft.X / dpiScaleX);
                        int scaledY = (int)(topLeft.Y / dpiScaleY);
                        int scaledWidth = (int)(width / dpiScaleX);
                        int scaledHeight = (int)(height / dpiScaleY);

                        graphics.CopyFromScreen(scaledX, scaledY, 0, 0, new System.Drawing.Size(scaledWidth, scaledHeight));
                    }

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
            }
            catch
            {
                // Игнорируем ошибки
            }
        }
    }
}