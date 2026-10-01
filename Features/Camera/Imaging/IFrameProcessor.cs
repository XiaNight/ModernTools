namespace CameraPreview.Imaging;

public interface IFrameProcessor
{
    ValueTask ProcessAsync(
        VideoFrame frame,
        CancellationToken cancellationToken = default);
}
