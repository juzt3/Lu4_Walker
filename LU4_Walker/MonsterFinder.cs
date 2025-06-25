using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;

namespace LU4_Walker
{
    public static class MonsterFinder
    {
        // Импорт функций WinAPI
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, IntPtr dwExtraInfo);

        // Константы для keybd_event
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const int SW_RESTORE = 9; // Флаг для восстановления окна

        // Виртуальные коды клавиш
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

        /// <summary>
        /// Проверяет наличие красного пикселя и отправляет клавиши в зависимости от результата.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна.</param>
        /// <param name="textBox">TextBlock для вывода результата.</param>
        public static async void FindMonsterTick(IntPtr hWnd, TextBlock textBox)
        {
            if (hWnd == IntPtr.Zero)
            {
                return;
            }

            // Проверка пикселя в фоновом потоке
            var (found, x, y) = await Task.Run(() => RedPixelChecker.CheckRedPixel(hWnd));

            // Отправка клавиш в основном потоке
            await Dispatcher.CurrentDispatcher.InvokeAsync(() =>
            {
                // Проверяем, минимизировано ли окно, и восстанавливаем
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }

                // Активируем окно перед отправкой клавиш
                SetForegroundWindow(hWnd);
                System.Threading.Thread.Sleep(50); // Даём время на фокусировку

                if (found)
                {
                    // Красный пиксель найден, отправляем 1–5
                    textBox.Text = $"Red pixel found at ({x}, {y}), sending 1–5";
                    SendKeyPress((byte)VK_1);
                    System.Threading.Thread.Sleep(100); // Задержка 100 мс
                    SendKeyPress((byte)VK_2);
                    System.Threading.Thread.Sleep(100);
                    SendKeyPress((byte)VK_3);
                    System.Threading.Thread.Sleep(100);
                    SendKeyPress((byte)VK_4);
                    System.Threading.Thread.Sleep(100);
                    SendKeyPress((byte)VK_5);
                }
                else
                {
                    // Красный пиксель не найден, отправляем 6–=
                    textBox.Text = "No red pixel found, sending 6–=";
                    SendKeyPress((byte)VK_6);
                    System.Threading.Thread.Sleep(100);
                    SendKeyPress((byte)VK_7);
                    System.Threading.Thread.Sleep(100);
                    SendKeyPress((byte)VK_8);
                    System.Threading.Thread.Sleep(100);
                    SendKeyPress((byte)VK_9);
                    System.Threading.Thread.Sleep(100);
                    SendKeyPress((byte)VK_0);
                    System.Threading.Thread.Sleep(100);
                    SendKeyPress((byte)VK_MINUS);
                    System.Threading.Thread.Sleep(100);
                    SendKeyPress((byte)VK_PLUS);
                }
            });
        }

        private static void SendKeyPress(byte virtualKey)
        {
            // Нажатие клавиши
            keybd_event(virtualKey, 0, KEYEVENTF_KEYDOWN, IntPtr.Zero);
            System.Threading.Thread.Sleep(10); // Короткая задержка для обработки нажатия
            // Отпускание клавиши
            keybd_event(virtualKey, 0, KEYEVENTF_KEYUP, IntPtr.Zero);
        }
    }
}