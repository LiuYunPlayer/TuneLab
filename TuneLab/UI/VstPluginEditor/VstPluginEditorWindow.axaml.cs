using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Runtime.InteropServices;
using TuneLab.GUI;
using TuneLab.PluginHost;
using TuneLab.Utils;

namespace TuneLab.UI;

/// <summary>
/// Window for hosting VST plugin editors.
/// Uses NativeControlHost (via VstPluginNativeHost) to properly embed the VST plugin's
/// native editor into the Avalonia window, avoiding the airspace rendering problem.
/// 
/// DPI Handling:
/// VST plugins report their editor size in physical pixels.
/// Avalonia layout uses logical (DPI-independent) pixels.
/// This class converts between the two coordinate systems using the window's DPI scaling factor.
/// </summary>
public partial class VstPluginEditorWindow : Window
{
    private const double TitleBarHeight = 32.0; // Logical pixels

    private readonly PluginInstance _pluginInstance;
    private VstPluginNativeHost? _nativeHost;
    private Border _pluginEditorHost = null!;
    private bool _isDisposed;

    /// <summary>
    /// Parameterless constructor required for XAML loading
    /// </summary>
    public VstPluginEditorWindow() : this(null!)
    {
    }

    /// <summary>
    /// Creates a new VST plugin editor window
    /// </summary>
    /// <param name="pluginInstance">The plugin instance to show the editor for</param>
    public VstPluginEditorWindow(PluginInstance pluginInstance)
    {
        _pluginInstance = pluginInstance ?? throw new ArgumentNullException(nameof(pluginInstance));
        
        AvaloniaXamlLoader.Load(this);
        
        Background = Style.BACK.ToBrush();
        
        // Get references
        _pluginEditorHost = this.FindControl<Border>("PluginEditorHost") 
            ?? throw new InvalidOperationException("PluginEditorHost not found");
        
        // Setup title bar
        var titleBar = this.FindControl<Grid>("TitleBar");
        if (titleBar != null)
        {
            titleBar.Background = Style.INTERFACE.ToBrush();
        }
        
        var titleText = this.FindControl<TextBlock>("TitleText");
        if (titleText != null)
        {
            titleText.Text = $"{_pluginInstance.Name} - Plugin Editor";
            titleText.Foreground = Style.LIGHT_WHITE.ToBrush();
        }
        
        // Update window title
        Title = $"{_pluginInstance.Name} - Plugin Editor";
        
        // Setup bypass button
        SetupButtons();
        
        // Subscribe to editor resize events
        _pluginInstance.EditorResized += OnPluginEditorResized;
        
        // Handle window closing
        Closing += OnWindowClosing;
    }

