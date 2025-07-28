using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LU4_Walker
{
    //  var playerDead = await Task.Run(() => HpPlayerCheck.Scan(targetHwnd));
    //  Мертв ли игрок?
    //  И есть ли возможность кликнуть по "Вернуться в город"?
    //
    //  SetCursorPos(playerDead.ClickX, playerDead.ClickY);
    //  Для координат клика используй ClickX и ClickY
    internal class HpPlayerCheck
    {
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

        public struct ScanResult
        {
            public bool IsReady;    // Условия выполнены
            public int ClickX;      // X для клика
            public int ClickY;      // Y для клика
        }

        private class RedCheckPoint
        {
            public int X { get; init; }
            public int Y { get; init; }
            public int R { get; init; }

            public RedCheckPoint(int x, int y, int r)
            {
                X = x;
                Y = y;
                R = r;
            }
        }

        private static readonly List<RedCheckPoint> redPoints = new()
        {
            new RedCheckPoint(30, 60, 166),
            new RedCheckPoint(32, 64, 165),
            new RedCheckPoint(34, 70, 121)
            // 🔧 Добавляй координаты для разных разрешений
        };

        private static readonly List<(Point p25, Point p179)> cityReturnPairs = new()
        {
            (new Point(1247, 564), new Point(1248, 565)),
            (new Point(1245, 551), new Point(1246, 552)),
            (new Point(1242, 538), new Point(1243, 539))
        };

        public static ScanResult Scan(IntPtr hWnd)
        {
            var result = new ScanResult { IsReady = false, ClickX = 0, ClickY = 0 };

            if (!GetClientRect(hWnd, out RECT rect)) return result;
            POINT topLeft = new() { x = rect.Left, y = rect.Top };
            if (!ClientToScreen(hWnd, ref topLeft)) return result;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            using var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(topLeft.x, topLeft.y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

            // Шаг 1: Проверка HP — если хотя бы один красный пиксель найден, персонаж жив
            foreach (var point in redPoints)
            {
                if (point.X >= width || point.Y >= height) continue;
                var c = bmp.GetPixel(point.X, point.Y);
                if (Math.Abs(c.R - point.R) <= 5 && c.G < 30 && c.B < 30)
                    return result;
            }

            // Шаг 2: Поиск пары пикселей "Вернуться в город"
            foreach (var (p25, p179) in cityReturnPairs)
            {
                if (p25.X >= width || p25.Y >= height || p179.X >= width || p179.Y >= height)
                    continue;

                var c1 = bmp.GetPixel(p25.X, p25.Y);
                var c2 = bmp.GetPixel(p179.X, p179.Y);

                if (c1.R == 25 && c2.R == 179)
                {
                    result.IsReady = true;
                    result.ClickX = p179.X;
                    result.ClickY = p179.Y;
                    return result;
                }
            }

            return result;
        }
    }
}
