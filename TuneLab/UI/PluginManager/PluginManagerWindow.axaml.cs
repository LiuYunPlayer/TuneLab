using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Base.Utils;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.PluginHost;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.UI;

internal partial class PluginManagerWindow : Window
{
    private StackPanel mPluginListPanel = null!;
    private StackPanel mScanPathsPanel = null!;
    private readonly List<string> mScanPaths = new();
    
    public PluginManagerWindow()
    {
        AvaloniaXamlLoader.Load(this);
        
        Background = Style.BACK.ToBrush();
        
        // Get references
        mPluginListPanel = this.FindControl<StackPanel>("PluginListPanel") ?? throw new InvalidOperationException("PluginListPanel not found");
        mScanPathsPanel = this.FindControl<StackPanel>("ScanPathsPanel") ?? throw new InvalidOperationException("ScanPathsPanel not found");
        
        // Style the window
        var titleBar = this.FindControl<Grid>("TitleBar");
        if (titleBar != null)
        {
            titleBar.Background = Style.INTERFACE.ToBrush();
        }
        
        // Setup buttons
        SetupButtons();
        
        // Load current data
        LoadScanPaths();
        RefreshPluginList();
    }

    private void SetupButtons()
    {
        // Close button
        var closeBtn = this.FindControl<Avalonia.Controls.Button>("CloseButton");
        if (closeBtn != null)
        {
            closeBtn.Click += (s, e) => Close();
            closeBtn.Background = Style.BUTTON_NORMAL.ToBrush();
            closeBtn.Foreground = Style.LIGHT_WHITE.ToBrush();
        }
        
        // Rescan button
        var rescanBtn = this.FindControl<Avalonia.Controls.Button>("RescanButton");
        if (rescanBtn != null)
        {
            rescanBtn.Click += async (s, e) => await RescanPlugins();
            rescanBtn.Background = Style.BUTTON_PRIMARY.ToBrush();
            rescanBtn.Foreground = Colors.White.ToBrush();
        }
        
        // Add path button  
        var addPathBtn = this.FindControl<Avalonia.Controls.Button>("AddPathButton");
        if (addPathBtn != null)
        {
            addPathBtn.Click += async (s, e) => await AddScanPath();
            addPathBtn.Background = Style.BUTTON_NORMAL.ToBrush();
            addPathBtn.Foreground = Style.LIGHT_WHITE.ToBrush();
        }
    }

    private void LoadScanPaths()
    {
        mScanPaths.Clear();
        
        // Get default paths based on OS
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            var commonProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
            var commonProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86);
            
