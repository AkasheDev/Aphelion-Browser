using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Aphelion.Desktop.UI.Views;

/// <summary>
/// The Aphelion mark converted to greys, for private mode.
/// </summary>
/// <remarks>
/// Converted at runtime from the one colour asset rather than shipped as a
/// second file, which would leave two images to keep in step whenever the mark
/// changes. Avalonia has no colour filter for an Image, and masking the asset
/// against a flat fill throws away everything but the silhouette — the logo
/// stops looking like itself. Reading the pixels keeps its shading and only
/// takes the hue out.
/// </remarks>
internal static class MonochromeLogo
{
    private static readonly Uri Source =
        new("avares://Aphelion.Desktop.UI/Assets/aphelion_logo_colored.png");

    private static Bitmap? _cached;

    /// <summary>Built once and shared; every private window draws the same mark.</summary>
    public static Bitmap? Image => _cached ??= TryBuild();

    private static WriteableBitmap? TryBuild()
    {
        try
        {
            using var stream = AssetLoader.Open(Source);
            using var original = new Bitmap(stream);

            var size = new PixelSize(original.PixelSize.Width, original.PixelSize.Height);
            var stride = size.Width * 4;
            var buffer = new byte[stride * size.Height];

            // Copied through unmanaged memory rather than a pinned array, which
            // would need the project opened up to unsafe code for one method.
            var scratch = Marshal.AllocHGlobal(buffer.Length);

            try
            {
                original.CopyPixels(new PixelRect(size), scratch, buffer.Length, stride);
                Marshal.Copy(scratch, buffer, 0, buffer.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(scratch);
            }

            // Rec. 709 luma, so the result reads the way the eye weighs the
            // original rather than as a flat average of its channels. Alpha is
            // left alone, which keeps the mark's edges as soft as they were.
            for (var i = 0; i < buffer.Length; i += 4)
            {
                var luma = (byte)Math.Clamp(
                    (0.2126 * buffer[i + 2]) + (0.7152 * buffer[i + 1]) + (0.0722 * buffer[i]),
                    0,
                    255);

                buffer[i] = luma;
                buffer[i + 1] = luma;
                buffer[i + 2] = luma;
            }

            var grey = new WriteableBitmap(size, original.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);

            using (var locked = grey.Lock())
            {
                for (var row = 0; row < size.Height; row++)
                {
                    Marshal.Copy(
                        buffer,
                        row * stride,
                        locked.Address + (row * locked.RowBytes),
                        stride);
                }
            }

            return grey;
        }
        catch (Exception)
        {
            // A missing or unreadable asset must not take the New Tab page with
            // it; the page simply shows no mark.
            return null;
        }
    }
}
