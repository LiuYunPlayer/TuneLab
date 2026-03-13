using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Runtime.InteropServices;
using TuneLab.PluginHost;

namespace TuneLab.UI;

/// <summary>
/// A NativeControlHost-based control that properly embeds a VST plugin editor
/// into an Avalonia window. This avoids the "airspace" problem where Avalonia's
/// composited rendering surface would otherwise cover the native plugin window.
/// 
/// The NativeControlHost manages the airspace by creating a hole in the composited
/// surface. The child window fills the entire parent area provided by NativeControlHost,
/// and the plugin editor renders into that child window at the correct physical pixel size.
/// </summary>
public class VstPluginNativeHost : NativeControlHost
{
    private readonly PluginInstance _pluginInstance;
    private IntPtr _hostWindowHandle = IntPtr.Zero;
    private IntPtr _pluginEditorHandle = IntPtr.Zero;
    private bool _isDestroyed;

    public VstPluginNativeHost(PluginInstance pluginInstance)
    {
        _pluginInstance = pluginInstance ?? throw new ArgumentNullException(nameof(pluginInstance));
    }

    /// <summary>
    /// The native handle of the plugin editor window, if open
    /// </summary>
    public IntPtr PluginEditorHandle => _pluginEditorHandle;

    /// <summary>
    /// The native host window handle
    /// </summary>
    public IntPtr HostWindowHandle => _hostWindowHandle;

    /// <summary>
    /// Creates the native control that will host the VST plugin editor.
    /// Called by Avalonia when the NativeControlHost is attached to the visual tree.
    /// The parent HWND is sized by Avalonia's layout system (already DPI-scaled to physical pixels).
    /// </summary>
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (_isDestroyed || !_pluginInstance.HasEditor)
        {
            return base.CreateNativeControlCore(parent);
        }

        var parentHandle = parent.Handle;
        if (parentHandle == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine("VstPluginNativeHost: parent handle is zero");
            return base.CreateNativeControlCore(parent);
        }

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Get the actual physical pixel size of the parent window provided by NativeControlHost.
                // This is already correctly DPI-scaled by Avalonia's layout engine.
                int hostWidth, hostHeight;
                if (Win32Native.GetClientRect(parentHandle, out var parentRect))
                {
                    hostWidth = Math.Max(parentRect.Width, 1);
                    hostHeight = Math.Max(parentRect.Height, 1);
                }
                else
                {
                    // Fallback: use plugin's reported size as physical pixels
                    try
                    {
                        var (w, h) = _pluginInstance.GetEditorSize();
                        hostWidth = Math.Max(w, 1);
                        hostHeight = Math.Max(h, 1);
                    }
                    catch
                    {
                        hostWidth = 800;
                        hostHeight = 600;
                    }
                }

                // Create a child window that fills the entire parent area.
                // This child window serves as the direct parent for the VST plugin editor.
                _hostWindowHandle = CreateWindowsHostWindow(parentHandle, hostWidth, hostHeight);
                if (_hostWindowHandle == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("VstPluginNativeHost: Failed to create host window");
                    return base.CreateNativeControlCore(parent);
                }

                // Open the plugin editor with our host window as parent
                _pluginEditorHandle = _pluginInstance.OpenEditor(_hostWindowHandle);

                if (_pluginEditorHandle != IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine($"VstPluginNativeHost: Plugin editor opened: {_pluginEditorHandle}");
                }
                else
                {
                    // Some plugins render directly into the parent - this is OK
                    System.Diagnostics.Debug.WriteLine("VstPluginNativeHost: OpenEditor returned zero (plugin may render into host directly)");
                }

                return new PlatformHandle(_hostWindowHandle, "HWND");
            }
            else
            {
                return base.CreateNativeControlCore(parent);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VstPluginNativeHost: Failed to create native control: {ex.Message}");
            return base.CreateNativeControlCore(parent);
        }
    }

    /// <summary>
    /// Destroys the native control when the NativeControlHost is removed from the visual tree.
    /// </summary>
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _isDestroyed = true;

        // Close the plugin editor first
        try
        {
            _pluginInstance.CloseEditor();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"VstPluginNativeHost: Error closing editor: {ex.Message}");
        }

        _pluginEditorHandle = IntPtr.Zero;

        // Destroy the host window
        if (_hostWindowHandle != IntPtr.Zero && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Win32Native.DestroyWindow(_hostWindowHandle);
            _hostWindowHandle = IntPtr.Zero;
        }

        base.DestroyNativeControlCore(control);
    }

    /// <summary>
    /// Resizes the host window to match the NativeControlHost's current physical pixel size.
    /// Called when the plugin reports a size change.
    /// </summary>
    public void ResizeHostWindow(int physicalWidth, int physicalHeight)
    {
        if (_hostWindowHandle == IntPtr.Zero)
            return;

        Win32Native.SetWindowPos(
            _hostWindowHandle,
            IntPtr.Zero,
            0, 0, physicalWidth, physicalHeight,
            Win32Native.SWP_NOMOVE | Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE
        );
    }

    #region Windows Host Window Creation

    private static bool _windowClassRegistered = false;
    private const string HostClassName = "TuneLabVstEditorHost";
    private static Win32Native.WndProcDelegate? _wndProcDelegate;

    private static void EnsureWindowClassRegistered()
    {
        if (_windowClassRegistered)
            return;

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
            hbrBackground = IntPtr.Zero, // No background brush - let plugin paint
            lpszMenuName = null,
            lpszClassName = HostClassName,
            hIconSm = IntPtr.Zero
        };

        var atom = Win32Native.RegisterClassEx(ref wndClass);
        if (atom != 0)
        {
            _windowClassRegistered = true;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"VstPluginNativeHost: RegisterClassEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private static IntPtr CreateWindowsHostWindow(IntPtr parentHandle, int width, int height)
    {
        width = Math.Max(width, 1);
        height = Math.Max(height, 1);

        EnsureWindowClassRegistered();

        string className = _windowClassRegistered ? HostClassName : "Static";

        var hwnd = Win32Native.CreateWindowEx(
            0,
            className,
            "",
            Win32Native.WS_CHILD | Win32Native.WS_VISIBLE | Win32Native.WS_CLIPCHILDREN | Win32Native.WS_CLIPSIBLINGS,
            0, 0, width, height,
            parentHandle,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero
        );

        if (hwnd == IntPtr.Zero)
        {
            System.Diagnostics.Debug.WriteLine($"VstPluginNativeHost: CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }

        return hwnd;
    }

    #endregion

    /// <summary>
    /// Simple IPlatformHandle implementation for returning our HWND
    /// </summary>
    private class PlatformHandle : IPlatformHandle
    {
        public PlatformHandle(IntPtr handle, string descriptor)
        {
            Handle = handle;
            HandleDescriptor = descriptor;
        }

        public IntPtr Handle { get; }
        public string HandleDescriptor { get; }
    }
}
