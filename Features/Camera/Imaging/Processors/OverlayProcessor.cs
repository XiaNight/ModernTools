namespace CameraPreview.Imaging.Processors;

public sealed class OverlayProcessor : IFrameProcessor
{
    public ValueTask ProcessAsync(
        VideoFrame frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // TODO: Draw boxes, labels, guides, masks, or other overlays.
        return ValueTask.CompletedTask;
    }
}
