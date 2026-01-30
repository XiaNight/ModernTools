// GpuUvChartTwoBuffers.cs
// NuGet: ComputeSharp
// Project: net6.0-windows (or newer) + <UseWPF>true</UseWPF>
// Requires: <AllowUnsafeBlocks>true</AllowUnsafeBlocks>

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ComputeSharp;

namespace Base.Components.Chart.GPUChart;

public partial class GpuChart : Image, IDisposable
{
    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(
            nameof(Size),
            typeof(int),
            typeof(GpuChart),
            new PropertyMetadata(256, static (d, _) => ((GpuChart)d).RecreateAndRender()));

    public int Size
    {
        get => (int)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private GraphicsDevice _device;
    private ReadWriteTexture2D<Bgra32, float4> _texture;
    private WriteableBitmap _bmp;

    public GpuChart()
    {
        // IMPORTANT: allow WPF layout to size the control, and scale the fixed-size Source.
        // Pick one: Fill / Uniform / UniformToFill / None
        Stretch = Stretch.Fill;
        StretchDirection = StretchDirection.Both;

        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        SnapsToDevicePixels = true;

        // Optional: choose scaling quality (NearestNeighbor for crisp pixels, HighQuality for smooth)
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);

        Loaded += (_, _) => RecreateAndRender();
        Unloaded += (_, _) => Dispose();
    }

    public void RecreateAndRender()
    {
        int size = Math.Clamp(Size, 1, 8192);

        _device ??= GraphicsDevice.GetDefault();

        if (_texture is null || _texture.Width != size || _texture.Height != size)
        {
            _texture?.Dispose();
            _texture = _device.AllocateReadWriteTexture2D<Bgra32, float4>(size, size);
        }

        if (_bmp is null || _bmp.PixelWidth != size || _bmp.PixelHeight != size)
        {
            _bmp = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
            Source = _bmp;

            // DO NOT set Width/Height here. Let layout decide, Stretch will scale Source.
        }

        Render();
    }

    public void Render()
    {
        if (_device is null || _texture is null || _bmp is null)
            return;

        _device.For(_texture.Width, _texture.Height, new UvGradient(_texture, (float)(DateTime.Now.Ticks / 10_000d)));

        CopyToWriteableBitmap(_texture, _bmp);
    }

    public void Dispose()
    {
        _texture?.Dispose();
        _texture = null;

        Source = null;
        _bmp = null;

        _device = null; // do not dispose GetDefault() device
    }

    public static void CopyToWriteableBitmap(
        ReadWriteTexture2D<Bgra32, float4> texture,
        WriteableBitmap bmp,
        bool addDirtyRect = true)
    {
        if (texture is null) throw new ArgumentNullException(nameof(texture));
        if (bmp is null) throw new ArgumentNullException(nameof(bmp));

        int w = bmp.PixelWidth;
        int h = bmp.PixelHeight;

        if (texture.Width != w || texture.Height != h)
            throw new ArgumentException("Texture and bitmap dimensions must match.");

        bmp.Lock();
        try
        {
            int strideBytes = bmp.BackBufferStride;

            unsafe
            {
                byte* basePtr = (byte*)bmp.BackBuffer;

                for (int y = 0; y < h; y++)
                {
                    var row = new Span<Bgra32>(basePtr + (y * strideBytes), w);
                    texture.CopyTo(row, 0, y, w, 1);
                }
            }

            if (addDirtyRect)
                bmp.AddDirtyRect(new Int32Rect(0, 0, w, h));
        }
        finally
        {
            bmp.Unlock();
        }
    }

    [ThreadGroupSize(DefaultThreadGroupSizes.XY)]
    [GeneratedComputeShaderDescriptor]
    public readonly partial struct UvGradient(IReadWriteNormalizedTexture2D<float4> target, float timeMs) : IComputeShader
    {
        public void Execute()
        {
            float2 uv = ThreadIds.Normalized.XY;     // [0,1]
            float2 p = uv * 2f - 1f;                // [-1,1]
            p.X *= DispatchSize.X / (float)DispatchSize.Y; // aspect-correct

            float t = timeMs * 0.001f;

            // Swirly domain-warped polar field
            float r = Hlsl.Length(p);
            float a = Hlsl.Atan2(p.Y, p.X);

            float swirl = Hlsl.Sin(a * 6f + t * 1.8f) * 0.35f + Hlsl.Sin(r * 10f - t * 2.2f) * 0.18f;

            float2 q = p;
            q += new float2(
                Hlsl.Sin((p.Y + swirl) * 3.2f + t * 1.3f),
                Hlsl.Sin((p.X - swirl) * 3.6f - t * 1.1f)
            ) * 0.25f;

            float rq = Hlsl.Length(q);
            float aq = Hlsl.Atan2(q.Y, q.X);

            float bands = 0.5f + 0.5f * Hlsl.Sin(rq * 18f - t * 3.5f + Hlsl.Sin(aq * 4f + t));
            float rays = 0.5f + 0.5f * Hlsl.Sin(aq * 10f + t * 2.0f + rq * 2.0f);

            float vignette = Hlsl.Saturate(1.1f - rq * 0.85f);
            float glow = Hlsl.Exp(-rq * rq * 2.2f);

            // Palette
            float3 baseCol = new float3(
                0.55f + 0.45f * Hlsl.Sin(t * 0.7f + rq * 2.0f),
                0.55f + 0.45f * Hlsl.Sin(t * 0.9f + rq * 2.4f + 2.1f),
                0.55f + 0.45f * Hlsl.Sin(t * 1.1f + rq * 2.8f + 4.2f)
            );

            float m = Hlsl.Saturate(0.45f * bands + 0.55f * rays);
            float3 col = baseCol * (0.35f + 0.65f * m);

            // Highlight arcs + subtle noise-ish shimmer
            float arcs = Hlsl.SmoothStep(0.75f, 1.0f, Hlsl.Sin(aq * 3f - t * 1.6f) * 0.5f + 0.5f);
            col += new float3(0.15f, 0.25f, 0.35f) * arcs * glow;

            float shimmer = Hlsl.Frac(Hlsl.Sin(Hlsl.Dot(uv * 512f, new float2(12.9898f, 78.233f))) * 43758.5453f);
            col *= 0.96f + 0.08f * shimmer;

            col *= vignette;
            col += glow * 0.08f;

            target[ThreadIds.XY] = new float4(Hlsl.Saturate(col), 1f);
        }
    }
}
