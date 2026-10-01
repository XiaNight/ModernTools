using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using Base.Services.APIService;

namespace Camera;

public partial class CameraPage
{
	// Number of frames sampled for each comparison.
	private const int ComparisonSampleCount = 5;

	// Delay between samples to avoid repeatedly sampling the same camera frame.
	private const int ComparisonSampleDelayMs = 40;

	[GET("cameras", requireMainThread: true,
		Summary = "List available cameras.",
		Description = "Returns an array of cameras, each with index, id, and name.")]
	private ApiResponse ApiListCameras()
	{
		if (CameraCombo.ItemsSource is not List<CameraListItem> items)
			return Ok(Array.Empty<object>());

		return Ok(items.Select((c, i) => new
		{
			index = i,
			id = c.Id,
			name = c.Name,
		}).ToList());
	}

	[POST("select", requireMainThread: true,
		Summary = "Select a camera by id or index.",
		Description = "Pass 'id' (device id string) or 'index' (0-based). " +
			"If both are provided, id takes precedence. " +
			"Camera switching is asynchronous; the response confirms selection, not that the stream is live.")]
	private ApiResponse ApiSelectCamera(string id = "", int index = -1)
	{
		if (CameraCombo.ItemsSource is not List<CameraListItem> items || items.Count == 0)
			return Bad("No cameras available.");

		if (!string.IsNullOrEmpty(id))
		{
			int found = items.FindIndex(c => c.Id == id);
			if (found < 0)
				return NotFound($"Camera id '{id}'");

			CameraCombo.SelectedIndex = found;
			return Ok(new { index = found, id = items[found].Id, name = items[found].Name });
		}

		if (index >= 0 && index < items.Count)
		{
			CameraCombo.SelectedIndex = index;
			return Ok(new { index, id = items[index].Id, name = items[index].Name });
		}

		return Bad("Provide a valid 'id' or 'index'.");
	}

	[GET("rect", requireMainThread: true,
		Summary = "Get the current mark rect position and size.")]
	private ApiResponse ApiGetRect()
	{
		return Ok(new { x = markX, y = markY, width = markW, height = markH });
	}

	[POST("rect", requireMainThread: true,
		Summary = "Set the mark rect position and size.",
		Description = "Pass x, y, width, height as integers. Updates both the overlay and the textboxes.")]
	private ApiResponse ApiSetRect(int x = 0, int y = 0, int width = 0, int height = 0)
	{
		markX = x;
		markY = y;
		markW = width;
		markH = height;
		SetRectFields();
		UpdateOverlayRect();

		return Ok(new
		{
			x = markX,
			y = markY,
			width = markW,
			height = markH
		});
	}

	[POST("remember", requireMainThread: true,
		Summary = "Capture and store the current marked rect region.",
		Description = "Captures the pixels inside the current mark rect from the live preview " +
			"and stores them as the remembered image. Returns the remembered region dimensions.")]
	private ApiResponse ApiRemember()
	{
		WriteableBitmap region = CaptureRectRegion();

		if (region == null)
			return Bad("No valid rect or no active preview.");

		rememberedBitmap = region;
		RememberedPreview.Source = rememberedBitmap;

		return Ok(new
		{
			width = region.PixelWidth,
			height = region.PixelHeight
		});
	}

	[POST("compare", requireMainThread: true,
		Summary = "Compare the current camera rect with the remembered image.",
		Description = "Captures several frames over a short burst and compares each frame " +
			"against the remembered image using SSIM. Returns the median SSIM to reduce " +
			"camera noise, display refresh artifacts, and transient exposure changes.")]
	private ApiResponse ApiCompare()
	{
		if (rememberedBitmap == null)
			return Bad("No remembered image. Call remember first.");

		double[] samples = new double[ComparisonSampleCount];

		for (int i = 0; i < ComparisonSampleCount; i++)
		{
			WriteableBitmap current = CaptureRectRegion();

			if (current == null)
				return Bad("No valid rect or no active preview.");

			if (current.PixelWidth != rememberedBitmap.PixelWidth ||
			    current.PixelHeight != rememberedBitmap.PixelHeight)
			{
				return Bad(
					$"Size mismatch: current rect is {current.PixelWidth}x{current.PixelHeight} " +
					$"but remembered is {rememberedBitmap.PixelWidth}x{rememberedBitmap.PixelHeight}. " +
					"Restore the original rect dimensions before comparing.");
			}

			samples[i] = ComputeSsim(current, rememberedBitmap);

			if (i + 1 < ComparisonSampleCount)
				Thread.Sleep(ComparisonSampleDelayMs);
		}

		Array.Sort(samples);

		double median;

		if ((samples.Length & 1) != 0)
		{
			median = samples[samples.Length / 2];
		}
		else
		{
			int middle = samples.Length / 2;
			median = (samples[middle - 1] + samples[middle]) * 0.5;
		}

		double average = samples.Average();
		double minimum = samples[0];
		double maximum = samples[^1];

		return Ok(new
		{
			similarity = Math.Round(median, 6),
			average = Math.Round(average, 6),
			minimum = Math.Round(minimum, 6),
			maximum = Math.Round(maximum, 6),
			samples = samples.Select(x => Math.Round(x, 6)).ToArray(),
			sampleCount = ComparisonSampleCount,
			width = rememberedBitmap.PixelWidth,
			height = rememberedBitmap.PixelHeight,
		});
	}

