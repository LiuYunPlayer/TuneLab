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
/// </summary>
public partial class VstPluginEditorWindow : Window
{
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
        
        // Set initial size based on plugin editor size
        try
        {
            var (width, height) = _pluginInstance.GetEditorSize();
            if (width > 0 && height > 0)
            {
                Width = width;
                Height = height + 32; // Add title bar height
                MinWidth = Math.Max(200, width / 2);
                MinHeight = Math.Max(150, height / 2 + 32);
            }
        }
        catch
        {
            // Use default size if we can't get editor size
        }
        
        // Subscribe to editor resize events
        _pluginInstance.EditorResized += OnPluginEditorResized;
        
        // Handle window closing
        Closing += OnWindowClosing;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        
        // Initialize the NativeControlHost after the Avalonia window is fully opened
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
    /// and adding it to the visual tree. The NativeControlHost handles the airspace problem
    /// by creating a proper hole in Avalonia's composited rendering surface.
    /// </summary>
    private void InitializePluginEditor()
    {
        if (_isDisposed || !_pluginInstance.HasEditor)
            return;
        
        try
        {
            // Get editor size for sizing the NativeControlHost
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

            // Create the NativeControlHost-based control
            _nativeHost = new VstPluginNativeHost(_pluginInstance)
            {
                Width = editorWidth,
                Height = editorHeight,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
            };

            // Add the NativeControlHost as the child of the Border in XAML
            _pluginEditorHost.Child = _nativeHost;

            System.Diagnostics.Debug.WriteLine($"VstPluginNativeHost added to visual tree ({editorWidth}x{editorHeight})");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to initialize plugin editor: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles plugin editor resize events
    /// </summary>
    private void OnPluginEditorResized(object? sender, EditorResizedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_isDisposed)
                return;
            
            // Update NativeControlHost size
            if (_nativeHost != null)
            {
                _nativeHost.Width = e.Width;
                _nativeHost.Height = e.Height;
            }
            
            // Update Avalonia window size
            Width = e.Width;
            Height = e.Height + 32; // Add title bar height
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
