namespace CameraPreview.Imaging;

public sealed class VideoFrame
{
    public required Memory<byte> Buffer { get; init; }

    public required int Width { get; init; }
    public required int Height { get; init; }
    public required int Stride { get; init; }

    public required VideoPixelFormat Format { get; set; }

    public long Timestamp { get; init; }

    public VideoFrame Clone()
    {
        return new VideoFrame
        {
            Buffer = Buffer.ToArray(),
            Width = Width,
            Height = Height,
            Stride = Stride,
            Format = Format,
            Timestamp = Timestamp
        };
    }
}

public enum VideoPixelFormat
{
    Bgra32,
    Bgr24,
    Gray8
}
