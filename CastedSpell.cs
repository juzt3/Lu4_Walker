using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LU4_Walker
{
    public static class CastedSpell
    {
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }

        /// <summary>
        /// Проверяет наличие визуального признака шкалы каста.
        /// Работает на любом разрешении.
        /// </summary>
        public static bool IsCasting(IntPtr hWnd)
        {
            if (!GetClientRect(hWnd, out RECT rect)) return false;

            POINT topLeft = new() { x = rect.Left, y = rect.Top };
            if (!ClientToScreen(hWnd, ref topLeft)) return false;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(topLeft.x, topLeft.y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

            // 🧭 Примерная область, где каст обычно появляется (относительно экрана)
            int scanStartX = (int)(width * 0.80);
            int scanStartY = (int)(height * 0.70);
            int scanWidth = Math.Min(60, width - scanStartX);
            int scanHeight = Math.Min(60, height - scanStartY);

            // 🎨 Диапазон цвета каста (настраиваемый)
            int minR = 10, maxR = 40;
            int minG = 140, maxG = 190;
            int minB = 195, maxB = 235;

            for (int x = scanStartX; x < scanStartX + scanWidth; x++)
            {
                for (int y = scanStartY; y < scanStartY + scanHeight; y++)
                {
                    if (x >= width || y >= height) continue;

                    var c = bmp.GetPixel(x, y);

                    if (c.R >= minR && c.R <= maxR &&
                        c.G >= minG && c.G <= maxG &&
                        c.B >= minB && c.B <= maxB)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
