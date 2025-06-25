using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace LU4_Walker
{
    public static class RedPixelChecker
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
        /// Ищет красный пиксель (R == 255, G == 0, B == 0) в области шкалы HP моба, начиная с X=1064, Y=0.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна.</param>
        /// <returns>Кортеж (found, x, y): found — true, если пиксель найден; x, y — координаты пикселя (или -1, -1, если не найден).</returns>
        public static (bool found, int x, int y) CheckRedPixel(IntPtr hWnd)
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

                // Определяем область поиска (X: 1064 до конца ширины, Y: 0–1.875% высоты)
                int startX = (int)(width * 1064.0 / 2560.0); // Нормализация начальной координаты X=1064 для 2560x1440
                int endX = width;
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

                    // Поиск красного пикселя (R == 255, G == 0, B == 0)
                    for (int yy = 0; yy < searchHeight; yy++)
                    {
                        for (int xx = 0; xx < (endX - startX); xx++)
                        {
                            Color pixel = bitmap.GetPixel(xx, yy);
                            if (pixel.R == 255 && pixel.G == 0 && pixel.B == 0)
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