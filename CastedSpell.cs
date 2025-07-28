using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace LU4_Walker
{
    //  bool isCasting = CastedSpell.IsCasting(targetHwnd);
    //  Задействована ли шкала каста?
    //
    //
    public static class CastedSpell
    {
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }

        private class SpellPixel
        {
            public int X { get; init; }
            public int Y { get; init; }
            public int R { get; init; }
            public int G { get; init; }
            public int B { get; init; }

            public SpellPixel(int x, int y, int r, int g, int b)
            {
                X = x;
                Y = y;
                R = r;
                G = g;
                B = b;
            }
        }

        private static readonly List<SpellPixel> castIndicators = new()
        {
            new SpellPixel(1220, 754, 16, 156, 204),
            new SpellPixel(1220, 754, 16, 156, 205),
            new SpellPixel(1220, 1078, 28, 166, 215),
            new SpellPixel(1220, 782, 16, 153, 202),
            new SpellPixel(1220, 753, 16, 154, 203)
            // ✨ Добавь ещё — если появятся новые состояния
        };

        /// <summary>
        /// Проверяет наличие пикселя начала шкалы каста.
        /// Возвращает true — если он найден, false — если нет.
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

            // 🎯 Центр поиска по координатам шкалы
            int baseX = 1220;
            int baseY = 754;

            // 📦 Размер области поиска
            int scanRadius = 10;

            // 🎨 Диапазоны цвета (можешь адаптировать ещё шире)
            int minR = 10, maxR = 35;
            int minG = 140, maxG = 180;
            int minB = 195, maxB = 230;

            for (int dx = -scanRadius; dx <= scanRadius; dx++)
            {
                for (int dy = -scanRadius; dy <= scanRadius; dy++)
                {
                    int x = baseX + dx;
                    int y = baseY + dy;

                    if (x < 0 || y < 0 || x >= width || y >= height) continue;

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
