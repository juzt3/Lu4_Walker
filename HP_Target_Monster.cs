using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LU4_Walker
{
    public static class HP_Target_Monster
    {
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>
        /// Проверяет наличие чистого красного пикселя (255,0,0) в клиентской области окна.
        /// </summary>
        public static bool Scan(IntPtr hWnd)
        {
            if (!GetClientRect(hWnd, out RECT rect)) return false;

            POINT topLeft = new() { x = rect.Left, y = rect.Top };
            if (!ClientToScreen(hWnd, ref topLeft)) return false;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(topLeft.x, topLeft.y, 0, 0,
                                 new Size(width, height),
                                 CopyPixelOperation.SourceCopy);
            }

            for (int x = 1; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.R == 255 && c.G == 0 && c.B == 0)
                        return true;
                }
            }

            return false;
        }
    }
}