            if (System.IO.Directory.Exists(System.IO.Path.Combine(commonProgramFiles, "VST3")))
                mScanPaths.Add(System.IO.Path.Combine(commonProgramFiles, "VST3"));
            if (System.IO.Directory.Exists(System.IO.Path.Combine(commonProgramFilesX86, "VST3")))
                mScanPaths.Add(System.IO.Path.Combine(commonProgramFilesX86, "VST3"));
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            var macPaths = new[] { "/Library/Audio/Plug-Ins/VST3", "/Library/Audio/Plug-Ins/Components" };
            foreach (var path in macPaths)
            {
                if (System.IO.Directory.Exists(path))
                    mScanPaths.Add(path);
            }
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var linuxPaths = new[] { "/usr/lib/vst3", "/usr/local/lib/vst3", System.IO.Path.Combine(home, ".vst3") };
            foreach (var path in linuxPaths)
            {
                if (System.IO.Directory.Exists(path))
                    mScanPaths.Add(path);
            }
        }
        
        RefreshScanPathsList();
    }

    private void RefreshScanPathsList()
    {
        mScanPathsPanel.Children.Clear();
        
        foreach (var path in mScanPaths)
        {
            var pathPanel = new DockPanel { Margin = new Thickness(0, 2) };
            
            // Remove button
            var removeBtn = new Avalonia.Controls.Button
            {
                Content = "×",
                Width = 20,
                Height = 20,
                FontSize = 14,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Style.LIGHT_WHITE.ToBrush()
            };
            var capturedPath = path;
            removeBtn.Click += (s, e) =>
            {
                mScanPaths.Remove(capturedPath);
                RefreshScanPathsList();
            };
            DockPanel.SetDock(removeBtn, Dock.Right);
            pathPanel.Children.Add(removeBtn);
            
            // Path text
            var pathText = new TextBlock
            {
                Text = path,
                FontSize = 10,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Style.LIGHT_WHITE.ToBrush()
            };
            pathText.SetupToolTip(path);
            pathPanel.Children.Add(pathText);
            
            mScanPathsPanel.Children.Add(pathPanel);
        }
        
        if (mScanPaths.Count == 0)
        {
            mScanPathsPanel.Children.Add(new TextBlock
            {
                Text = "No scan paths".Tr(TC.Property),
                FontSize = 11,
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                FontStyle = FontStyle.Italic
            });
        }
    }

    private void RefreshPluginList()
    {
        mPluginListPanel.Children.Clear();
        
        var manager = PluginHostManager.Instance;
        if (!manager.IsInitialized)
        {
            mPluginListPanel.Children.Add(new TextBlock
            {
                Text = "Plugin host not initialized".Tr(TC.Property),
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                FontStyle = FontStyle.Italic,
                Margin = new Thickness(8)
            });
            return;
        }
        
        var plugins = manager.GetAllPlugins().ToList();
        
        if (plugins.Count == 0)
        {
            mPluginListPanel.Children.Add(new TextBlock
            {
                Text = "No plugins found. Click 'Rescan Plugins' to scan.".Tr(TC.Property),
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                FontStyle = FontStyle.Italic,
                Margin = new Thickness(8),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        
        // Header
        var header = new TextBlock
        {
            Text = $"{plugins.Count} plugins found".Tr(TC.Property),
            FontSize = 11,
            Foreground = Style.LIGHT_WHITE.Opacity(0.7).ToBrush(),
            Margin = new Thickness(4, 0, 4, 8)
        };
        mPluginListPanel.Children.Add(header);
        
        // Plugin items
        foreach (var plugin in plugins.OrderBy(p => p.Name))
        {
            var item = CreatePluginItem(plugin);
            mPluginListPanel.Children.Add(item);
        }
    }

    private Control CreatePluginItem(PluginInfo plugin)
    {
        var border = new Border
        {
            Background = Style.INTERFACE.ToBrush(),
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 2),
            Padding = new Thickness(8, 6)
        };
        
        var stack = new StackPanel();
        
        // Plugin name and type
        var namePanel = new DockPanel();
        var nameText = new TextBlock
        {
            Text = plugin.Name,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Style.LIGHT_WHITE.ToBrush()
        };
        var typeText = new TextBlock
        {
            Text = plugin.Type.ToString(),
            FontSize = 10,
            Foreground = Style.HIGH_LIGHT.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        DockPanel.SetDock(typeText, Dock.Right);
        namePanel.Children.Add(typeText);
        namePanel.Children.Add(nameText);
        stack.Children.Add(namePanel);
        
        // Vendor
        var vendorText = new TextBlock
        {
            Text = plugin.Vendor,
            FontSize = 10,
            Foreground = Style.LIGHT_WHITE.Opacity(0.7).ToBrush(),
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Children.Add(vendorText);
        
        // Category and details
        var categoryStr = plugin.Category == PluginCategory.Instrument || plugin.IsSynth ? "Instrument" : "Effect";
        var detailsText = new TextBlock
        {
            Text = $"{plugin.Category} | {categoryStr} | In: {plugin.NumInputChannels} Out: {plugin.NumOutputChannels}",
            FontSize = 9,
            Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
            Margin = new Thickness(0, 2, 0, 0)
        };
        stack.Children.Add(detailsText);
        
        // File path (smaller)
        var pathText = new TextBlock
        {
            Text = plugin.FilePath,
            FontSize = 9,
            Foreground = Style.LIGHT_WHITE.Opacity(0.4).ToBrush(),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0)
        };
        pathText.SetupToolTip(plugin.FilePath);
        stack.Children.Add(pathText);
        
        border.Child = stack;
        return border;
    }

    private async System.Threading.Tasks.Task RescanPlugins()
    {
        var manager = PluginHostManager.Instance;
        if (!manager.IsInitialized)
        {
            await this.ShowMessage("Error".Tr(TC.Dialog), "Plugin host is not initialized.".Tr(TC.Property));
            return;
        }
        
        if (mScanPaths.Count == 0)
        {
            await this.ShowMessage("Error".Tr(TC.Dialog), "No scan paths configured. Please add at least one scan path.".Tr(TC.Property));
            return;
        }
        
        // Clear and re-add scan paths
        try
        {
            manager.ClearScanPaths();
            foreach (var path in mScanPaths)
            {
                try
                {
                    if (System.IO.Directory.Exists(path))
                    {
                        manager.AddScanPath(path);
                        Log.Info($"Added scan path: {path}");
                    }
                    else
                    {
                        Log.Warning($"Scan path does not exist: {path}");
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Failed to add scan path {path}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            await this.ShowMessage("Error".Tr(TC.Dialog), $"Failed to configure scan paths:\n{ex.Message}");
            return;
        }
        
        // Show progress
        var progressDialog = new Dialog();
        progressDialog.SetTitle("Scanning".Tr(TC.Property));
        progressDialog.SetMessage("Scanning for plugins...".Tr(TC.Property));
        var stopButton = progressDialog.AddButton("Stop".Tr(TC.Dialog), Dialog.ButtonType.Normal);
        
        var scanStopped = false;
        stopButton.Clicked += () =>
        {
            scanStopped = true;
            try { manager.StopScan(); } catch { }
            progressDialog.Close();
        };
        
        // Keep progress handler reference to prevent GC
        EventHandler<ScanProgressEventArgs>? progressHandler = null;
        progressHandler = (sender, args) =>
        {
            try
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (!scanStopped)
                        {
                            var fileName = string.IsNullOrEmpty(args.CurrentPath) ? "" : System.IO.Path.GetFileName(args.CurrentPath);
                            progressDialog.SetMessage($"Scanning: {fileName}\n{args.PluginsFound} plugins found");
                        }
                    }
                    catch { }
                });
            }
            catch { }
        };
        
        manager.ScanProgress += progressHandler;
        
        _ = progressDialog.ShowDialog(this);
        
        try
        {
            Log.Info("Starting plugin scan...");
            await manager.ScanPluginsAsync();
            Log.Info($"Scan complete. Found {manager.PluginCount} plugins.");
        }
        catch (System.Runtime.InteropServices.SEHException ex)
        {
            Log.Error($"Plugin scan crashed (native exception): {ex}");
            if (!scanStopped)
            {
                await this.ShowMessage("Scan Error".Tr(TC.Property),
                    "Plugin scan crashed due to a native library error. Some plugins may be incompatible.".Tr(TC.Property));
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Scan failed: {ex}");
            if (!scanStopped)
            {
                await this.ShowMessage("Scan Error".Tr(TC.Property),
                    $"Plugin scan failed:\n{ex.Message}");
            }
        }
        finally
        {
            try { manager.ScanProgress -= progressHandler; } catch { }
            try { progressDialog.Close(); } catch { }
            RefreshPluginList();
        }
    }

    private async System.Threading.Tasks.Task AddScanPath()
    {
        var result = await this.OpenFolder(new FolderPickerOpenOptions
        {
            Title = "Select Plugin Folder".Tr(TC.Property),
            AllowMultiple = false
        });
        
        if (!string.IsNullOrEmpty(result))
        {
            if (!mScanPaths.Contains(result))
            {
                mScanPaths.Add(result);
                RefreshScanPathsList();
            }
        }
    }
}
