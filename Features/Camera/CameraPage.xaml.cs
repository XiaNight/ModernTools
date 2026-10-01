using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Base.Core;
using Base.Pages;
using Base.Services;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Devices;
using WinRT;

namespace Camera;

[PageInfo("Camera",
	Glyph = "\uE114",
	ShortName = "CAM",
	Description = "Camera diagnostic tools.",
	Path = ["ATE"],
	ShowDeviceSelection = false)]
public partial class CameraPage : PageBase
{
	private const int FrameBufferSlots = 4;

	private MediaCapture mediaCapture;
	private MediaFrameReader frameReader;
	private WriteableBitmap previewBitmap;
	private FrameBuffer frameBuffer;

	private bool isCapturing;
	private bool updatingControls;
	private int renderingFrame;

	private WriteableBitmap rememberedBitmap;

	private bool isDragging;
	private Point dragStartPixel;
	private int markX;
	private int markY;
	private int markW;
	private int markH;
	private bool updatingRectFields;

	public CameraPage()
	{
		InitializeComponent();

		RectXBox.TextChanged += RectBox_TextChanged;
        RectYBox.TextChanged += RectBox_TextChanged;
		RectWBox.TextChanged += RectBox_TextChanged;
		RectHBox.TextChanged += RectBox_TextChanged;
	}

	protected override void OnEnable()
	{
		base.OnEnable();
		_ = RefreshCamerasAsync();
	}

	protected override void OnDisable()
	{
		base.OnDisable();
		_ = StopPreviewAsync();
	}

	public override void OnDestroy()
	{
		base.OnDestroy();
		_ = StopPreviewAsync();
	}

