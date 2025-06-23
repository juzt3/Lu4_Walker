using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Linq;

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

        // Константы для сообщений Windows
        private const uint WM_KEYDOWN = 0x0100;
        private const int VK_NUM_DIV = 0x6F; // Код клавиши NumPad Divide

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
            }
            else
            {
                MessageBox.Show("Процессы с названием LU4 не найдены!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StartButton.IsEnabled = false;
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

        // Метод для остановки таймера при закрытии приложения
        protected override void OnClosed(EventArgs e)
        {
            findMonster.Stop();
            base.OnClosed(e);
        }
    }
}