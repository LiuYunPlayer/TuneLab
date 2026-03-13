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
/// </summary>
public class VstPluginNativeHost : NativeControlHost
{
    private readonly PluginInstance _pluginInstance;
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
    /// Creates the native control that will host the VST plugin editor.
    /// Called by Avalonia when the NativeControlHost is attached to the visual tree.
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
            // Get editor size
            int editorWidth = 800;
            int editorHeight = 600;
            try
            {
                var (w, h) = _pluginInstance.GetEditorSize();
                if (w > 0 && h > 0)
                {
                    editorWidth = w;
                    editorHeight = h;
                }
            }
            catch
            {
                // Use default size
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Create our host child window within the NativeControlHost-provided parent
                var hostHandle = CreateWindowsHostWindow(parentHandle, editorWidth, editorHeight);
                if (hostHandle == IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine("VstPluginNativeHost: Failed to create host window");
                    return base.CreateNativeControlCore(parent);
                }

                // Open the plugin editor with our host window as parent
                _pluginEditorHandle = _pluginInstance.OpenEditor(hostHandle);

                if (_pluginEditorHandle != IntPtr.Zero)
                {
                    System.Diagnostics.Debug.WriteLine($"VstPluginNativeHost: Plugin editor opened successfully: {_pluginEditorHandle}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("VstPluginNativeHost: OpenEditor returned zero, plugin may render into host directly");
                }

                return new PlatformHandle(hostHandle, "HWND");
            }
            else
            {
                // For macOS/Linux, fall back to base implementation for now
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
        if (control.Handle != IntPtr.Zero && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Win32Native.DestroyWindow(control.Handle);
        }

        base.DestroyNativeControlCore(control);
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