    /// <summary>
    /// Gets the DPI scaling factor for this window.
    /// Returns the ratio of physical pixels to logical pixels.
    /// e.g., 1.5 for 150% scaling, 2.0 for 200% scaling.
    /// </summary>
    private double GetDpiScale()
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen != null)
        {
            return screen.Scaling;
        }
        // Fallback: try VisualRoot rendering scaling
        if (VisualRoot is Visual visual)
        {
            var transform = visual.RenderTransform;
            // Default fallback
        }
        return 1.0;
    }

    /// <summary>
    /// Converts physical pixels to Avalonia logical pixels using the current DPI scale
    /// </summary>
    private double PhysicalToLogical(int physicalPixels)
    {
        var scale = GetDpiScale();
        return physicalPixels / scale;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Initialize the NativeControlHost after the Avalonia window is fully opened
        // (at this point we can get the correct DPI scaling)
        Dispatcher.UIThread.Post(() => InitializePluginEditor(), DispatcherPriority.Loaded);
    }

    private void SetupButtons()
    {
        var bypassBtn = this.FindControl<Avalonia.Controls.Button>("BypassButton");
        if (bypassBtn != null)
        {
            bypassBtn.Click += (s, e) =>
            {
                // Toggle bypass (can be implemented based on plugin capabilities)
            };
            bypassBtn.Background = Style.BUTTON_NORMAL.ToBrush();
            bypassBtn.Foreground = Style.LIGHT_WHITE.ToBrush();
        }
    }

    /// <summary>
    /// Initializes the plugin editor by creating a VstPluginNativeHost (NativeControlHost)
    /// and adding it to the visual tree. 
    /// 
    /// Key: The NativeControlHost size is set in Avalonia logical pixels.
    /// Avalonia then creates the native surface at the correct physical pixel size
    /// (logical * DPI scale), so the plugin editor fits perfectly.
    /// </summary>
    private void InitializePluginEditor()
    {
        if (_isDisposed || !_pluginInstance.HasEditor)
            return;
        
        try
        {
            // Get plugin editor size (in physical pixels)
            int editorPhysicalWidth = 800;
            int editorPhysicalHeight = 600;
            try
            {
                var (w, h) = _pluginInstance.GetEditorSize();
                if (w > 0 && h > 0)
                {
                    editorPhysicalWidth = w;
                    editorPhysicalHeight = h;
                }
            }
            catch
            {
                // Use default size
            }

            // Convert physical pixels to logical pixels for Avalonia layout
            double logicalWidth = PhysicalToLogical(editorPhysicalWidth);
            double logicalHeight = PhysicalToLogical(editorPhysicalHeight);

            System.Diagnostics.Debug.WriteLine(
                $"Plugin editor size: {editorPhysicalWidth}x{editorPhysicalHeight} physical, " +
                $"{logicalWidth:F1}x{logicalHeight:F1} logical (DPI scale: {GetDpiScale()})");

            // Create the NativeControlHost-based control with exact logical size
            _nativeHost = new VstPluginNativeHost(_pluginInstance)
            {
                Width = logicalWidth,
                Height = logicalHeight,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
            };

            // Add the NativeControlHost as the child of the Border
            _pluginEditorHost.Child = _nativeHost;

            // Resize the window to exactly fit the plugin editor + title bar
            // SizeToContent is set in XAML, but we also set explicit sizes as fallback
            Width = logicalWidth;
            Height = logicalHeight + TitleBarHeight;

            System.Diagnostics.Debug.WriteLine(
                $"Window size set to: {Width:F1}x{Height:F1} logical pixels");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize plugin editor: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles plugin editor resize events.
    /// Some plugins can resize their editor (e.g., resizable plugin UIs).
    /// The size reported by the plugin is in physical pixels.
    /// </summary>
    private void OnPluginEditorResized(object? sender, EditorResizedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed)
                return;

            // Convert physical pixels to logical pixels
            double logicalWidth = PhysicalToLogical(e.Width);
            double logicalHeight = PhysicalToLogical(e.Height);

            // Update NativeControlHost size (logical pixels)
            if (_nativeHost != null)
            {
                _nativeHost.Width = logicalWidth;
                _nativeHost.Height = logicalHeight;

                // Also resize the internal host window to match (physical pixels)
                _nativeHost.ResizeHostWindow(e.Width, e.Height);
            }
            
            // Update Avalonia window size (logical pixels)
            Width = logicalWidth;
            Height = logicalHeight + TitleBarHeight;
        });
    }

    /// <summary>
    /// Handles window closing
    /// </summary>
    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        CleanupPluginEditor();
    }

    /// <summary>
    /// Cleans up the plugin editor resources
    /// </summary>
    private void CleanupPluginEditor()
    {
        if (_isDisposed)
            return;
        
        _isDisposed = true;
        
        // Unsubscribe from events
        _pluginInstance.EditorResized -= OnPluginEditorResized;
        
        // Remove the NativeControlHost from the visual tree.
        // This triggers DestroyNativeControlCore which handles closing the editor
        // and destroying the native window.
        if (_nativeHost != null)
        {
            _pluginEditorHost.Child = null;
            _nativeHost = null;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        CleanupPluginEditor();
        base.OnClosed(e);
    }
}