	private async Task RefreshCamerasAsync()
	{
		try
		{
			DeviceInformationCollection devices =
				await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);

			List<CameraListItem> items = new();

			foreach (DeviceInformation device in devices)
			{
				items.Add(new CameraListItem(device.Id, device.Name));
			}

			CameraCombo.ItemsSource = items;
			CameraCombo.DisplayMemberPath = nameof(CameraListItem.Name);

			if (items.Count > 0)
			{
				CameraCombo.SelectedIndex = 0;
			}
			else
			{
				ShowStatus("No cameras found.");
			}
		}
		catch (Exception ex)
		{
			ShowStatus($"Failed to enumerate cameras: {ex.Message}");
			Debug.Log($"[Camera] enumerate failed: {ex.Message}");
		}
	}

	private async void CameraCombo_SelectionChanged(
		object sender,
		SelectionChangedEventArgs e)
	{
		await StopPreviewAsync();

		if (CameraCombo.SelectedItem is CameraListItem item)
		{
			await StartPreviewAsync(item.Id);
		}
	}

	private async Task StartPreviewAsync(string deviceId)
	{
		try
		{
			ShowStatus("Starting camera...");

			PreviewImage.Source = null;
			previewBitmap = null;

			mediaCapture = new MediaCapture();

			MediaCaptureInitializationSettings settings = new()
			{
				VideoDeviceId = deviceId,
				StreamingCaptureMode = StreamingCaptureMode.Video,
				MemoryPreference = MediaCaptureMemoryPreference.Cpu,
			};

			await mediaCapture.InitializeAsync(settings);

			MediaFrameSource videoSource = FindVideoSource();

			if (videoSource == null)
			{
				ShowStatus("No video source available on this camera.");
				return;
			}

			frameReader = await mediaCapture.CreateFrameReaderAsync(videoSource);
			frameReader.FrameArrived += OnFrameArrived;

			MediaFrameReaderStartStatus status = await frameReader.StartAsync();

			if (status == MediaFrameReaderStartStatus.Success)
			{
				isCapturing = true;

				ConfigureCameraControls();

				StatusText.Visibility = Visibility.Collapsed;
			}
			else
			{
				ShowStatus($"Failed to start camera: {status}");
			}
		}
		catch (Exception ex)
		{
			ShowStatus($"Camera error: {ex.Message}");
			Debug.Log($"[Camera] start failed: {ex.Message}");
		}
	}

	private void ConfigureCameraControls()
	{
		if (mediaCapture == null)
		{
			DisableCameraControls();
			return;
		}

		VideoDeviceController controller = mediaCapture.VideoDeviceController;

		updatingControls = true;

		try
		{
			MediaDeviceControl exposure = controller.Exposure;

			Debug.Log(
				$"[Camera] Exposure: {exposure.Capabilities.Supported}");

			if (exposure.Capabilities.Supported)
			{
				if (exposure.TryGetAuto(out bool autoExposure))
				{
					Debug.Log(
						$"[Camera] Exposure auto before disable: {autoExposure}");
				}

				bool autoDisabled = exposure.TrySetAuto(false);

				Debug.Log(
					$"[Camera] Disable auto exposure: {autoDisabled}");
			}

			ConfigureSlider(
				ExposureSlider,
				ExposureValueText,
				exposure);

			ConfigureSlider(
				BrightnessSlider,
				BrightnessValueText,
				controller.Brightness);

			ConfigureSlider(
				ContrastSlider,
				ContrastValueText,
				controller.Contrast);

			ConfigureSlider(
				HueSlider,
				HueValueText,
				controller.Hue);

			Debug.Log(
				$"[Camera] Brightness: {controller.Brightness.Capabilities.Supported}");

			Debug.Log(
				$"[Camera] Contrast: {controller.Contrast.Capabilities.Supported}");

			Debug.Log(
				$"[Camera] Hue: {controller.Hue.Capabilities.Supported}");
		}
		finally
		{
			updatingControls = false;
		}
	}

	private static void ConfigureSlider(
		Slider slider,
		TextBlock valueText,
		MediaDeviceControl control)
	{
		MediaDeviceControlCapabilities capabilities = control.Capabilities;

		if (!capabilities.Supported)
		{
			slider.IsEnabled = false;
			valueText.Text = "N/A";
			return;
		}

		slider.Minimum = capabilities.Min;
		slider.Maximum = capabilities.Max;

		if (capabilities.Step > 0)
		{
			slider.SmallChange = capabilities.Step;
			slider.LargeChange = capabilities.Step;
			slider.TickFrequency = capabilities.Step;
			slider.IsSnapToTickEnabled = true;
		}
		else
		{
			slider.IsSnapToTickEnabled = false;
		}

		if (control.TryGetValue(out double value))
		{
			slider.Value = Math.Clamp(
				value,
				capabilities.Min,
				capabilities.Max);

			valueText.Text = FormatControlValue(value);
		}
		else
		{
			double defaultValue = Math.Clamp(
				capabilities.Default,
				capabilities.Min,
				capabilities.Max);

			slider.Value = defaultValue;
			valueText.Text = FormatControlValue(defaultValue);
		}

		slider.IsEnabled = true;
	}

	private static string FormatControlValue(double value)
	{
		return value.ToString("0.##");
	}

	private void ExposureSlider_ValueChanged(
		object sender,
		RoutedPropertyChangedEventArgs<double> e)
	{
		if (updatingControls)
			return;

		ExposureValueText.Text =
			FormatControlValue(e.NewValue);

		if (mediaCapture == null)
			return;

		MediaDeviceControl control =
			mediaCapture.VideoDeviceController.Exposure;

		if (!control.Capabilities.Supported)
			return;

		if (!control.TrySetValue(e.NewValue))
		{
			Debug.Log(
				$"[Camera] Failed to set exposure: {e.NewValue}");
		}
	}

	private void BrightnessSlider_ValueChanged(
		object sender,
		RoutedPropertyChangedEventArgs<double> e)
	{
		if (updatingControls)
			return;

		BrightnessValueText.Text =
			FormatControlValue(e.NewValue);

		if (mediaCapture == null)
			return;

		MediaDeviceControl control =
			mediaCapture.VideoDeviceController.Brightness;

		if (!control.Capabilities.Supported)
			return;

		if (!control.TrySetValue(e.NewValue))
		{
			Debug.Log(
				$"[Camera] Failed to set brightness: {e.NewValue}");
		}
	}

	private void ContrastSlider_ValueChanged(
		object sender,
		RoutedPropertyChangedEventArgs<double> e)
	{
		if (updatingControls)
			return;

		ContrastValueText.Text =
			FormatControlValue(e.NewValue);

		if (mediaCapture == null)
			return;

		MediaDeviceControl control =
			mediaCapture.VideoDeviceController.Contrast;

		if (!control.Capabilities.Supported)
			return;

		if (!control.TrySetValue(e.NewValue))
		{
			Debug.Log(
				$"[Camera] Failed to set contrast: {e.NewValue}");
		}
	}

	private void HueSlider_ValueChanged(
		object sender,
		RoutedPropertyChangedEventArgs<double> e)
	{
		if (updatingControls)
			return;

		HueValueText.Text =
			FormatControlValue(e.NewValue);

		if (mediaCapture == null)
			return;

		MediaDeviceControl control =
			mediaCapture.VideoDeviceController.Hue;

		if (!control.Capabilities.Supported)
			return;

		if (!control.TrySetValue(e.NewValue))
		{
			Debug.Log(
				$"[Camera] Failed to set hue: {e.NewValue}");
		}
	}

	private void DisableCameraControls()
	{
		updatingControls = true;

		try
		{
			ExposureSlider.IsEnabled = false;
			BrightnessSlider.IsEnabled = false;
			ContrastSlider.IsEnabled = false;
			HueSlider.IsEnabled = false;

			ExposureValueText.Text = "-";
			BrightnessValueText.Text = "-";
			ContrastValueText.Text = "-";
			HueValueText.Text = "-";
		}
		finally
		{
			updatingControls = false;
		}
	}

	private MediaFrameSource FindVideoSource()
	{
		if (mediaCapture == null)
			return null;

		foreach (KeyValuePair<string, MediaFrameSource> kvp
		         in mediaCapture.FrameSources)
		{
			MediaStreamType streamType =
				kvp.Value.Info.MediaStreamType;

			if (streamType == MediaStreamType.VideoPreview ||
			    streamType == MediaStreamType.VideoRecord)
			{
				return kvp.Value;
			}
		}

		return null;
	}

	private void OnFrameArrived(
		MediaFrameReader sender,
		MediaFrameArrivedEventArgs args)
	{
		if (!isCapturing)
			return;

		if (Interlocked.CompareExchange(
			    ref renderingFrame,
			    1,
			    0) != 0)
		{
			return;
		}

		try
		{
			using MediaFrameReference frame =
				sender.TryAcquireLatestFrame();

			SoftwareBitmap softwareBitmap =
				frame?.VideoMediaFrame?.SoftwareBitmap;

			if (softwareBitmap == null)
				return;

			bool converted = false;

			if (softwareBitmap.BitmapPixelFormat !=
			    BitmapPixelFormat.Bgra8 ||
			    softwareBitmap.BitmapAlphaMode !=
			    BitmapAlphaMode.Premultiplied)
			{
				softwareBitmap = SoftwareBitmap.Convert(
					softwareBitmap,
					BitmapPixelFormat.Bgra8,
					BitmapAlphaMode.Premultiplied);

				converted = true;
			}

			try
			{
				PushToFrameBuffer(softwareBitmap);
				Dispatcher.Invoke(
					() => RenderFrame(softwareBitmap));
			}
			catch (TaskCanceledException)
			{
			}
			finally
			{
				if (converted)
				{
					softwareBitmap.Dispose();
				}
			}
		}
		finally
		{
			Interlocked.Exchange(ref renderingFrame, 0);
		}
	}

	private unsafe void RenderFrame(SoftwareBitmap bitmap)
	{
		int width = bitmap.PixelWidth;
		int height = bitmap.PixelHeight;

		if (previewBitmap == null ||
		    previewBitmap.PixelWidth != width ||
		    previewBitmap.PixelHeight != height)
		{
			previewBitmap = new WriteableBitmap(
				width,
				height,
				96,
				96,
				PixelFormats.Pbgra32,
				null);

			PreviewImage.Source = previewBitmap;
		}

		using BitmapBuffer buffer =
			bitmap.LockBuffer(BitmapBufferAccessMode.Read);

		BitmapPlaneDescription plane =
			buffer.GetPlaneDescription(0);

		using Windows.Foundation.IMemoryBufferReference reference =
			buffer.CreateReference();

		reference
			.As<IMemoryBufferByteAccess>()
			.GetBuffer(
				out byte* sourceData,
				out uint capacity);

		previewBitmap.Lock();

		try
		{
			int sourceStride = plane.Stride;
			int destStride = previewBitmap.BackBufferStride;

			byte* dest =
				(byte*)previewBitmap.BackBuffer;

			if (sourceStride == destStride)
			{
				uint copySize =
					(uint)(destStride * height);

				System.Buffer.MemoryCopy(
					sourceData,
					dest,
					copySize,
					copySize);
			}
			else
			{
				int rowBytes =
					Math.Min(sourceStride, destStride);

				for (int y = 0; y < height; y++)
				{
					System.Buffer.MemoryCopy(
						sourceData + (y * sourceStride),
						dest + (y * destStride),
						rowBytes,
						rowBytes);
				}
			}

			previewBitmap.AddDirtyRect(
				new Int32Rect(
					0,
					0,
					width,
					height));
		}
		finally
		{
			previewBitmap.Unlock();
		}
	}

	private unsafe void PushToFrameBuffer(SoftwareBitmap bitmap)
	{
		int width = bitmap.PixelWidth;
		int height = bitmap.PixelHeight;

		using BitmapBuffer buffer =
			bitmap.LockBuffer(BitmapBufferAccessMode.Read);

		BitmapPlaneDescription plane =
			buffer.GetPlaneDescription(0);

		int stride = plane.Stride;

		if (frameBuffer == null ||
		    frameBuffer.Capacity < FrameBufferSlots)
		{
			frameBuffer = new FrameBuffer(
				FrameBufferSlots, stride * height);
		}

		FrameSlot slot = frameBuffer.Push(width, height, stride);

		using Windows.Foundation.IMemoryBufferReference reference =
			buffer.CreateReference();

		reference
			.As<IMemoryBufferByteAccess>()
			.GetBuffer(
				out byte* sourceData,
				out uint capacity);

		int copySize = stride * height;

		fixed (byte* dest = slot.Data)
		{
			System.Buffer.MemoryCopy(
				sourceData,
				dest,
				slot.Data.Length,
				copySize);
		}
	}

	#region ---- Mark Rect ----

	private void MarkButton_Checked(object sender, RoutedEventArgs e)
	{
		OverlayCanvas.Cursor = Cursors.Cross;
	}

	private void MarkButton_Unchecked(object sender, RoutedEventArgs e)
	{
		OverlayCanvas.Cursor = Cursors.Arrow;
	}

	private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
	{
		if (MarkButton.IsChecked != true) return;
		if (PreviewImage.Source is not BitmapSource) return;

		Point pixel = ScreenToImagePixel(e.GetPosition(OverlayCanvas));
		dragStartPixel = pixel;
		isDragging = true;
		OverlayCanvas.CaptureMouse();

		markX = (int)pixel.X;
		markY = (int)pixel.Y;
		markW = 0;
		markH = 0;
		SetRectFields();
		UpdateOverlayRect();
		MarkRect.Visibility = Visibility.Visible;
	}

	private void Overlay_MouseMove(object sender, MouseEventArgs e)
	{
		if (!isDragging) return;

		Point pixel = ScreenToImagePixel(e.GetPosition(OverlayCanvas));

		double x0 = Math.Min(dragStartPixel.X, pixel.X);
		double y0 = Math.Min(dragStartPixel.Y, pixel.Y);
		double x1 = Math.Max(dragStartPixel.X, pixel.X);
		double y1 = Math.Max(dragStartPixel.Y, pixel.Y);

		markX = (int)x0;
		markY = (int)y0;
		markW = (int)(x1 - x0);
		markH = (int)(y1 - y0);

		SetRectFields();
		UpdateOverlayRect();
	}

	private void Overlay_MouseUp(object sender, MouseButtonEventArgs e)
	{
		if (!isDragging) return;
		isDragging = false;
		OverlayCanvas.ReleaseMouseCapture();
	}

	private void RectBox_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (updatingRectFields) return;

		int.TryParse(RectXBox.Text, out markX);
		int.TryParse(RectYBox.Text, out markY);
		int.TryParse(RectWBox.Text, out markW);
		int.TryParse(RectHBox.Text, out markH);

		UpdateOverlayRect();
	}

	private void OverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		UpdateOverlayRect();
	}

	private void SetRectFields()
	{
		updatingRectFields = true;
		RectXBox.Text = markX.ToString();
		RectYBox.Text = markY.ToString();
		RectWBox.Text = markW.ToString();
		RectHBox.Text = markH.ToString();
		updatingRectFields = false;
	}

	private void UpdateOverlayRect()
	{
		if (PreviewImage.Source is not BitmapSource source) return;
		if (markW <= 0 || markH <= 0)
		{
			MarkRect.Visibility = Visibility.Collapsed;
			return;
		}

		double canvasW = OverlayCanvas.ActualWidth;
		double canvasH = OverlayCanvas.ActualHeight;
		if (canvasW <= 0 || canvasH <= 0) return;

		double imageW = source.PixelWidth;
		double imageH = source.PixelHeight;
		double scale = Math.Min(canvasW / imageW, canvasH / imageH);
		double offsetX = (canvasW - imageW * scale) / 2;
		double offsetY = (canvasH - imageH * scale) / 2;

		Canvas.SetLeft(MarkRect, offsetX + markX * scale);
		Canvas.SetTop(MarkRect, offsetY + markY * scale);
		MarkRect.Width = markW * scale;
		MarkRect.Height = markH * scale;
		MarkRect.Visibility = Visibility.Visible;
	}

	private Point ScreenToImagePixel(Point screenPoint)
	{
		if (PreviewImage.Source is not BitmapSource source)
			return new Point(0, 0);

		double canvasW = OverlayCanvas.ActualWidth;
		double canvasH = OverlayCanvas.ActualHeight;
		if (canvasW <= 0 || canvasH <= 0)
			return new Point(0, 0);

		double imageW = source.PixelWidth;
		double imageH = source.PixelHeight;
		double scale = Math.Min(canvasW / imageW, canvasH / imageH);
		double offsetX = (canvasW - imageW * scale) / 2;
		double offsetY = (canvasH - imageH * scale) / 2;

		double pixelX = (screenPoint.X - offsetX) / scale;
		double pixelY = (screenPoint.Y - offsetY) / scale;

		return new Point(
			Math.Clamp(pixelX, 0, imageW),
			Math.Clamp(pixelY, 0, imageH));
	}

	private void RememberButton_Click(object sender, RoutedEventArgs e)
	{
		WriteableBitmap region = CaptureRectRegion();

		if (region == null)
			return;

		rememberedBitmap = region;
		RememberedPreview.Source = rememberedBitmap;
	}

	private void CompareButton_Click(object sender, RoutedEventArgs e)
	{
		if (rememberedBitmap == null)
		{
			CompareResultText.Text = "No remembered image.";
			return;
		}

		WriteableBitmap current = CaptureRectRegion();

		if (current == null)
		{
			CompareResultText.Text = "No valid rect or preview.";
			return;
		}

		if (current.PixelWidth != rememberedBitmap.PixelWidth ||
		    current.PixelHeight != rememberedBitmap.PixelHeight)
		{
			CompareResultText.Text = "Size mismatch.";
			return;
		}

		double similarity = ComputeSsim(current, rememberedBitmap);
		CompareResultText.Text = $"{similarity * 100:F2}%";
	}

	private WriteableBitmap CaptureRectRegion()
	{
		if (previewBitmap == null || markW <= 0 || markH <= 0)
			return null;

		int srcW = previewBitmap.PixelWidth;
		int srcH = previewBitmap.PixelHeight;

		int x = Math.Clamp(markX, 0, srcW);
		int y = Math.Clamp(markY, 0, srcH);
		int w = Math.Min(markW, srcW - x);
		int h = Math.Min(markH, srcH - y);

		if (w <= 0 || h <= 0)
			return null;

		int stride = w * 4;
		byte[] pixels = new byte[stride * h];
		previewBitmap.CopyPixels(new Int32Rect(x, y, w, h), pixels, stride, 0);

		WriteableBitmap region = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
		region.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
		return region;
	}

	#endregion

	private async Task StopPreviewAsync()
	{
		isCapturing = false;

		DisableCameraControls();

		if (frameReader != null)
		{
			frameReader.FrameArrived -= OnFrameArrived;

			try
			{
				await frameReader.StopAsync();
			}
			catch
			{
			}

			frameReader.Dispose();
			frameReader = null;
		}

		if (mediaCapture != null)
		{
			mediaCapture.Dispose();
			mediaCapture = null;
		}
	}

	private void ShowStatus(string message)
	{
		StatusText.Text = message;
		StatusText.Visibility = Visibility.Visible;
	}

	[ComImport]
	[Guid("5b0d3235-4dba-4d44-865e-8f1d0e4fd04d")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private unsafe interface IMemoryBufferByteAccess
	{
		void GetBuffer(
			out byte* buffer,
			out uint capacity);
	}

	private sealed record CameraListItem(
		string Id,
		string Name);
}