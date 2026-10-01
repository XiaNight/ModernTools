namespace CameraPreview.Imaging.Processors;

public sealed class ResizeProcessor : IFrameProcessor
{
    public ResizeProcessor(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }

    public ValueTask ProcessAsync(
        VideoFrame frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // TODO: Resize frame.Buffer and update frame metadata.
        return ValueTask.CompletedTask;
    }
}
