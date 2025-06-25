using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;

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
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        // Константы для сообщений Windows
        private const int HOTKEY_ID_SCREENSHOT = 0x9000; // ID для Ctrl + F12
        private const int HOTKEY_ID_START = 0x9001; // ID для Page Up
        private const int HOTKEY_ID_STOP = 0x9002; // ID для Page Down
        private const int HOTKEY_ID_CHECK_PIXEL = 0x9003; // ID для End
        private const uint MOD_CONTROL = 0x0002; // Модификатор Ctrl
        private const int WM_HOTKEY = 0x0312; // Сообщение для горячей клавиши

        // Виртуальные коды клавиш
        private static ushort VK_PAGE_UP = 0x21;
        private static ushort VK_PAGE_DOWN = 0x22;
        private static ushort VK_END = 0x23;
        private static ushort VK_F12 = 0x7B;

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
            checkRedPixelTimer.Tick += (s, e) => MonsterFinder.FindMonsterTick(selectedHwnd, PixelColorTextBox);
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
                    // SetForegroundWindow(hWnd); // Убрано для избежания фокусировки
                }

                if (hotkeyId == HOTKEY_ID_SCREENSHOT)
                {
                    ScreenshotMaker.TakeScreenshot(selectedHwnd);
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
                    PixelColorInspector.InspectPixelColor(selectedHwnd, XTextBox.Text, YTextBox.Text, PixelColorTextBox);
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
            if (ProcessComboBox.SelectedItem != null)
            {
                string selectedWindow = ProcessComboBox.SelectedItem.ToString();
                IntPtr hWnd = lu4Windows[selectedWindow];
                ScreenshotMaker.TakeScreenshot(hWnd);
            }
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
            PixelColorInspector.InspectPixelColor(hWnd, XTextBox.Text, YTextBox.Text, PixelColorTextBox);
        }
    }
}