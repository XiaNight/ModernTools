using CameraPreview.Imaging;

namespace CameraPreview.Capture;

/// <summary>
/// Capture backend placeholder.
/// Connect this class to Media Foundation, OpenCvSharp, DirectShow,
/// Windows.Media.Capture, or another camera backend.
/// </summary>
public sealed class CameraService : ICameraService
{
    public event EventHandler<VideoFrame>? FrameReceived;

    public Task<IReadOnlyList<CameraDevice>> GetDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CameraDevice> devices = [];
        return Task.FromResult(devices);
    }

    public Task StartAsync(
        CameraDevice device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        // TODO:
        // 1. Open the selected camera.
        // 2. Convert captured frames into VideoFrame.
        // 3. Raise FrameReceived for each frame.
        //
        // Example:
        // FrameReceived?.Invoke(this, videoFrame);

        return Task.CompletedTask;
    }

    public Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        // TODO: Stop capture and release native camera resources.
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private void PublishFrame(VideoFrame frame)
    {
        FrameReceived?.Invoke(this, frame);
    }
}
