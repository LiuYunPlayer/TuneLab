using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Linq;
using TuneLab.Base.Event;
using TuneLab.Base.Utils;
using TuneLab.Data;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.PluginHost;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;

namespace TuneLab.UI;

/// <summary>
/// Controller for displaying and managing track plugin effects in the sidebar
/// </summary>
internal class TrackPluginController : StackPanel
{
    public ITrack? Track
    {
        get => mTrack;
        set
        {
            if (mTrack == value)
                return;
                
            s.DisposeAll();
            mTrack = value;
            Refresh();
            
            if (mTrack != null)
            {
                mTrack.Plugins.ItemAdded.Subscribe(_ => Refresh(), s);
                mTrack.Plugins.ItemRemoved.Subscribe(_ => Refresh(), s);
            }
        }
    }

    public TrackPluginController()
    {
        Orientation = Avalonia.Layout.Orientation.Vertical;
        Background = Style.INTERFACE.ToBrush();
    }

    private void Refresh()
    {
        Children.Clear();
        
        if (mTrack == null)
            return;

        // Add header with title and manage button
        var headerPanel = new DockPanel { Margin = new Thickness(12, 8, 12, 4) };
        
        // Manage button (right side)
        var manageBtn = new Button() { Width = 60, Height = 20 };
        manageBtn.AddContent(new ButtonContent
        {
            Item = new BorderItem { CornerRadius = 3 },
            ColorSet = new ColorSet { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER }
        });
        manageBtn.AddContent(new ButtonContent
        {
            Item = new TextItem { Text = "Manage".Tr(TC.Property), FontSize = 10 },
            ColorSet = new ColorSet { Color = Style.LIGHT_WHITE }
        });
        manageBtn.Clicked += () => OpenPluginManager();
        DockPanel.SetDock(manageBtn, Dock.Right);
        headerPanel.Children.Add(manageBtn);
        
        // Title (left side)
        var titleLabel = new TextBlock
        {
            Text = "Plugin Slots".Tr(TC.Property),
            FontSize = 12,
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        headerPanel.Children.Add(titleLabel);
        
        Children.Add(headerPanel);
        Children.Add(new Border { Height = 1, Background = Style.BACK.ToBrush(), Margin = new Thickness(12, 4, 12, 4) });

        // Add each plugin item
        foreach (var plugin in mTrack.Plugins)
        {
            Children.Add(new TrackPluginItem(plugin, mTrack));
            Children.Add(new Border { Height = 1, Background = Style.BACK.ToBrush() });
        }

        // Add "Add Plugin" button
        var addButton = new Button() { Width = 200, Height = 32, Margin = new Thickness(24, 8, 24, 8) };
        addButton.AddContent(new ButtonContent
        {
            Item = new BorderItem { CornerRadius = 4 },
            ColorSet = new ColorSet { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER }
        });
        addButton.AddContent(new ButtonContent
        {
            Item = new TextItem { Text = "+ " + "Add Plugin".Tr(TC.Property), FontSize = 12 },
            ColorSet = new ColorSet { Color = Style.LIGHT_WHITE }
        });
        addButton.Clicked += async () => await ShowAddPluginDialog();
        Children.Add(addButton);
    }

    private void OpenPluginManager()
    {
        var window = new PluginManagerWindow();
        var parentWindow = this.Window();
        if (parentWindow != null)
        {
            window.ShowDialog(parentWindow);
        }
        else
        {
            window.Show();
        }
    }

    private async System.Threading.Tasks.Task ShowAddPluginDialog()
    {
        if (mTrack == null)
            return;

        var manager = PluginHostManager.Instance;
        if (!manager.IsInitialized)
            return;

        // Get available plugins
        var plugins = manager.GetAllPlugins().ToList();
        if (plugins.Count == 0)
        {
            // Offer to scan for plugins
            var shouldScan = await ShowScanConfirmDialog();
            if (shouldScan)
            {
                await ScanPluginsAsync();
                plugins = manager.GetAllPlugins().ToList();
                if (plugins.Count == 0)
                {
                    await this.ShowMessage("No Plugins".Tr(TC.Property), "No plugins found after scan.".Tr(TC.Property));
                    return;
                }
            }
            else
            {
                return;
            }
        }

        // Create a simple selection dialog
        var window = new Window
        {
            Title = "Select Plugin".Tr(TC.Property),
            Width = 400,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Style.BACK.ToBrush()
        };

        var listBox = new ListBox
        {
            Background = Style.INTERFACE.ToBrush(),
            Foreground = Style.LIGHT_WHITE.ToBrush()
        };

        foreach (var plugin in plugins)
        {
            var item = new ListBoxItem
            {
                Content = $"{plugin.Name} ({plugin.Vendor})",
                Tag = plugin,
                Foreground = Style.LIGHT_WHITE.ToBrush()
            };
            listBox.Items.Add(item);
        }

        var okButton = new Button() { Width = 80, Height = 32, Margin = new Thickness(8) };
        okButton.AddContent(new ButtonContent
        {
            Item = new BorderItem { CornerRadius = 4 },
            ColorSet = new ColorSet { Color = Style.BUTTON_PRIMARY, HoveredColor = Style.BUTTON_PRIMARY_HOVER }
        });
        okButton.AddContent(new ButtonContent
        {
            Item = new TextItem { Text = "OK", FontSize = 12 },
            ColorSet = new ColorSet { Color = Style.TEXT_LIGHT }
        });

        var cancelButton = new Button() { Width = 80, Height = 32, Margin = new Thickness(8) };
        cancelButton.AddContent(new ButtonContent
        {
            Item = new BorderItem { CornerRadius = 4 },
            ColorSet = new ColorSet { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER }
        });
        cancelButton.AddContent(new ButtonContent
        {
            Item = new TextItem { Text = "Cancel".Tr(TC.Dialog), FontSize = 12 },
            ColorSet = new ColorSet { Color = Style.TEXT_LIGHT }
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Margin = new Thickness(8)
        };
        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(okButton);

        var mainPanel = new DockPanel();
        DockPanel.SetDock(buttonPanel, Dock.Bottom);
        mainPanel.Children.Add(buttonPanel);
        mainPanel.Children.Add(new ScrollViewer { Content = listBox });

        window.Content = mainPanel;

        PluginInfo? selectedPlugin = null;

        okButton.Clicked += () =>
        {
            if (listBox.SelectedItem is ListBoxItem item && item.Tag is PluginInfo info)
            {
                selectedPlugin = info;
            }
            window.Close();
        };

        cancelButton.Clicked += () => window.Close();

        listBox.DoubleTapped += (sender, e) =>
        {
            if (listBox.SelectedItem is ListBoxItem item && item.Tag is PluginInfo info)
            {
                selectedPlugin = info;
                window.Close();
            }
        };

        // Find parent window
        var parentWindow = this.Window();
        await window.ShowDialog(parentWindow);

        if (selectedPlugin != null)
        {
            // Add the plugin to the track
            var pluginInfo = new TrackPluginInfo
            {
                PluginUid = selectedPlugin.Value.Uid,
                PluginPath = selectedPlugin.Value.FilePath,
                Name = selectedPlugin.Value.Name
            };
            mTrack.AddPlugin(pluginInfo);
            if (mTrack is Track track)
            {
                track.Commit();
            }
        }
    }

    private async System.Threading.Tasks.Task<bool> ShowScanConfirmDialog()
    {
        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        
        var dialog = new Dialog();
        dialog.SetTitle("No Plugins".Tr(TC.Property));
        dialog.SetMessage("No plugins found. Would you like to scan for plugins now?".Tr(TC.Property));
        
        // Dialog.AddButton returns TuneLab.GUI.Components.Button which uses Clicked action
        var scanButton = dialog.AddButton("Scan".Tr(TC.Property), Dialog.ButtonType.Primary);
        var cancelButton = dialog.AddButton("Cancel".Tr(TC.Dialog), Dialog.ButtonType.Normal);
        
        // Subscribe to Clicked before the default Close handler
        scanButton.Clicked += () => tcs.TrySetResult(true);
        cancelButton.Clicked += () => tcs.TrySetResult(false);
        dialog.Closed += (_, _) => tcs.TrySetResult(false);
        
        dialog.ShowDialog(this.Window());
        return await tcs.Task;
    }

    private async System.Threading.Tasks.Task ScanPluginsAsync()
    {
        var manager = PluginHostManager.Instance;
        if (!manager.IsInitialized)
        {
            await this.ShowMessage("Plugin Host Error".Tr(TC.Property),
                "Plugin host is not initialized. The native library may not be loaded.".Tr(TC.Property));
            return;
        }
            
        // Show a scanning progress indicator
        var progressDialog = new Dialog();
        progressDialog.SetTitle("Scanning Plugins".Tr(TC.Property));
        progressDialog.SetMessage("Scanning for plugins, please wait...".Tr(TC.Property));
        
        var stopButton = progressDialog.AddButton("Stop".Tr(TC.Dialog), Dialog.ButtonType.Normal);
        
        var scanCancelled = false;
        stopButton.Clicked += () => { scanCancelled = true; manager.StopScan(); };
        
        // Subscribe to scan progress
        EventHandler<ScanProgressEventArgs>? progressHandler = null;
        progressHandler = (sender, args) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                progressDialog.SetMessage($"Scanning: {args.CurrentPath}\n({args.PluginsFound} plugins found, {args.TotalScanned} scanned)");
            });
        };
        manager.ScanProgress += progressHandler;
        
