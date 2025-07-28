using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LU4_Walker
{
    // Оверлей для выделения области на экране.
    // Используется для визуального указания на определённые координаты.
    // OverlayHighlighter.ShowOverlay(int x, int y)
    public static class OverlayHighlighter
    {
        /// <summary>
        /// Показывает прозрачное окно с рамкой на заданных координатах экрана.
        /// </summary>
        /// <param name="x">X-координата центра рамки.</param>
        /// <param name="y">Y-координата центра рамки.</param>
        public static void ShowOverlay(int x, int y)
        {
            var overlay = new Window
            {
                Width = 10,
                Height = 10,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
                Left = x - 5,
                Top = y - 5
            };

            var border = new Border
            {
                BorderBrush = Brushes.LightGreen,
                BorderThickness = new Thickness(2),
                Width = 10,
                Height = 10
            };

            overlay.Content = border;
            overlay.Show();

            var closeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                overlay.Close();
            };

            closeTimer.Start();
        }
    }
}
