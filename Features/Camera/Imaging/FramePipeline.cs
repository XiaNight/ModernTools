namespace CameraPreview.Imaging;

public sealed class FramePipeline
{
    private readonly IReadOnlyList<IFrameProcessor> _processors;

    public FramePipeline(IEnumerable<IFrameProcessor> processors)
    {
        ArgumentNullException.ThrowIfNull(processors);
        _processors = processors.ToArray();
    }

    public async ValueTask ProcessAsync(
        VideoFrame frame,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);

        foreach (var processor in _processors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await processor.ProcessAsync(frame, cancellationToken);
        }
    }
}
