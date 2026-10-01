using CameraPreview.Imaging;

namespace CameraPreview.Capture;

public interface ICameraService : IAsyncDisposable
{
    event EventHandler<VideoFrame>? FrameReceived;

    Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default);

    Task StartAsync(
        CameraDevice device,
        CancellationToken cancellationToken = default);

    Task StopAsync(
        CancellationToken cancellationToken = default);
}
