using Avalonia.Controls;
using System;
using System.Collections.Generic;
using TuneLab.PluginHost;

namespace TuneLab.UI;

/// <summary>
/// Extension methods for showing VST plugin editor windows
/// </summary>
public static class PluginEditorExtensions
{
    // Track open editor windows to prevent opening multiple editors for the same plugin
    private static readonly Dictionary<IntPtr, VstPluginEditorWindow> _openEditors = new();

    /// <summary>
    /// Shows the plugin editor window for the given plugin instance.
    /// If an editor window is already open for this plugin, it will be activated instead of creating a new one.
    /// </summary>
    /// <param name="pluginInstance">The plugin instance to show the editor for</param>
    /// <param name="ownerWindow">Optional owner window for the editor</param>
    /// <returns>The editor window, or null if the plugin has no editor</returns>
    public static VstPluginEditorWindow? ShowEditorWindow(this PluginInstance pluginInstance, Window? ownerWindow = null)
    {
        if (pluginInstance == null)
            throw new ArgumentNullException(nameof(pluginInstance));

        if (!pluginInstance.HasEditor)
            return null;

        // Check if an editor is already open for this plugin
        if (_openEditors.TryGetValue(pluginInstance.Handle, out var existingWindow))
        {
            // Activate the existing window
            existingWindow.Activate();
            return existingWindow;
        }

        // Create a new editor window
        var editorWindow = new VstPluginEditorWindow(pluginInstance);

        // Track this window
        _openEditors[pluginInstance.Handle] = editorWindow;

        // Remove from tracking when closed
        editorWindow.Closed += (s, e) =>
        {
            _openEditors.Remove(pluginInstance.Handle);
        };

        // Show the window
        if (ownerWindow != null)
        {
            editorWindow.Show(ownerWindow);
        }
        else
        {
            editorWindow.Show();
        }

        return editorWindow;
    }

    /// <summary>
    /// Closes the editor window for the given plugin instance if one is open
    /// </summary>
    /// <param name="pluginInstance">The plugin instance</param>
    public static void CloseEditorWindow(this PluginInstance pluginInstance)
    {
        if (pluginInstance == null)
            return;

        if (_openEditors.TryGetValue(pluginInstance.Handle, out var window))
        {
            window.Close();
            // Note: the Closed event handler will remove it from _openEditors
        }
    }

    /// <summary>
    /// Checks if an editor window is currently open for the given plugin instance
    /// </summary>
    /// <param name="pluginInstance">The plugin instance</param>
    /// <returns>True if an editor window is open</returns>
    public static bool IsEditorWindowOpen(this PluginInstance pluginInstance)
    {
        if (pluginInstance == null)
            return false;

        return _openEditors.ContainsKey(pluginInstance.Handle);
    }

    /// <summary>
    /// Gets the editor window for the given plugin instance if one is open
    /// </summary>
    /// <param name="pluginInstance">The plugin instance</param>
    /// <returns>The editor window, or null if none is open</returns>
    public static VstPluginEditorWindow? GetEditorWindow(this PluginInstance pluginInstance)
    {
        if (pluginInstance == null)
            return null;

        _openEditors.TryGetValue(pluginInstance.Handle, out var window);
        return window;
    }

    /// <summary>
    /// Closes all open plugin editor windows
    /// </summary>
    public static void CloseAllEditorWindows()
    {
        // Create a copy of the values to avoid modification during iteration
        var windows = new List<VstPluginEditorWindow>(_openEditors.Values);
        foreach (var window in windows)
        {
            window.Close();
        }
    }
}
