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
        // WinAPI для отправки сообщений, перемещения курсора и снятия скринов
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool SetCursorPos(int X, int Y);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        // Коды сообщений и клавиш
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int VK_1 = 0x31;

        // Структуры для координат
        [StructLayout(LayoutKind.Sequential)]
        struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        // Таймеры для OCR и нажатия "1"
        private readonly DispatcherTimer ocrTimer = new();
        private readonly DispatcherTimer keyTimer = new();
        private IntPtr targetHwnd;

        public MainWindow()
        {
            InitializeComponent();

            ocrTimer.Interval = TimeSpan.FromSeconds(5);
            ocrTimer.Tick += (s, e) =>
            {
                if (targetHwnd != IntPtr.Zero)
                    RunOcrAndAim(targetHwnd);
            };

            keyTimer.Interval = TimeSpan.FromSeconds(5);
            keyTimer.Tick += (s, e) =>
            {
                if (targetHwnd != IntPtr.Zero)
                {
                    PostMessage(targetHwnd, WM_KEYDOWN, new IntPtr(VK_1), IntPtr.Zero);
                    PostMessage(targetHwnd, WM_KEYUP, new IntPtr(VK_1), IntPtr.Zero);
                }
            };
        }

        // Заполняем ComboBox окнами, в названии которых есть "lu4"
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

        // Нажатие Старт
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

        // Нажатие Стоп
        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            ocrTimer.Stop();
            keyTimer.Stop();
            targetHwnd = IntPtr.Zero;

            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        // Кнопка "Скриншот" — снимаем полное окно игры и сохраняем .bmp
        private void btnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            // если ещё не сделали старт, пытаемся взять окно из ComboBox
            IntPtr hwnd = targetHwnd;
            if (hwnd == IntPtr.Zero && cbWindows.SelectedItem is WindowItem wi)
                hwnd = wi.Hwnd;

            if (hwnd == IntPtr.Zero)
            {
                MessageBox.Show("Окно игры не выбрано.");
                return;
            }

            if (!GetClientRect(hwnd, out RECT rect)) return;

            POINT tl = new() { X = 0, Y = 0 };
            if (!ClientToScreen(hwnd, ref tl)) return;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            using Bitmap bmp = new(width, height);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(tl.X, tl.Y, 0, 0, bmp.Size);

            string name = DateTime.Now.ToString("yyyy-MM-dd - HH-mm-ss") + ".bmp";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);

            MessageBox.Show($"Скриншот сохранён:\n{name}");
        }

        // Кнопка "OCR-Скрин" — выделяем текст и сохраняем .bmp
        private void btnOcrScreenshot_Click(object sender, RoutedEventArgs e)
        {
            IntPtr hwnd = targetHwnd;
            if (hwnd == IntPtr.Zero && cbWindows.SelectedItem is WindowItem wi)
                hwnd = wi.Hwnd;

            if (hwnd == IntPtr.Zero)
            {
                MessageBox.Show("Окно игры не выбрано.");
                return;
            }

            if (!GetClientRect(hwnd, out RECT rect)) return;

            POINT tl = new() { X = 0, Y = 0 };
            if (!ClientToScreen(hwnd, ref tl)) return;

            int width = rect.Right - rect.Left;
            int height = rect.Bottom - rect.Top;

            using Bitmap bmp = new(width, height);
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

            string name = DateTime.Now.ToString("yyyy-MM-dd - HH-mm-ss") + " - OCR.bmp";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);

            MessageBox.Show($"OCR-скрин сохранён:\n{name}");
        }

        // OCR-скан и прицеливание мыши на случайный текст
        private void RunOcrAndAim(IntPtr hWnd)
        {
            try
            {
                if (!GetClientRect(hWnd, out RECT rect)) return;

                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;

                POINT tl = new() { X = 0, Y = 0 };
                if (!ClientToScreen(hWnd, ref tl)) return;

                int cropLeft = (int)(width * 0.18);
                int cropTop = (int)(height * 0.08);
                int cropRight = width - (int)(width * 0.02);
                int cropBottom = height - (int)(height * 0.12);

                int cropWidth = cropRight - cropLeft;
                int cropHeight = cropBottom - cropTop;

                using Bitmap bmp = new(cropWidth, cropHeight);
                using var g = Graphics.FromImage(bmp);
                g.CopyFromScreen(tl.X + cropLeft, tl.Y + cropTop, 0, 0, bmp.Size);

                using var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default);
                using var pix = Pix.LoadFromMemory(BitmapToBytes(bmp));
                using var page = engine.Process(pix);
                using var iter = page.GetIterator();

                iter.Begin();
                List<(int X, int Y)> centers = new();
                do
                {
                    string txt = iter.GetText(PageIteratorLevel.Word);
                    if (!string.IsNullOrWhiteSpace(txt) &&
                        iter.TryGetBoundingBox(PageIteratorLevel.Word, out var bbox))
                    {
                        int cx = bbox.X1 + bbox.Width / 2;
                        int cy = bbox.Y1 + bbox.Height / 2;
                        centers.Add((cx, cy));
                    }
                }
                while (iter.Next(PageIteratorLevel.Word));

                if (centers.Count == 0) return;

                var rnd = new Random();
                var target = centers[rnd.Next(centers.Count)];

                int screenX = tl.X + cropLeft + target.X;
                int screenY = tl.Y + cropTop + target.Y;

                SetCursorPos(screenX, screenY);
            }
            catch
            {
                // Игнорируем ошибки OCR
            }
        }

        // Конвертация Bitmap в байты для Tesseract
        private byte[] BitmapToBytes(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            return ms.ToArray();
        }

        // Класс для ComboBox
        private class WindowItem
        {
            public IntPtr Hwnd { get; }
            public string Title { get; }
            public WindowItem(IntPtr hwnd, string title)
            {
                Hwnd = hwnd;
                Title = title;
            }
            public override string ToString() => $"{Title} [{Hwnd}]";
        }
    }
}
