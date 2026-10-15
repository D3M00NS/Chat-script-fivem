// C# / src/ZyonMenu.Windows/NativeHotKeys.cs / .NET 8 WinForms / Windows 10+ x64.
using System.Runtime.InteropServices;

namespace ZyonMenu.Windows;

internal static class NativeHotKeys
{
    public const int ToggleMenuId = 0x7A11;
    public const int HotKeyMessage = 0x0312;
    public const uint NoRepeat = 0x4000;
    public const uint InsertKey = 0x2D;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
