using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CameraPreview.Imaging;

namespace CameraPreview.UI;

public sealed class PreviewRenderer
{
    public Task<BitmapSource> RenderAsync(VideoFrame frame)
    {
        var pixelFormat = frame.Format switch
        {
            VideoPixelFormat.Bgra32 => PixelFormats.Bgra32,
            VideoPixelFormat.Bgr24 => PixelFormats.Bgr24,
            VideoPixelFormat.Gray8 => PixelFormats.Gray8,
            _ => throw new NotSupportedException($"Unsupported preview format: {frame.Format}")
        };

        var bitmap = BitmapSource.Create(
            frame.Width,
            frame.Height,
            96,
            96,
            pixelFormat,
            null,
            frame.Buffer.ToArray(),
            frame.Stride);

        bitmap.Freeze();
        return Task.FromResult(bitmap);
    }
}
