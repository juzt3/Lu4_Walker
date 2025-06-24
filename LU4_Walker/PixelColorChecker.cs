using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Windows.Forms;

namespace LU4_Walker
{
    public static class PixelColorChecker
    {
        // Импорт функций WinAPI
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

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
        /// Проверяет цвет пикселя в окне по дескриптору окна и возвращает значение указанной компоненты цвета (R, G или B).
        /// </summary>
        /// <param name="colorComponent">Компонента цвета: "R", "G" или "B".</param>
        /// <param name="hWnd">Дескриптор окна процесса.</param>
        /// <param name="x">Координата X (в пределах клиентской области).</param>
        /// <param name="y">Координата Y (в пределах клиентской области).</param>
        /// <returns>Значение компоненты цвета (0–255) или -1 при ошибке.</returns>
        public static int CheckPixelColor(string colorComponent, IntPtr hWnd, int x, int y)
        {
            try
            {
                // Проверка валидности дескриптора окна
                if (hWnd == IntPtr.Zero)
                {
                    return -1;
                }

                // Проверка, минимизировано ли окно, и восстановление
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }

                // Активация окна для корректного рендеринга
                SetForegroundWindow(hWnd);

                // Задержка для рендеринга окна
                System.Threading.Thread.Sleep(50);

                // Получение размеров клиентской области
                if (!GetClientRect(hWnd, out RECT clientRect))
                {
                    return -1;
                }

                int width = clientRect.Right - clientRect.Left;
                int height = clientRect.Bottom - clientRect.Top;

                if (width <= 0 || height <= 0)
                {
                    return -1;
                }

                // Проверка введённых координат
                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    return -1;
                }

                // Преобразование координат клиентской области в экранные
                POINT topLeft = new POINT { X = x, Y = y };
                if (!ClientToScreen(hWnd, ref topLeft))
                {
                    return -1;
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

                    // Возвращаем значение указанной компоненты цвета
                    switch (colorComponent.ToUpper())
                    {
                        case "R":
                            return pixelColor.R;
                        case "G":
                            return pixelColor.G;
                        case "B":
                            return pixelColor.B;
                        default:
                            return -1; // Неверная компонента цвета
                    }
                }
            }
            catch
            {
                return -1; // Ошибка
            }
        }
    }
}