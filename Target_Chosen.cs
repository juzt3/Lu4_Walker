using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LU4_Walker
{
    public static class Target_Chosen
    {
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }

        /// <summary>
        /// Проверяет наличие пикселя с цветом (161,157,136) в клиентской области окна — означает, что цель выбрана.
        /// </summary>
        public static bool IsSelected(IntPtr hWnd)
        {
            if (!GetClientRect(hWnd, out RECT rect)) return false;

            POINT topLeft = new() { x = rect.Left, y = rect.Top };
            if (!ClientToScreen(hWnd, ref topLeft)) return false;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;
            if (width < 10 || height < 5) return false;

            using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(topLeft.x, topLeft.y, 0, 0,
                                 new Size(width, height),
                                 CopyPixelOperation.SourceCopy);
            }

            // 🧭 Зоны поиска белого пикселя — ты можешь легко расширить список
            var zones = new[]
            {
        new Rectangle(1107, 0, 346, 28),
        new Rectangle(1093, 0, 374, 30),
        new Rectangle(1078, 0, 404, 32)
    };

            foreach (var zone in zones)
            {
                int startX = Math.Max(zone.X, 0);
                int startY = Math.Max(zone.Y, 0);
                int endX = Math.Min(zone.X + zone.Width, width);
                int endY = Math.Min(zone.Y + zone.Height, height);

                for (int x = startX; x < endX; x++)
                {
                    for (int y = startY; y < endY; y++)
                    {
                        var c = bmp.GetPixel(x, y);
                        if (c.R == 255 && c.G == 255 && c.B == 255)
                            return true;
                    }
                }
            }

            return false;


        }
    }
}
