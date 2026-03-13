using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using System;
using System.Runtime.InteropServices;

namespace TuneLab.UI;

/// <summary>
/// Helper class for native window operations and handle management
/// </summary>
public static class NativeWindowHelper
{
    /// <summary>
    /// Gets the native window handle (HWND on Windows, NSView* on macOS, X11 Window on Linux)
    /// from an Avalonia Window
    /// </summary>
    /// <param name="window">The Avalonia window</param>
    /// <returns>The native window handle, or IntPtr.Zero if not available</returns>
    public static IntPtr GetWindowHandle(Window window)
    {
        if (window == null)
            return IntPtr.Zero;

        try
        {
            // Try to get handle via PlatformImpl
            var platformImpl = window.PlatformImpl;
            if (platformImpl == null)
                return IntPtr.Zero;

            // Avalonia 11.x approach
            var handleProp = platformImpl.GetType().GetProperty("Handle");
            if (handleProp != null)
            {
                var handle = handleProp.GetValue(platformImpl);
                if (handle != null)
                {
                    // IPlatformHandle.Handle property
                    var handleValueProp = handle.GetType().GetProperty("Handle");
                    if (handleValueProp != null)
                    {
                        var value = handleValueProp.GetValue(handle);
                        if (value is IntPtr ptr)
                            return ptr;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get window handle: {ex.Message}");
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Creates a native child window suitable for hosting a VST plugin editor
    /// </summary>
    /// <param name="parentHandle">The parent window handle</param>
    /// <param name="x">X position relative to parent</param>
    /// <param name="y">Y position relative to parent</param>
    /// <param name="width">Width of the child window</param>
    /// <param name="height">Height of the child window</param>
    /// <returns>Handle to the created child window, or IntPtr.Zero on failure</returns>
    public static IntPtr CreatePluginHostWindow(IntPtr parentHandle, int x, int y, int width, int height)
    {
        if (parentHandle == IntPtr.Zero)
            return IntPtr.Zero;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return CreateWindowsPluginHost(parentHandle, x, y, width, height);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return CreateMacOSPluginHost(parentHandle, x, y, width, height);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return CreateLinuxPluginHost(parentHandle, x, y, width, height);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Destroys a native plugin host window
    /// </summary>
    /// <param name="handle">The window handle to destroy</param>
    public static void DestroyPluginHostWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Win32Native.DestroyWindow(handle);
        }
        // macOS and Linux implementations would go here
    }

    /// <summary>
    /// Resizes a native plugin host window
    /// </summary>
    /// <param name="handle">The window handle</param>
    /// <param name="width">New width</param>
    /// <param name="height">New height</param>
    public static void ResizePluginHostWindow(IntPtr handle, int width, int height)
    {
        if (handle == IntPtr.Zero)
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Win32Native.SetWindowPos(
                handle,
                IntPtr.Zero,
                0, 0, width, height,
                Win32Native.SWP_NOMOVE | Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE
            );
        }
        // macOS and Linux implementations would go here
    }

    /// <summary>
    /// Moves a native plugin host window
    /// </summary>
    /// <param name="handle">The window handle</param>
    /// <param name="x">New X position</param>
    /// <param name="y">New Y position</param>
    public static void MovePluginHostWindow(IntPtr handle, int x, int y)
    {
        if (handle == IntPtr.Zero)
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Win32Native.SetWindowPos(
                handle,
                IntPtr.Zero,
                x, y, 0, 0,
                Win32Native.SWP_NOSIZE | Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE
            );
        }
        // macOS and Linux implementations would go here
    }

    /// <summary>
    /// Shows or hides a native plugin host window
    /// </summary>
    /// <param name="handle">The window handle</param>
    /// <param name="show">True to show, false to hide</param>
    public static void ShowPluginHostWindow(IntPtr handle, bool show)
    {
        if (handle == IntPtr.Zero)
            return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Win32Native.ShowWindow(handle, show ? Win32Native.SW_SHOW : Win32Native.SW_HIDE);
        }
        // macOS and Linux implementations would go here
    }

    /// <summary>
    /// Embeds a child window into a parent window (used for embedding VST plugin windows)
    /// </summary>
    /// <param name="childHandle">The child window to embed</param>
    /// <param name="newParentHandle">The new parent window</param>
    /// <returns>True if successful</returns>
    public static bool EmbedWindow(IntPtr childHandle, IntPtr newParentHandle)
    {
        if (childHandle == IntPtr.Zero || newParentHandle == IntPtr.Zero)
            return false;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Change the window style to be a child window
            uint style = Win32Native.GetWindowLong(childHandle, Win32Native.GWL_STYLE);
            style = (style & ~Win32Native.WS_POPUP) | Win32Native.WS_CHILD;
            Win32Native.SetWindowLong(childHandle, Win32Native.GWL_STYLE, style);

            // Set the parent
            var result = Win32Native.SetParent(childHandle, newParentHandle);
            return result != IntPtr.Zero;
        }

        return false;
    }

