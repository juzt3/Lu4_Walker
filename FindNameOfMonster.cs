using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace LU4_Walker
{
    // var point = await Task.Run(() => FindNameOfMonster.FindTargetPixel(targetHwnd));
    // Находим белый пиксель в ограниченной области окна, который соответствует имени монстра.
    //  SetCursorPos(point.Value.X, point.Value.Y);
    //  Для координат клика используй point.Value.X и point.Value.Y
    public static class FindNameOfMonster
    {
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }

        public static Point? FindTargetPixel(IntPtr hWnd)
        {
            if (!GetClientRect(hWnd, out RECT rect)) return null;

            POINT topLeft = new() { x = rect.Left, y = rect.Top };
            if (!ClientToScreen(hWnd, ref topLeft)) return null;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(topLeft.x, topLeft.y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

            int leftOffset = (int)(width * 0.15);
            int rightLimit = width - (int)(width * 0.10);
            int topOffset = (int)(height * 0.10);
            int bottomLimit = height - (int)(height * 0.15);

            int centerX = (leftOffset + rightLimit) / 2;
            int centerY = (topOffset + bottomLimit) / 2;

            var points = Enumerable.Range(leftOffset, rightLimit - leftOffset)
                .SelectMany(x => Enumerable.Range(topOffset, bottomLimit - topOffset)
                    .Select(y => new { x, y }))
                .OrderBy(p => Math.Sqrt(Math.Pow(p.x - centerX, 2) + Math.Pow(p.y - centerY, 2)))
                .ToList();

            foreach (var pt in points)
            {
                var c = bmp.GetPixel(pt.x, pt.y);
                if (Math.Abs(c.R - 220) <= 5 &&
                    Math.Abs(c.G - 219) <= 5 &&
                    Math.Abs(c.B - 203) <= 5)
                    return new Point(topLeft.x + pt.x, topLeft.y + pt.y);
            }

            return null;
        }
    }
}
