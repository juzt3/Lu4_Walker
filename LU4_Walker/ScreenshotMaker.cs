using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace LU4_Walker
{
    public static class ScreenshotMaker
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
        /// Снимает скриншот клиентской области окна и сохраняет его как BMP.
        /// </summary>
        /// <param name="hWnd">Дескриптор окна.</param>
        public static void TakeScreenshot(IntPtr hWnd)
        {
            try
            {
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