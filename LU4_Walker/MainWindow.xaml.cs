using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using System.Drawing;
using System.Drawing.Imaging;

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

        // Константы для сообщений Windows
        private const uint WM_KEYDOWN = 0x0100;
        private const int VK_NUM_DIV = 0x6F; // Код клавиши NumPad Divide
        private const uint SRCCOPY = 0x00CC0020; // Код для BitBlt

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

        public MainWindow()
        {
            InitializeComponent();
            InitializeTimer();
            LoadLU4Processes();
        }

        private void InitializeTimer()
        {
            findMonster = new DispatcherTimer();
            findMonster.Tick += new EventHandler(findMonster_Tick);
            findMonster.Interval = new TimeSpan(0, 0, 0, 0, 200);
        }

        private void LoadLU4Processes()
        {
            lu4Windows = new Dictionary<string, IntPtr>();
            ProcessComboBox.Items.Clear(); // Очистка текущего списка

            Process[] processes = Process.GetProcesses();
            foreach (Process proc in processes)
            {
                if (proc.MainWindowTitle.Contains("LU4") && proc.MainWindowHandle != IntPtr.Zero)
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
                MessageBox.Show("Процессы с названием LU4 не найдены!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                StartButton.IsEnabled = false;
                StopButton.IsEnabled = true;
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            findMonster.Stop();
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private void ProcessComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (findMonster.IsEnabled)
            {
                findMonster.Stop();
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
            }
        }

        private void ProcessComboBox_DropDownOpened(object sender, EventArgs e)
        {
            LoadLU4Processes(); // Обновление списка процессов при открытии ComboBox
        }

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            if (ProcessComboBox.SelectedItem == null)
            {
                MessageBox.Show("Выберите процесс LU4!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedWindow = ProcessComboBox.SelectedItem.ToString();
            IntPtr hWnd = lu4Windows[selectedWindow];

            // Получение размеров окна
            if (!GetWindowRect(hWnd, out RECT rect))
            {
                MessageBox.Show("Не удалось получить размеры окна!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            if (width <= 0 || height <= 0)
            {
                MessageBox.Show("Окно имеет некорректные размеры!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    // Открытие диалогового окна для сохранения
                    SaveFileDialog saveDialog = new SaveFileDialog
                    {
                        Filter = "BMP Files (*.bmp)|*.bmp",
                        Title = "Сохранить скриншот",
                        DefaultExt = "bmp",
                        AddExtension = true
                    };

                    if (saveDialog.ShowDialog() == true)
                    {
                        bitmap.Save(saveDialog.FileName, ImageFormat.Bmp);
                        MessageBox.Show("Скриншот успешно сохранен!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                DeleteObject(hBitmap);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при захвате скриншота: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
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