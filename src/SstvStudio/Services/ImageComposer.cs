// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 OM1PU

using SkiaSharp;
using SstvStudio.Core.Imaging;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace SstvStudio.Services;

/// <summary>Scales the source picture to the mode's resolution and draws the overlay text lines.</summary>
public static class ImageComposer
{
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    public static SKBitmap? Load(string path) => SKBitmap.Decode(path);

    /// <param name="overlay">Lines already expanded (placeholders filled) with their style.</param>
    public static SKBitmap Compose(SKBitmap? source, int width, int height, ImageFit fit,
        IReadOnlyList<(string Text, OverlayLine Style)> overlay)
    {
        var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Black);

        if (source is null)
            DrawTestPattern(canvas, width, height);
        else
            DrawSource(canvas, source, width, height, fit);

        DrawOverlay(canvas, overlay, width, height);
        return bitmap;
    }

    public static RgbImage ToRgbImage(SKBitmap bitmap)
    {
        var rgba = bitmap.GetPixelSpan();
        var rgb = new byte[bitmap.Width * bitmap.Height * 3];
        for (int i = 0, j = 0; j < rgb.Length; i += 4, j += 3)
        {
            rgb[j] = rgba[i];
            rgb[j + 1] = rgba[i + 1];
            rgb[j + 2] = rgba[i + 2];
        }
        return new RgbImage(bitmap.Width, bitmap.Height, rgb);
    }

    public static SKBitmap FromRgbImage(RgbImage image)
    {
        var rgba = new byte[image.Width * image.Height * 4];
        for (int i = 0, j = 0; j < image.Data.Length; i += 4, j += 3)
        {
            rgba[i] = image.Data[j];
            rgba[i + 1] = image.Data[j + 1];
            rgba[i + 2] = image.Data[j + 2];
            rgba[i + 3] = 255;
        }
        var bitmap = new SKBitmap(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        return bitmap;
    }

    public static AvaloniaBitmap ToAvaloniaBitmap(RgbImage image)
    {
        using var bitmap = FromRgbImage(image);
        return ToAvaloniaBitmap(bitmap);
    }

    public static void SavePng(RgbImage image, string path)
    {
        using var bitmap = FromRgbImage(image);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    public static AvaloniaBitmap ToAvaloniaBitmap(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new AvaloniaBitmap(stream);
    }

    private static void DrawSource(SKCanvas canvas, SKBitmap source, int width, int height, ImageFit fit)
    {
        using var image = SKImage.FromBitmap(source);
        var src = SKRect.Create(source.Width, source.Height);
        var dst = SKRect.Create(width, height);

        if (fit != ImageFit.Stretch)
        {
            var scale = fit == ImageFit.Fill
                ? Math.Max((float)width / source.Width, (float)height / source.Height)
                : Math.Min((float)width / source.Width, (float)height / source.Height);

            if (fit == ImageFit.Fill)
            {
                // Crop the centre of the source to the destination aspect ratio.
                var cropW = width / scale;
                var cropH = height / scale;
                src = SKRect.Create((source.Width - cropW) / 2, (source.Height - cropH) / 2, cropW, cropH);
            }
            else
            {
                var w = source.Width * scale;
                var h = source.Height * scale;
                dst = SKRect.Create((width - w) / 2, (height - h) / 2, w, h);
            }
        }

        using var paint = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, src, dst, Sampling, paint);
    }

    private static void DrawTestPattern(SKCanvas canvas, int width, int height)
    {
        SKColor[] bars =
        [
            SKColors.White, SKColors.Yellow, SKColors.Cyan, SKColors.Lime,
            SKColors.Magenta, SKColors.Red, SKColors.Blue, SKColors.Black,
        ];
        var barW = width / (float)bars.Length;
        var barH = height * 0.62f;
        using var paint = new SKPaint();
        for (var i = 0; i < bars.Length; i++)
        {
            paint.Color = bars[i];
            canvas.DrawRect(i * barW, 0, barW + 1, barH, paint);
        }

        using var ramp = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(width, 0), [SKColors.Black, SKColors.White], SKShaderTileMode.Clamp);
        paint.Shader = ramp;
        canvas.DrawRect(0, barH, width, height - barH, paint);
    }

    /// <summary>
    /// Lines sharing a position are stacked in list order: the top block hangs from the top margin, the
    /// bottom block rests on the bottom margin and the middle block is centred.
    /// </summary>
    private static void DrawOverlay(SKCanvas canvas, IReadOnlyList<(string Text, OverlayLine Style)> lines, int width, int height)
    {
        var margin = height * 0.03f;
        foreach (var group in lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).GroupBy(l => l.Style.Position))
        {
            var fonts = group.Select(l => (l.Text, l.Style, Font: CreateFont(l.Text, l.Style, width, height))).ToList();
            try
            {
                var blockHeight = fonts.Sum(f => LineHeight(f.Font));
                var y = group.Key switch
                {
                    OverlayPosition.Top => margin,
                    OverlayPosition.Middle => (height - blockHeight) / 2,
                    _ => height - margin - blockHeight,
                };
                foreach (var (text, style, font) in fonts)
                {
                    DrawLine(canvas, text, style, font, y - font.Metrics.Ascent, width);
                    y += LineHeight(font);
                }
            }
            finally
            {
                // Typefaces are cached by Skia's font manager (and Default is shared), so only the fonts go.
                foreach (var f in fonts) f.Font.Dispose();
            }
        }
    }

    private static SKFont CreateFont(string text, OverlayLine style, int width, int height)
    {
        var typeface = SKTypeface.FromFamilyName(style.FontFamily,
                           style.Bold ? SKFontStyle.Bold : SKFontStyle.Normal) ?? SKTypeface.Default;
        var font = new SKFont(typeface, height * (float)style.SizePercent / 100f);
        // Shrink long lines so they always fit with a margin.
        var maxWidth = width * 0.94f;
        var measured = font.MeasureText(text);
        if (measured > maxWidth) font.Size *= maxWidth / measured;
        return font;
    }

    private static float LineHeight(SKFont font) => (font.Metrics.Descent - font.Metrics.Ascent) * 1.05f;

    private static void DrawLine(SKCanvas canvas, string text, OverlayLine style, SKFont font, float baseline, int width)
    {
        var (x, align) = style.Align switch
        {
            OverlayAlign.Left => (width * 0.03f, SKTextAlign.Left),
            OverlayAlign.Right => (width * 0.97f, SKTextAlign.Right),
            _ => (width / 2f, SKTextAlign.Center),
        };

        if (style.Outline)
        {
            using var stroke = new SKPaint
            {
                IsAntialias = true, Style = SKPaintStyle.Stroke, Color = SKColors.Black,
                StrokeWidth = Math.Max(2, font.Size * 0.14f), StrokeJoin = SKStrokeJoin.Round,
            };
            canvas.DrawText(text, x, baseline, align, font, stroke);
        }
        var color = SKColor.TryParse(style.Color, out var c) ? c : SKColors.White;
        using var fill = new SKPaint { IsAntialias = true, Color = color };
        canvas.DrawText(text, x, baseline, align, font, fill);
    }
}
