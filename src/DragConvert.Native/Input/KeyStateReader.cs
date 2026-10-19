using DragConvert.Core.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace DragConvert.Native.Input;

/// <summary>
/// Reads modifier and Escape state with GetAsyncKeyState. Polled only while a drag is active,
/// so the app needs no global keyboard hook.
/// </summary>
public static class KeyStateReader
{
    public static KeyState Read() => new(
        Control: IsDown(VIRTUAL_KEY.VK_CONTROL),
        Shift: IsDown(VIRTUAL_KEY.VK_SHIFT),
        Alt: IsDown(VIRTUAL_KEY.VK_MENU),
        Escape: IsDown(VIRTUAL_KEY.VK_ESCAPE));

    private static bool IsDown(VIRTUAL_KEY key) => (PInvoke.GetAsyncKeyState((int)key) & 0x8000) != 0;
}
