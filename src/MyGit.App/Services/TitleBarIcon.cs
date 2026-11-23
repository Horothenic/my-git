using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace MyGit.App.Services;

/// <summary>
/// Shows the outline-only My Git icon in a window's title bar (white on dark, black on light),
/// while the taskbar and Alt+Tab keep the full icon from <see cref="Window.Icon"/>.
/// Windows uses a window's "small" icon for the title bar and its "big" icon for the taskbar,
/// so this replaces only the small one.
/// </summary>
public static class TitleBarIcon
{
    private const uint WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const uint ImageIcon = 1;
    private const uint LoadFromFile = 0x0010;
    private const int SmallIconWidthMetric = 49; // SM_CXSMICON

    /// <summary>Call from a window's constructor.</summary>
    public static void Attach(Window window)
    {
        window.Opened += (_, _) => Apply(window);
        window.ActualThemeVariantChanged += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindows() || window.TryGetPlatformHandle()?.Handle is not { } handle || handle == IntPtr.Zero)
        {
            return;
        }

        var dark = window.ActualThemeVariant != ThemeVariant.Light;
        var file = Path.Combine(AppContext.BaseDirectory, "Assets", dark ? "my-git-outline-white.ico" : "my-git-outline-black.ico");

        if (!File.Exists(file))
        {
            return;
        }

        // Pick the .ico entry that matches the title bar size at this window's DPI (16 px at 100%).
        var size = GetSystemMetricsForDpi(SmallIconWidthMetric, GetDpiForWindow(handle));
        var icon = LoadImage(IntPtr.Zero, file, ImageIcon, size, size, LoadFromFile);

        if (icon == IntPtr.Zero)
        {
            return;
        }

        // Windows returns the previous small icon; free it if it was one we loaded.
        var previous = SendMessage(handle, WmSetIcon, IconSmall, icon);
        if (previous != IntPtr.Zero && Loaded.Remove(previous))
        {
            DestroyIcon(previous);
        }

        Loaded.Add(icon);
    }

    private static readonly HashSet<IntPtr> Loaded = [];

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(IntPtr instance, string name, uint type, int width, int height, uint load);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, int wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);
}
