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

        /// <summary>
        /// Ищет самый левый красный пиксель (R > 200, G < 80, B < 80) в области шкалы HP моба.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна.</param>
        /// <returns>Кортеж (found, x, y): found — true, если пиксель найден; x, y — координаты пикселя (или -1, -1, если не найден).</returns>
        public static (bool found, int x, int y) FindFirstRedPixel(IntPtr hWnd)
        {
            try
            {
                // Проверка валидности окна
                if (hWnd == IntPtr.Zero)
                {
                    return (false, -1, -1);
                }

                // Получение размеров клиентского окна
                if (!GetClientRect(hWnd, out RECT clientRect))
                {
                    return (false, -1, -1);
                }

                int width = clientRect.Right - clientRect.Left;
                int height = clientRect.Bottom - clientRect.Top;

                if (width <= 0 || height <= 0)
                {
                    return (false, -1, -1);
                }

                // Определяем область поиска (X: 43.36%–56.72% ширины, Y: 0–1.875% высоты)
                int startX = (int)(width * 0.4336); // 1107/2560
                int endX = (int)(width * 0.5672); // 1452/2560
                int searchHeight = (int)(height * 0.01875); // 27/1440
                searchHeight = Math.Max(1, Math.Min(searchHeight, height)); // Не менее 1 пикселя

                // Преобразование координат в экранные
                POINT topLeft = new POINT { X = startX, Y = 0 };
                if (!ClientToScreen(hWnd, ref topLeft))
                {
                    return (false, -1, -1);
                }

                // Захват области
                using (Bitmap bitmap = new Bitmap(endX - startX, searchHeight))
                {
                    using (Graphics graphics = Graphics.FromImage(bitmap))
                    {
                        // Учёт DPI-шкалирования
                        float dpiScaleX = graphics.DpiX / 96.0f;
                        float dpiScaleY = graphics.DpiY / 96.0f;
                        int scaledX = (int)(topLeft.X / dpiScaleX);
                        int scaledY = (int)(topLeft.Y / dpiScaleY);
                        int scaledWidth = (int)((endX - startX) / dpiScaleX);
                        int scaledHeight = (int)(searchHeight / dpiScaleY);

                        graphics.CopyFromScreen(scaledX, scaledY, 0, 0, new System.Drawing.Size(scaledWidth, scaledHeight));
                    }

                    // Поиск самого левого красного пикселя
                    for (int yy = 0; yy < searchHeight; yy++)
                    {
                        for (int xx = 0; xx < (endX - startX); xx++)
                        {
                            Color pixel = bitmap.GetPixel(xx, yy);
                            if (pixel.R > 200 && pixel.G < 80 && pixel.B < 80)
                            {
                                return (true, startX + xx, yy);
                            }
                        }
                    }

                    return (false, -1, -1); // Пиксель не найден
                }
            }
            catch
            {
                return (false, -1, -1); // Ошибка
            }
        }
    }
}