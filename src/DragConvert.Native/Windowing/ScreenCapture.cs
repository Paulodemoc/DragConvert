using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace DragConvert.Native.Windowing;

/// <summary>A captured screen area as 32-bit BGRA pixels, top row first.</summary>
public sealed record ScreenPixels(byte[] Bgra, int Width, int Height)
{
    public int Stride => Width * 4;
}

/// <summary>
/// Copies a small screen area into memory (GDI BitBlt). The glass overlay blurs it to fake a frosted backdrop with
/// round edges, which the DWM blur APIs can't do. Pixels are never written to disk.
/// </summary>
public static class ScreenCapture
{
    public static unsafe ScreenPixels? Capture(ScreenRect area)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return null;
        }

        HDC screen = PInvoke.GetDC(HWND.Null);
        HDC memory = PInvoke.CreateCompatibleDC(screen);
        HBITMAP bitmap = PInvoke.CreateCompatibleBitmap(screen, area.Width, area.Height);
        HGDIOBJ previous = PInvoke.SelectObject(memory, bitmap);
        try
        {
            if (!PInvoke.BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, ROP_CODE.SRCCOPY))
            {
                return null;
            }

            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = (uint)sizeof(BITMAPINFOHEADER),
                    biWidth = area.Width,
                    biHeight = -area.Height, // negative: top-down rows
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0, // BI_RGB
                },
            };
            byte[] pixels = new byte[area.Width * area.Height * 4];
            fixed (byte* p = pixels)
            {
                PInvoke.SelectObject(memory, previous);
                int rows = PInvoke.GetDIBits(memory, bitmap, 0, (uint)area.Height, p, &info, DIB_USAGE.DIB_RGB_COLORS);
                if (rows == 0)
                {
                    return null;
                }
            }

            // GDI leaves the alpha byte at zero; make every pixel opaque.
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 255;
            }

            return new ScreenPixels(pixels, area.Width, area.Height);
        }
        finally
        {
            PInvoke.SelectObject(memory, previous);
            PInvoke.DeleteObject(bitmap);
            PInvoke.DeleteDC(memory);
            _ = PInvoke.ReleaseDC(HWND.Null, screen);
        }
    }
}
