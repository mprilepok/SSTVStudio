// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

namespace SstvStudio.Core.Imaging;

public enum NoiseFilter { Off, Low, High }

public static class ImageFilters
{
    /// <summary>
    /// Cleans up a picture received through noise. Weak FM signals produce isolated wrong pixels ("clicks"),
    /// which a 3×3 median removes without blurring edges much (<see cref="NoiseFilter.Low"/>);
    /// <see cref="NoiseFilter.High"/> adds a 3×3 smoothing pass for the grain that remains.
    /// </summary>
    public static RgbImage Denoise(RgbImage source, NoiseFilter filter)
    {
        if (filter == NoiseFilter.Off) return source;
        var result = Median3x3(source);
        return filter == NoiseFilter.High ? Smooth3x3(result) : result;
    }

    private static RgbImage Median3x3(RgbImage src)
    {
        var dst = new RgbImage(src.Width, src.Height);
        Span<byte> window = stackalloc byte[9];
        for (var y = 0; y < src.Height; y++)
        for (var x = 0; x < src.Width; x++)
        for (var c = 0; c < 3; c++)
        {
            var n = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = Math.Clamp(x + dx, 0, src.Width - 1);
                var sy = Math.Clamp(y + dy, 0, src.Height - 1);
                window[n++] = src.Data[(sy * src.Width + sx) * 3 + c];
            }
            window.Sort();
            dst.Data[(y * src.Width + x) * 3 + c] = window[4];
        }
        return dst;
    }

    private static RgbImage Smooth3x3(RgbImage src)
    {
        // 1-2-1 kernel in both directions.
        var dst = new RgbImage(src.Width, src.Height);
        for (var y = 0; y < src.Height; y++)
        for (var x = 0; x < src.Width; x++)
        for (var c = 0; c < 3; c++)
        {
            int sum = 0, weight = 0;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var w = (dx == 0 ? 2 : 1) * (dy == 0 ? 2 : 1);
                var sx = Math.Clamp(x + dx, 0, src.Width - 1);
                var sy = Math.Clamp(y + dy, 0, src.Height - 1);
                sum += src.Data[(sy * src.Width + sx) * 3 + c] * w;
                weight += w;
            }
            dst.Data[(y * src.Width + x) * 3 + c] = (byte)((sum + weight / 2) / weight);
        }
        return dst;
    }
}