    #region Windows Implementation

    private static bool _windowClassRegistered = false;
    private const string PluginHostClassName = "TuneLabPluginHost";

    private static Win32Native.WndProcDelegate? _wndProcDelegate;
    
    private static void EnsureWindowClassRegistered()
    {
        if (_windowClassRegistered)
            return;

        // Keep a reference to prevent garbage collection
        _wndProcDelegate = new Win32Native.WndProcDelegate(Win32Native.DefWindowProc);
        
        var wndClass = new Win32Native.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<Win32Native.WNDCLASSEX>(),
            style = Win32Native.CS_HREDRAW | Win32Native.CS_VREDRAW,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = IntPtr.Zero,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = Win32Native.GetStockObject(Win32Native.BLACK_BRUSH),
            lpszMenuName = null,
            lpszClassName = PluginHostClassName,
            hIconSm = IntPtr.Zero
        };

        var atom = Win32Native.RegisterClassEx(ref wndClass);
        if (atom != 0)
        {
            _windowClassRegistered = true;
        }
        else
        {
            // If registration fails, we'll fall back to using "Static" class
            System.Diagnostics.Debug.WriteLine($"RegisterClassEx failed with error: {Marshal.GetLastWin32Error()}");
        }
    }

    private static IntPtr CreateWindowsPluginHost(IntPtr parentHandle, int x, int y, int width, int height)
    {
        // Ensure reasonable dimensions
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);

        // Try to register our custom window class
        EnsureWindowClassRegistered();

        string className = _windowClassRegistered ? PluginHostClassName : "Static";

        // Create a child window to host the plugin
        var hwnd = Win32Native.CreateWindowEx(
            0,  // No extended style
            className,  // Window class
            "",  // No window title
            Win32Native.WS_CHILD | Win32Native.WS_VISIBLE | Win32Native.WS_CLIPCHILDREN | Win32Native.WS_CLIPSIBLINGS,
            x, y, width, height,
            parentHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero
        );

        if (hwnd == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            System.Diagnostics.Debug.WriteLine($"CreateWindowEx failed with error: {error}");
        }

        return hwnd;
    }

    #endregion

    #region macOS Implementation (Placeholder)

    private static IntPtr CreateMacOSPluginHost(IntPtr parentHandle, int x, int y, int width, int height)
    {
        // macOS implementation would create an NSView
        // This requires Objective-C runtime interop
        System.Diagnostics.Debug.WriteLine("macOS plugin host creation not yet implemented");
        return IntPtr.Zero;
    }

    #endregion

    #region Linux Implementation (Placeholder)

    private static IntPtr CreateLinuxPluginHost(IntPtr parentHandle, int x, int y, int width, int height)
    {
        // Linux implementation would create an X11 window or Wayland surface
        // This requires X11/Wayland interop
        System.Diagnostics.Debug.WriteLine("Linux plugin host creation not yet implemented");
        return IntPtr.Zero;
    }

    #endregion
}

/// <summary>
/// Win32 native methods for window management
/// </summary>
internal static class Win32Native
{
    // Window Styles
    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_DISABLED = 0x08000000;
    public const uint WS_CLIPSIBLINGS = 0x04000000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_BORDER = 0x00800000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;

    // Extended Window Styles
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    // SetWindowPos flags
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOREDRAW = 0x0008;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;

    // GetWindowLong indices
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    // ShowWindow commands
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_SHOWNA = 8;

    // Window Class Style constants
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;

    // Stock objects
    public const int BLACK_BRUSH = 4;
    public const int WHITE_BRUSH = 0;
    public const int GRAY_BRUSH = 2;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, uint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
    }

    // WNDCLASSEX structure for registering window classes
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    // RegisterClassEx
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    // GetStockObject
    [DllImport("gdi32.dll")]
    public static extern IntPtr GetStockObject(int fnObject);

    // DefWindowProc
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    // We need a delegate for the window procedure
    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
