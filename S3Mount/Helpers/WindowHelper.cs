using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace S3Mount.Helpers;

public static class WindowHelper
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    public static void EnableDarkModeForWindow(Window window)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            var hwnd = helper.Handle;

            if (hwnd == IntPtr.Zero)
            {
                // Window not yet created, hook the SourceInitialized event
                window.SourceInitialized += (s, e) =>
                {
                    var h = new WindowInteropHelper(window).Handle;
                    SetDarkMode(h);
                };
            }
            else
            {
                SetDarkMode(hwnd);
            }
        }
        catch
        {
            // Silently fail on older Windows versions
        }
    }

    private static void SetDarkMode(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        try
        {
            // Try Windows 11 first
            int useImmersiveDarkMode = 1;
            int result = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useImmersiveDarkMode, sizeof(int));

            // If that fails, try Windows 10 20H1+
            if (result != 0)
            {
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useImmersiveDarkMode, sizeof(int));
            }
        }
        catch
        {
            // Silently fail on unsupported systems
        }
    }
}
