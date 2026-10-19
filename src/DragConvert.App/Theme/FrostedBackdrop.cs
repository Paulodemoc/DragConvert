using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DragConvert.App.Theme;

/// <summary>
/// Blurs a captured screen area on the CPU (downscaled 3x, then three box-blur passes per axis, which approximates a
/// Gaussian) and crops off the capture margin. Done outside WPF's visual tree because effects on layered windows are slow and fragile.
/// </summary>
internal static class FrostedBackdrop
{
    /// <param name="bgra">32-bit BGRA pixels, top row first.</param>
    /// <param name="margin">Pixels captured beyond the visible area on each side; cropped after blurring.</param>
    /// <param name="sigma">Gaussian-equivalent blur strength in pixels.</param>
    /// <returns>The blurred image and its average brightness (0 black to 1 white).</returns>
    public static (BitmapSource Image, double Brightness) Create(byte[] bgra, int width, int height, int margin, double sigma, double dpi)
    {
        // The result is blurred anyway: shrink first (block average), blur the small image, let WPF scale it back up.
        const int Factor = 3;
        byte[] work = Downscale(bgra, width, height, Factor, out width, out height);
        margin /= Factor;
        sigma /= Factor;
        dpi /= Factor;
        byte[] temp = new byte[work.Length];
        int boxRadius = Math.Max(1, (int)Math.Round(sigma * Math.Sqrt(3) / 1.7));
        for (int pass = 0; pass < 3; pass++)
        {
            BoxBlurHorizontal(work, temp, width, height, boxRadius);
            BoxBlurVertical(temp, work, width, height, boxRadius);
        }

        int outWidth = width - (2 * margin);
        int outHeight = height - (2 * margin);
        byte[] cropped = new byte[outWidth * outHeight * 4];
        for (int y = 0; y < outHeight; y++)
        {
            Buffer.BlockCopy(work, (((y + margin) * width) + margin) * 4, cropped, y * outWidth * 4, outWidth * 4);
        }

        double luminance = 0;
        for (int i = 0; i < cropped.Length; i += 4)
        {
            luminance += (0.0722 * cropped[i]) + (0.7152 * cropped[i + 1]) + (0.2126 * cropped[i + 2]);
        }

        var bitmap = BitmapSource.Create(outWidth, outHeight, dpi, dpi, PixelFormats.Bgra32, null, cropped, outWidth * 4);
        bitmap.Freeze();
        return (bitmap, luminance / (255.0 * outWidth * outHeight));
    }

    private static byte[] Downscale(byte[] source, int width, int height, int factor, out int newWidth, out int newHeight)
    {
        int w = width / factor;
        int h = height / factor;
        byte[] target = new byte[w * h * 4];
        int area = factor * factor;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int b = 0, g = 0, r = 0;
                for (int dy = 0; dy < factor; dy++)
                {
                    int row = (((y * factor) + dy) * width) + (x * factor);
                    for (int dx = 0; dx < factor; dx++)
                    {
                        int i = (row + dx) * 4;
                        b += source[i];
                        g += source[i + 1];
                        r += source[i + 2];
                    }
                }

                int o = ((y * w) + x) * 4;
                target[o] = (byte)(b / area);
                target[o + 1] = (byte)(g / area);
                target[o + 2] = (byte)(r / area);
                target[o + 3] = 255;
            }
        }

        newWidth = w;
        newHeight = h;
        return target;
    }

    private static void BoxBlurHorizontal(byte[] source, byte[] target, int width, int height, int radius)
    {
        int window = (2 * radius) + 1;
        Parallel.For(0, height, y =>
        {
            int row = y * width * 4;
            for (int channel = 0; channel < 3; channel++)
            {
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    sum += source[row + (Math.Clamp(i, 0, width - 1) * 4) + channel];
                }

                for (int x = 0; x < width; x++)
                {
                    target[row + (x * 4) + channel] = (byte)(sum / window);
                    int add = Math.Min(x + radius + 1, width - 1);
                    int remove = Math.Max(x - radius, 0);
                    sum += source[row + (add * 4) + channel] - source[row + (remove * 4) + channel];
                }
            }

            for (int x = 0; x < width; x++)
            {
                target[row + (x * 4) + 3] = 255;
            }
        });
    }

    private static void BoxBlurVertical(byte[] source, byte[] target, int width, int height, int radius)
    {
        int window = (2 * radius) + 1;
        int stride = width * 4;
        Parallel.For(0, width, x =>
        {
            int column = x * 4;
            for (int channel = 0; channel < 3; channel++)
            {
                int sum = 0;
                for (int i = -radius; i <= radius; i++)
                {
                    sum += source[(Math.Clamp(i, 0, height - 1) * stride) + column + channel];
                }

                for (int y = 0; y < height; y++)
                {
                    target[(y * stride) + column + channel] = (byte)(sum / window);
                    int add = Math.Min(y + radius + 1, height - 1);
                    int remove = Math.Max(y - radius, 0);
                    sum += source[(add * stride) + column + channel] - source[(remove * stride) + column + channel];
                }
            }

            for (int y = 0; y < height; y++)
            {
                target[(y * stride) + column + 3] = 255;
            }
        });
    }
}