        // Run scan asynchronously
        _ = progressDialog.ShowDialog(this.Window());
        
        try
        {
            TuneLab.Base.Utils.Log.Info("Starting plugin scan...");
            await manager.ScanPluginsAsync();
            TuneLab.Base.Utils.Log.Info($"Plugin scan complete. Found {manager.PluginCount} plugins.");
        }
        catch (Exception ex)
        {
            // Scan was stopped or failed
            TuneLab.Base.Utils.Log.Error($"Plugin scan failed: {ex}");
            await this.ShowMessage("Scan Failed".Tr(TC.Property),
                $"Plugin scan failed:\n{ex.Message}".Tr(TC.Property));
        }
        finally
        {
            manager.ScanProgress -= progressHandler;
            progressDialog.Close();
        }
    }

    private ITrack? mTrack;
    private readonly DisposableManager s = new();
}

/// <summary>
/// Individual plugin item in the list
/// </summary>
internal class TrackPluginItem : DockPanel
{
    public TrackPluginItem(ITrackPlugin plugin, ITrack track)
    {
        mPlugin = plugin;
        mTrack = track;
        
        Height = 40;
        Background = Style.INTERFACE.ToBrush();
        Margin = new Thickness(12, 4);

        // Bypass toggle button
        var bypassBtn = new Button() { Width = 24, Height = 24, Margin = new Thickness(4) };
        var bypassBorderContent = new ButtonContent
        {
            Item = new BorderItem { CornerRadius = 4 },
            ColorSet = new ColorSet 
            { 
                Color = plugin.Bypassed.Value ? Style.HIGH_LIGHT : Style.BUTTON_NORMAL, 
                HoveredColor = plugin.Bypassed.Value ? Style.HIGH_LIGHT.Brighter() : Style.BUTTON_NORMAL_HOVER 
            }
        };
        bypassBtn.AddContent(bypassBorderContent);
        bypassBtn.AddContent(new ButtonContent
        {
            Item = new TextItem { Text = "B", FontSize = 10 },
            ColorSet = new ColorSet { Color = Style.LIGHT_WHITE }
        });
        bypassBtn.SetupToolTip("Bypass".Tr(TC.Property));
        bypassBtn.Clicked += () =>
        {
            plugin.Bypassed.Set(!plugin.Bypassed.Value);
            if (track is Track t) t.Commit();
        };
        plugin.Bypassed.Modified.Subscribe(() =>
        {
            bypassBorderContent.ColorSet = new ColorSet 
            { 
                Color = plugin.Bypassed.Value ? Style.HIGH_LIGHT : Style.BUTTON_NORMAL, 
                HoveredColor = plugin.Bypassed.Value ? Style.HIGH_LIGHT.Brighter() : Style.BUTTON_NORMAL_HOVER 
            };
        }, mDisposable);
        this.AddDock(bypassBtn, Dock.Left);

        // Plugin name label
        var nameLabel = new Label
        {
            Content = string.IsNullOrEmpty(plugin.Name.Value) ? "(Empty)".Tr(TC.Property) : plugin.Name.Value,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            FontSize = 12,
            Margin = new Thickness(8, 0)
        };
        plugin.Name.Modified.Subscribe(() => 
            nameLabel.Content = string.IsNullOrEmpty(plugin.Name.Value) ? "(Empty)".Tr(TC.Property) : plugin.Name.Value, mDisposable);
        this.AddDock(nameLabel);

        // Remove button
        var removeBtn = new Button() { Width = 24, Height = 24, Margin = new Thickness(4) };
        removeBtn.AddContent(new ButtonContent
        {
            Item = new TextItem { Text = "×", FontSize = 16 },
            ColorSet = new ColorSet { Color = Style.LIGHT_WHITE, HoveredColor = Style.TEXT_LIGHT }
        });
        removeBtn.SetupToolTip("Remove".Tr(TC.Property));
        removeBtn.Clicked += () =>
        {
            track.RemovePlugin(plugin);
            if (track is Track t) t.Commit();
        };
        this.AddDock(removeBtn, Dock.Right);

        // Open editor button
        var editorBtn = new Button() { Width = 24, Height = 24, Margin = new Thickness(4) };
        editorBtn.AddContent(new ButtonContent
        {
            Item = new TextItem { Text = "⚙", FontSize = 14 },
            ColorSet = new ColorSet { Color = Style.LIGHT_WHITE, HoveredColor = Style.TEXT_LIGHT }
        });
        editorBtn.SetupToolTip("Open Editor".Tr(TC.Property));
        editorBtn.Clicked += () =>
        {
            if (!plugin.IsLoaded)
            {
                plugin.LoadPlugin();
            }
            if (plugin.IsLoaded)
            {
                var window = this.Window();
                if (window != null)
                {
                    plugin.OpenEditor(window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                }
            }
        };
        this.AddDock(editorBtn, Dock.Right);

        // Context menu for right-click
        ContextMenu = new ContextMenu
        {
            Items =
            {
                new MenuItem { Header = "Open Editor".Tr(TC.Property) }.SetAction(() =>
                {
                    if (!plugin.IsLoaded) plugin.LoadPlugin();
                    if (plugin.IsLoaded)
                    {
                        var window = this.Window();
                        if (window != null)
                        {
                            plugin.OpenEditor(window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
                        }
                    }
                }),
                new MenuItem { Header = "Bypass".Tr(TC.Property) }.SetAction(() =>
                {
                    plugin.Bypassed.Set(!plugin.Bypassed.Value);
                    if (track is Track t) t.Commit();
                }),
                new Separator(),
                new MenuItem { Header = "Remove".Tr(TC.Property) }.SetAction(() =>
                {
                    track.RemovePlugin(plugin);
                    if (track is Track t) t.Commit();
                })
            }
        };
    }

    private readonly ITrackPlugin mPlugin;
    private readonly ITrack mTrack;
    private readonly DisposableManager mDisposable = new();
}
