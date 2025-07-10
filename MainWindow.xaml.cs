using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Tesseract;

namespace LU4_Walker
{
    public partial class MainWindow : Window
    {
        // WinAPI для сообщений, курсора, скринов и чтения пикселя
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr hdc, int X, int Y);

        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int VK_1 = 0x31;

        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        private readonly DispatcherTimer ocrTimer = new();
        private readonly DispatcherTimer keyTimer = new();
        private IntPtr targetHwnd;

        public MainWindow()
        {
            InitializeComponent();

            ocrTimer.Interval = TimeSpan.FromSeconds(5);
            ocrTimer.Tick += (_, __) =>
            {
                if (targetHwnd != IntPtr.Zero)
                    RunOcrAndAim(targetHwnd);
            };

            keyTimer.Interval = TimeSpan.FromSeconds(5);
            keyTimer.Tick += (_, __) =>
            {
                if (targetHwnd != IntPtr.Zero)
                {
                    PostMessage(targetHwnd, WM_KEYDOWN, new IntPtr(VK_1), IntPtr.Zero);
                    PostMessage(targetHwnd, WM_KEYUP, new IntPtr(VK_1), IntPtr.Zero);
                }
            };
        }

        private void cbWindows_DropDownOpened(object sender, EventArgs e)
        {
            cbWindows.Items.Clear();
            foreach (var proc in Process.GetProcesses())
            {
                if (proc.MainWindowHandle != IntPtr.Zero &&
                    proc.ProcessName.IndexOf("lu4", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cbWindows.Items.Add(new WindowItem(proc.MainWindowHandle, proc.MainWindowTitle));
                }
            }
        }

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (cbWindows.SelectedItem is not WindowItem wi)
            {
                MessageBox.Show("Выберите окно LU4.");
                return;
            }

            targetHwnd = wi.Hwnd;
            ocrTimer.Start();
            keyTimer.Start();
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            ocrTimer.Stop();
            keyTimer.Stop();
            targetHwnd = IntPtr.Zero;
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        // Сохранить обычный скриншот окна
        private void btnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = GetEffectiveHwnd();
            if (hwnd == IntPtr.Zero) { MessageBox.Show("Окно игры не выбрано."); return; }

            if (!GetClientRect(hwnd, out RECT rect)) return;
            var tl = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(hwnd, ref tl)) return;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(tl.X, tl.Y, 0, 0, bmp.Size);

            string name = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss}.bmp";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
            MessageBox.Show($"Скриншот сохранён:\n{name}");
        }

        // Сохранить OCR-скриншот с рамками
        private void btnOcrScreenshot_Click(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = GetEffectiveHwnd();
            if (hwnd == IntPtr.Zero) { MessageBox.Show("Окно игры не выбрано."); return; }

            if (!GetClientRect(hwnd, out RECT rect)) return;
            var tl = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(hwnd, ref tl)) return;

            int w = rect.Right - rect.Left;
            int h = rect.Bottom - rect.Top;
            using var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(tl.X, tl.Y, 0, 0, bmp.Size);

            using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
            using var pix = Pix.LoadFromMemory(BitmapToBytes(bmp));
            using var page = engine.Process(pix);
            using var iter = page.GetIterator();
            iter.Begin();

            using var pen = new Pen(Color.Red, 2);
            do
            {
                if (iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bbox))
                    g.DrawRectangle(pen, bbox.X1, bbox.Y1, bbox.Width, bbox.Height);
            }
            while (iter.Next(PageIteratorLevel.Word));

            string name = $"{DateTime.Now:yyyy-MM-dd - HH-mm-ss} - OCR.bmp";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
            MessageBox.Show($"OCR-скрин сохранён:\n{name}");
        }

        // Получить значение R-канала пикселя
        private void btnGetR_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(tbX.Text, out int x) ||
                !int.TryParse(tbY.Text, out int y))
            {
                MessageBox.Show("Введите корректные целые значения X и Y.");
                return;
            }

            // Ищем последний .bmp файл в текущей папке
            var folder = AppDomain.CurrentDomain.BaseDirectory;
            var latestFile = new DirectoryInfo(folder)
                .GetFiles("*.bmp")
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            if (latestFile == null)
            {
                MessageBox.Show("Файл BMP не найден.");
                return;
            }

            try
            {
                using var bmp = new Bitmap(latestFile.FullName);

                // Проверка на выход за границы изображения
                if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height)
                {
                    MessageBox.Show("Координаты вне диапазона изображения.");
                    return;
                }

                var pixelColor = bmp.GetPixel(x, y);
                lblR.Text = pixelColor.R.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при чтении BMP:\n" + ex.Message);
            }
        }


        // OCR-скан и наведение курсора: центр + 5 пикселей вниз
        private void RunOcrAndAim(IntPtr hWnd)
        {
            try
            {
                if (!GetClientRect(hWnd, out RECT rect)) return;
                var tl = new POINT { X = 0, Y = 0 };
                if (!ClientToScreen(hWnd, ref tl)) return;

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                int cropLeft = (int)(width * 0.18);
                int cropTop = (int)(height * 0.08);
                int cropRight = width - (int)(width * 0.02);
                int cropBottom = height - (int)(height * 0.12);
                int cropW = cropRight - cropLeft;
                int cropH = cropBottom - cropTop;

                using var bmp = new Bitmap(cropW, cropH);
                using var g = Graphics.FromImage(bmp);
                g.CopyFromScreen(tl.X + cropLeft, tl.Y + cropTop, 0, 0, bmp.Size);

                using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
                using var pix = Pix.LoadFromMemory(BitmapToBytes(bmp));
                using var page = engine.Process(pix);
                using var iter = page.GetIterator();
                iter.Begin();

                var centers = new List<(int X, int Y)>();
                do
                {
                    if (!string.IsNullOrWhiteSpace(iter.GetText(PageIteratorLevel.Word)) &&
                        iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bbox))
                    {
                        int cx = bbox.X1 + bbox.Width / 2 - 10;  // смещение влево на 10px
                        int cy = bbox.Y1 + bbox.Height / 2 + 25;  // смещение вниз на 25px
                        centers.Add((cx, cy));
                    }
                }
                while (iter.Next(PageIteratorLevel.Word));

                if (centers.Count == 0) return;
                var t = centers[new Random().Next(centers.Count)];

                int screenX = tl.X + cropLeft + t.X;
                int screenY = tl.Y + cropTop + t.Y;
                SetCursorPos(screenX, screenY);
            }
            catch
            {
                // игнорируем ошибки
            }
        }




        private byte[] BitmapToBytes(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        private class WindowItem
        {
            public IntPtr Hwnd { get; }
            public string Title { get; }
            public WindowItem(IntPtr hwnd, string title) { Hwnd = hwnd; Title = title; }
            public override string ToString() => $"{Title} [{Hwnd}]";
        }

        private IntPtr GetEffectiveHwnd()
        {
            if (targetHwnd != IntPtr.Zero) return targetHwnd;
            if (cbWindows.SelectedItem is WindowItem wi) return wi.Hwnd;
            return IntPtr.Zero;
        }
        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.DragMove();
        }


    }
}
