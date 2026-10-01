namespace CameraPreview.Imaging.Processors;

public sealed class ColorProcessor : IFrameProcessor
{
    public ValueTask ProcessAsync(
        VideoFrame frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // TODO: Apply color correction, LUT, exposure, white balance, etc.
        return ValueTask.CompletedTask;
    }
}
