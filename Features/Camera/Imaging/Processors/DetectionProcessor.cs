namespace CameraPreview.Imaging.Processors;

public sealed class DetectionProcessor : IFrameProcessor
{
    public ValueTask ProcessAsync(
        VideoFrame frame,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // TODO: Run object/face/feature detection and attach/store results.
        return ValueTask.CompletedTask;
    }
}
