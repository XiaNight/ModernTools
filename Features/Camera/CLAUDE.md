# Camera

Camera diagnostic tools for peripherals with built-in cameras.

## What it does

The Camera page enumerates system video-capture devices via WinRT `DeviceInformation.FindAllAsync(DeviceClass.VideoCapture)`, lets the user pick one from a dropdown, and streams a live preview into a WPF `WriteableBitmap` using `MediaCapture` + `MediaFrameReader`. Selecting a different camera automatically stops the current stream and starts the new one.

## Where the pieces live

`CameraPage.xaml` / `.xaml.cs` — the `PageBase` page with the camera dropdown and live preview. Frame rendering uses the COM `IMemoryBufferByteAccess` interface to copy `SoftwareBitmap` pixels directly into a `WriteableBitmap` backbuffer (unsafe, requires `AllowUnsafeBlocks`).

`Capture/` — stub service layer (`ICameraService`, `CameraService`, `CameraDevice`) for a future pluggable camera backend. Not currently wired into the page.

`Imaging/` — stub frame-processing pipeline (`FramePipeline`, `IFrameProcessor`, `VideoFrame`) with placeholder processors (`ColorProcessor`, `DetectionProcessor`, `OverlayProcessor`, `ResizeProcessor`). Not currently wired into the page.

`UI/` — earlier prototype page, view-model, and renderer (`CameraPreview.UI` namespace). Not used by the shell; kept for reference.
