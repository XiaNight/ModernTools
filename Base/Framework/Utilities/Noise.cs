using System;

namespace Base.Framework.Utilities;

/// <summary>
/// Procedural noise helpers (deterministic, allocation-free). Original
/// implementation using standard hash-lattice techniques.
///
/// <see cref="Perlin"/> is smooth gradient-free value noise with optional fractal
/// (fBm) layering; <see cref="Voronoi"/> is cellular (Worley) noise returning the
/// nearest and second-nearest feature-point distances. Callers compose them, e.g.
/// domain-warping a Voronoi lookup with Perlin for a water-caustics effect.
/// </summary>
public static class Noise
{
    /// <summary>
    /// Smooth 2D value ("Perlin-like") noise in the range 0..1.
    /// </summary>
    /// <param name="x">Sample X.</param>
    /// <param name="y">Sample Y.</param>
    /// <param name="frequency">Scales the sample coordinates (higher = finer detail).</param>
    /// <param name="octaves">Number of fBm layers summed (1 = plain noise).</param>
    /// <param name="persistence">Amplitude multiplier applied per octave (0..1).</param>
    /// <param name="lacunarity">Frequency multiplier applied per octave.</param>
    /// <param name="seed">Offsets the lattice so different seeds give different fields.</param>
    public static float Perlin(
        float x,
        float y,
        float frequency = 1f,
        int octaves = 1,
        float persistence = 0.5f,
        float lacunarity = 2f,
        float seed = 0f)
    {
        float sum = 0f;
        float norm = 0f;
        float amp = 1f;
        float freq = frequency;

        int count = Math.Max(1, octaves);
        for (int o = 0; o < count; o++)
        {
            sum += amp * ValueNoise((x * freq) + seed, (y * freq) + seed);
            norm += amp;
            amp *= persistence;
            freq *= lacunarity;
        }

        return norm > 0f ? sum / norm : 0f;
    }

    /// <summary>
    /// Cellular (Worley) noise. Returns the distance to the nearest feature point
    /// (F1) and to the second nearest (F2); their difference is small along cell
    /// borders, which is useful for edge/vein effects.
    /// </summary>
    /// <param name="x">Sample X (integer part selects the cell).</param>
    /// <param name="y">Sample Y.</param>
    /// <param name="jitter">How far feature points stray from cell centres (0 = regular grid, 1 = fully random).</param>
    /// <param name="seed">Offsets the feature-point hashing.</param>
    public static (float f1, float f2) Voronoi(
        float x,
        float y,
        float jitter = 1f,
        float seed = 0f)
    {
        int cellX = (int)MathF.Floor(x);
        int cellY = (int)MathF.Floor(y);
        float fx = x - cellX;
        float fy = y - cellY;

        float bias = (1f - Clamp01(jitter)) * 0.5f; // pulls points toward cell centre
        float amt = Clamp01(jitter);

        float d1 = float.MaxValue;
        float d2 = float.MaxValue;

        for (int gy = -1; gy <= 1; gy++)
        {
            for (int gx = -1; gx <= 1; gx++)
            {
                float ox = bias + (amt * Hash(cellX + gx, cellY + gy, seed));
                float oy = bias + (amt * Hash(cellX + gx, cellY + gy, seed + 37.2f));

                float dx = gx + ox - fx;
                float dy = gy + oy - fy;
                float d = MathF.Sqrt((dx * dx) + (dy * dy));

                if (d < d1) { d2 = d1; d1 = d; }
                else if (d < d2) { d2 = d; }
            }
        }

        return (d1, d2);
    }

    // --- internals --------------------------------------------------------------

    // Smooth 2D value noise on the integer lattice, 0..1.
    private static float ValueNoise(float x, float y)
    {
        float xi = MathF.Floor(x);
        float yi = MathF.Floor(y);
        float xf = x - xi;
        float yf = y - yi;

        float u = xf * xf * (3f - (2f * xf)); // smoothstep
        float v = yf * yf * (3f - (2f * yf));

        float a = Hash(xi, yi, 0f);
        float b = Hash(xi + 1f, yi, 0f);
        float c = Hash(xi, yi + 1f, 0f);
        float d = Hash(xi + 1f, yi + 1f, 0f);

        return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
    }

    // Deterministic hash in 0..1 from a 2D lattice point and a seed.
    private static float Hash(float x, float y, float seed)
    {
        float n = MathF.Sin((x * 127.1f) + (y * 311.7f) + (seed * 74.7f)) * 43758.5453f;
        return n - MathF.Floor(n);
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float Clamp01(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
}