	/// <summary>
	/// Computes global Structural Similarity Index (SSIM) between two BGRA32 images.
	///
	/// 1.0 = identical.
	/// Values closer to 1.0 indicate greater structural similarity.
	///
	/// Luminance is calculated using Rec.709 coefficients so the comparison is
	/// primarily concerned with the displayed image structure rather than
	/// individual RGB channel noise.
	/// </summary>
	private static double ComputeSsim(WriteableBitmap a, WriteableBitmap b)
	{
		int width = a.PixelWidth;
		int height = a.PixelHeight;

		if (width != b.PixelWidth || height != b.PixelHeight)
			throw new ArgumentException("Bitmap dimensions must match.");

		int pixelCount = checked(width * height);

		if (pixelCount == 0)
			return 1.0;

		int stride = checked(width * 4);

		byte[] pixelsA = new byte[checked(stride * height)];
		byte[] pixelsB = new byte[checked(stride * height)];

		a.CopyPixels(
			new Int32Rect(0, 0, width, height),
			pixelsA,
			stride,
			0);

		b.CopyPixels(
			new Int32Rect(0, 0, width, height),
			pixelsB,
			stride,
			0);

		double sumA = 0.0;
		double sumB = 0.0;
		double sumAA = 0.0;
		double sumBB = 0.0;
		double sumAB = 0.0;

		for (int i = 0; i < pixelCount; i++)
		{
			int offset = i * 4;

			// WPF BGRA32 layout.
			double blueA = pixelsA[offset];
			double greenA = pixelsA[offset + 1];
			double redA = pixelsA[offset + 2];

			double blueB = pixelsB[offset];
			double greenB = pixelsB[offset + 1];
			double redB = pixelsB[offset + 2];

			// Rec.709 luminance.
			double luminanceA =
				0.2126 * redA +
				0.7152 * greenA +
				0.0722 * blueA;

			double luminanceB =
				0.2126 * redB +
				0.7152 * greenB +
				0.0722 * blueB;

			sumA += luminanceA;
			sumB += luminanceB;

			sumAA += luminanceA * luminanceA;
			sumBB += luminanceB * luminanceB;

			sumAB += luminanceA * luminanceB;
		}

		double n = pixelCount;

		double meanA = sumA / n;
		double meanB = sumB / n;

		double varianceA = (sumAA / n) - (meanA * meanA);
		double varianceB = (sumBB / n) - (meanB * meanB);
		double covariance = (sumAB / n) - (meanA * meanB);

		// Floating-point rounding can produce tiny negative variances.
		varianceA = Math.Max(0.0, varianceA);
		varianceB = Math.Max(0.0, varianceB);

		const double k1 = 0.01;
		const double k2 = 0.03;
		const double dynamicRange = 255.0;

		double c1 = (k1 * dynamicRange) * (k1 * dynamicRange);
		double c2 = (k2 * dynamicRange) * (k2 * dynamicRange);

		double numerator =
			(2.0 * meanA * meanB + c1) *
			(2.0 * covariance + c2);

		double denominator =
			(meanA * meanA + meanB * meanB + c1) *
			(varianceA + varianceB + c2);

		if (denominator <= double.Epsilon)
			return 1.0;

		double ssim = numerator / denominator;

		// SSIM mathematically ranges from -1 to 1. For this API, expose it as
		// a similarity metric where negative correlation is simply 0 similarity.
		return Math.Clamp(ssim, 0.0, 1.0);
	}

	private static ApiResponse Ok(object data) =>
		new() { Status = 200, Data = data };

	private static ApiResponse Bad(string message) =>
		new() { Status = 400, Data = new { error = message } };

	private static ApiResponse NotFound(string item) =>
		new() { Status = 404, Data = new { error = $"{item} not found." } };
}