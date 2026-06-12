using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Poe2PriceChecker;

internal static class ScreenCaptureService
{
    public static Screen SelectPoeScreen()
    {
        if (TryFindPathOfExileScreen(out var poeScreen, out var poeWindow))
        {
            BringWindowToForeground(poeWindow);
            return poeScreen;
        }

        return Screen.AllScreens
            .OrderByDescending(screen => screen.Bounds.Width == 3840 && screen.Bounds.Height == 2160)
            .ThenByDescending(screen => screen.Bounds.Width * screen.Bounds.Height)
            .FirstOrDefault() ?? Screen.PrimaryScreen!;
    }

    public static Bitmap CaptureScreen(Rectangle bounds)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static bool TryFindPathOfExileScreen(out Screen screen, out IntPtr windowHandle)
    {
        screen = Screen.PrimaryScreen!;
        windowHandle = IntPtr.Zero;
        var matches = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            var title = GetWindowTitle(handle);
            GetWindowThreadProcessId(handle, out var processId);
            var processName = TryGetProcessName(processId);

            if (LooksLikePathOfExile(title, processName))
            {
                matches.Add(handle);
            }

            return true;
        }, IntPtr.Zero);

        foreach (var handle in matches)
        {
            if (!GetWindowRect(handle, out var rect))
            {
                continue;
            }

            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (bounds.Width < 1000 || bounds.Height < 700)
            {
                continue;
            }

            screen = Screen.FromRectangle(bounds);
            windowHandle = handle;
            return true;
        }

        return false;
    }

    private static void BringWindowToForeground(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (IsIconic(handle))
        {
            ShowWindow(handle, ShowWindowRestore);
        }

        SetForegroundWindow(handle);
        Thread.Sleep(200);
    }

    private static bool LooksLikePathOfExile(string title, string processName)
    {
        return title.Contains("Path of Exile", StringComparison.OrdinalIgnoreCase) ||
               processName.Contains("PathOfExile", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        GetWindowText(handle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string TryGetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private const int ShowWindowRestore = 9;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
