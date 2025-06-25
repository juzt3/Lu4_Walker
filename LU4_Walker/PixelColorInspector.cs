using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Controls;

namespace LU4_Walker
{
    public static class PixelColorInspector
    {
        // Импорт функций WinAPI
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // Константы
        private const int SW_RESTORE = 9; // Флаг для восстановления окна

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

        /// <summary>
        /// Проверяет цвет пикселя в окне по координатам X, Y и возвращает значение красной компоненты (R).
        /// </summary>
        /// <param name="hWnd">Дескриптор окна процесса.</param>
        /// <param name="xText">Текст координаты X.</param>
        /// <param name="yText">Текст координаты Y.</param>
        /// <param name="textBox">TextBlock для вывода результата.</param>
        public static void InspectPixelColor(IntPtr hWnd, string xText, string yText, TextBlock textBox)
        {
            try
            {
                // Проверка валидности дескриптора окна
                if (hWnd == IntPtr.Zero)
                {
                    textBox.Text = "";
                    return;
                }

                // Проверка, минимизировано ли окно, и восстановление
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }

                // Активация окна для корректного рендеринга
                SetForegroundWindow(hWnd);

                // Задержка для рендеринга окна
                Thread.Sleep(50);

                // Проверка введённых координат
                if (!int.TryParse(xText, out int x) || !int.TryParse(yText, out int y))
                {
                    textBox.Text = "";
                    return;
                }

                // Получение размеров клиентской области
                if (!GetClientRect(hWnd, out RECT clientRect))
                {
                    textBox.Text = "";
                    return;
                }

                int width = clientRect.Right - clientRect.Left;
                int height = clientRect.Bottom - clientRect.Top;

                if (width <= 0 || height <= 0)
                {
                    textBox.Text = "";
                    return;
                }

                // Проверка введённых координат
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    textBox.Text = "";
                    return;
                }

                // Преобразование координат клиентской области в экранные
                POINT topLeft = new POINT { X = x, Y = y };
                if (!ClientToScreen(hWnd, ref topLeft))
                {
                    textBox.Text = "";
                    return;
                }

                // Захват пикселя
                using (Bitmap bitmap = new Bitmap(1, 1))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        // Учёт DPI-шкалирования
                        float dpiScaleX = graphics.DpiX / 96.0f;
                        float dpiScaleY = graphics.DpiY / 96.0f;
                        int scaledX = (int)(topLeft.X / dpiScaleX);
                        int scaledY = (int)(topLeft.Y / dpiScaleY);

                        graphics.CopyFromScreen(scaledX, scaledY, 0, 0, new System.Drawing.Size(1, 1));
                    }
                    Color pixelColor = bitmap.GetPixel(0, 0);

                    // Выводим только значение красной компоненты
                    textBox.Text = $"R: {pixelColor.R}";
                }
            }
            catch
            {
                textBox.Text = "";
            }
        }
    }
}