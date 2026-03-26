using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Base.Components
{
    /// <summary>
    /// Minimal per-pixel surface:
    /// - Dependency properties for size/DPI
    /// - byte[] backing buffer (BGRA32)
    /// - SetPixel(x,y,color) writes into the buffer
    /// - Call Flush() once per frame to show changes (simple & predictable)
    /// </summary>
    public partial class PNGSurface : UserControl, IDisposable
    {
        public static readonly DependencyProperty PixelWidthProperty =
            DependencyProperty.Register(nameof(PixelWidth), typeof(int), typeof(PNGSurface),
                new PropertyMetadata(256, OnSizeChanged), v => (int)v > 0);

        public static readonly DependencyProperty PixelHeightProperty =
            DependencyProperty.Register(nameof(PixelHeight), typeof(int), typeof(PNGSurface),
                new PropertyMetadata(256, OnSizeChanged), v => (int)v > 0);

        public static readonly DependencyProperty DpiXProperty =
            DependencyProperty.Register(nameof(DpiX), typeof(double), typeof(PNGSurface),
                new PropertyMetadata(96.0), v => (double)v > 0);

        public static readonly DependencyProperty DpiYProperty =
            DependencyProperty.Register(nameof(DpiY), typeof(double), typeof(PNGSurface),
                new PropertyMetadata(96.0), v => (double)v > 0);

        public static readonly DependencyProperty StretchProperty =
            Image.StretchProperty.AddOwner(typeof(PNGSurface),
                new FrameworkPropertyMetadata(Stretch.None, (d, e) =>
                {
                    var s = (PNGSurface)d;
                    if (s.ImageHost != null) s.ImageHost.Stretch = (Stretch)e.NewValue;
                }));

        public int PixelWidth
        {
            get => (int)GetValue(PixelWidthProperty);
            set => SetValue(PixelWidthProperty, value);
        }
        public int PixelHeight
        {
            get => (int)GetValue(PixelHeightProperty);
            set => SetValue(PixelHeightProperty, value);
        }
        public double DpiX
        {
            get => (double)GetValue(DpiXProperty);
            set => SetValue(DpiXProperty, value);
        }
        public double DpiY
        {
            get => (double)GetValue(DpiYProperty);
            set => SetValue(DpiYProperty, value);
        }
        public Stretch Stretch
        {
            get => (Stretch)GetValue(StretchProperty);
            set => SetValue(StretchProperty, value);
        }

        private byte[] _buffer; // BGRA32
        private int _stride;

        public PNGSurface()
        {
            InitializeComponent();
            Loaded += (_, __) => Recreate();
        }

        public void SetPixel(int x, int y, Color color)
        {
            if (_buffer == null) Recreate();
            if ((uint)x >= (uint)PixelWidth || (uint)y >= (uint)PixelHeight) return;

            int o = y * _stride + (x << 2);
            _buffer[o + 0] = color.B;
            _buffer[o + 1] = color.G;
            _buffer[o + 2] = color.R;
            _buffer[o + 3] = color.A;
        }

        public void Clear(Color color)
        {
            if (_buffer == null) Recreate();
            for (int y = 0; y < PixelHeight; y++)
            {
                int row = y * _stride;
                for (int x = 0; x < PixelWidth; x++)
                {
                    int o = row + (x << 2);
                    _buffer[o + 0] = color.B;
                    _buffer[o + 1] = color.G;
                    _buffer[o + 2] = color.R;
                    _buffer[o + 3] = color.A;
                }
            }
        }

        /// <summary>
        /// Uploads the current buffer to the screen. Call once per frame.
        /// </summary>
        public void Flush()
        {
            if (_buffer == null) Recreate();

            // Create a fresh BitmapSource from the managed buffer (simple, no locks/dirty rects)
            var bmp = BitmapSource.Create(
                PixelWidth,
                PixelHeight,
                Math.Max(1e-3, DpiX),
                Math.Max(1e-3, DpiY),
                PixelFormats.Bgra32,
                null,
                _buffer,
                _stride);

            ImageHost.Source = bmp;
        }

        private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PNGSurface)d).Recreate();
        }

        private void Recreate()
        {
            _stride = ((PixelWidth * 4) + 3) & ~3;
            _buffer = new byte[_stride * PixelHeight];
            ImageHost.Stretch = Stretch;
            // Optional: clear to transparent
            Array.Clear(_buffer, 0, _buffer.Length);
            Flush();
        }

        public void Dispose()
        {
            _buffer = null;
            ImageHost.Source = null;
        }


        public async Task ExportPngAsync(string filePath)
        {
            await Task.Run(() => ExportPng(filePath));
        }

        public void ExportPng(string filePath)
        {
            if (_buffer == null) Recreate();
            var bmp = BitmapSource.Create(
                PixelWidth,
                PixelHeight,
                Math.Max(1e-3, DpiX),
                Math.Max(1e-3, DpiY),
                PixelFormats.Bgra32,
                null,
                _buffer,
                _stride);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (var stream = System.IO.File.Create(filePath))
            {
                encoder.Save(stream);
            }
        }
    }
}
