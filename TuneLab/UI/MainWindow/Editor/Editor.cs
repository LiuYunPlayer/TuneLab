using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.GUI;
using TuneLab.Data;
using TuneLab.Audio;
using TuneLab.Extensions.Voices;
using Timer = System.Timers.Timer;
using Avalonia.Controls;
using System.Threading;
using Avalonia;
using TuneLab.GUI.Components;
using Avalonia.Media;
using TuneLab.Base.Event;
using Avalonia.Threading;
using Avalonia.Input;
using System.Diagnostics.CodeAnalysis;
using TuneLab.Extensions.Formats.DataInfo;
using Avalonia.Platform.Storage;
using System.IO;
using TuneLab.Extensions.Formats;
using TuneLab.GUI.Input;
using System.Diagnostics;
using static TuneLab.GUI.Dialog;
using TuneLab.Utils;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;
using TuneLab.Extensions;
using System.IO.Compression;
using System.Xml.Linq;
using System.Text.Json;
using TuneLab.I18N;
using TuneLab.Configs;
using Splat;
using System.Reactive.Joins;
using System.Runtime.InteropServices;

namespace TuneLab.UI;

internal class Editor : DockPanel, PianoWindow.IDependency, TrackWindow.IDependency, FunctionBar.IDependency
{
    public Menu Menu { get; }
    public TrackWindow TrackWindow => mTrackWindow;
    public PianoWindow PianoWindow => mPianoWindow;
    public ProjectDocument Document => mDocument;
    public Project? Project => mDocument.Project;
    public IPlayhead Playhead => mPlayhead;
    public IProvider<IProject> ProjectProvider => mDocument.ProjectProvider;
    public IProvider<IPart> EditingPart => mPianoWindow.PartProvider;
    public INotifiableProperty<PianoTool> PianoTool { get; } = new NotifiableProperty<PianoTool>(UI.PianoTool.Note);
    public INotifiableProperty<PlayScrollTarget> PlayScrollTarget { get; } = new NotifiableProperty<PlayScrollTarget>(UI.PlayScrollTarget.None);
    public Editor()
    {
        Background = Style.BACK.ToBrush();
        Focusable = true;
        IsTabStop = false;

        mPlayhead = new(this);

        mFunctionBar = new(this);
        mPianoWindow = new(this);// { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        mTrackWindow = new(this);
        mRightSideTabBar = new();
        mRightSideBar = new() { Width = 280, Margin = new(1, 0, 0, 0) };

        var panel = new DockPanel() { Background = Style.INTERFACE.ToBrush(), Margin = new(1, 0, 0, 0) };
        {
            var hoverBack = Colors.White.Opacity(0.05);
            var settingsButton = new GUI.Components.Button() { Width = 48, Height = 48 }
            .AddContent(new() { Item = new IconItem() { Icon = Assets.Settings, Scale = 4.0 / 3.0 }, ColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5), HoveredColor = Colors.White, PressedColor = Colors.White } });
            settingsButton.Clicked += () => new SettingsWindow().Show(this.Window());
            panel.AddDock(settingsButton, Dock.Bottom);
            panel.AddDock(mRightSideTabBar);
        }
        this.AddDock(panel, Dock.Right);
        this.AddDock(mRightSideBar, Dock.Right); mRightSideBar.IsVisible = false;
        this.AddDock(mTrackWindow, Dock.Top);
        this.AddDock(mFunctionBar, Dock.Top);
        this.AddDock(mPianoWindow);

        MinHeight = mFunctionBar.Height;

        mFunctionBar.Moved += y => TrackWindowHeight = y;
        mFunctionBar.CollapsePropertiesAsked += show => mRightSideBar.IsVisible = show;
        mFunctionBar.GotoStartAsked += () =>
        {
            var startTime = 0;
            AudioEngine.Seek(startTime);
            if (Project == null) 
                return;

            var startTick = Project.TempoManager.GetTick(startTime);
            mTrackWindow.TickAxis.AnimateMoveTickToX(startTick, 0);
            mPianoWindow.TickAxis.AnimateMoveTickToX(startTick, 0);
        };
        mFunctionBar.GotoEndAsked += () =>
        {
            var endTime = AudioEngine.EndTime;
            AudioEngine.Seek(endTime);
            if (Project == null) 
                return;

            var endTick = Project.TempoManager.GetTick(endTime);
            mTrackWindow.TickAxis.AnimateMoveTickToX(endTick, mTrackWindow.TickAxis.ViewLength);
            mPianoWindow.TickAxis.AnimateMoveTickToX(endTick, mPianoWindow.TickAxis.ViewLength);
        };
        ProjectProvider.ObjectWillChange.Subscribe(OnProjectWillChange, s);
        ProjectProvider.ObjectChanged.Subscribe(OnProjectChanged, s);
        ProjectProvider.When(project => project.Tracks.Any(track => track.Parts.ItemRemoved)).Subscribe(part => { if (part == mEditingPart) SwitchEditingPart(null); });
        ProjectProvider.When(project => project.Tracks.Any(track => track.Parts.ItemAdded)).Subscribe(part => { if (part == mEditingPart) SwitchEditingPart(mEditingPart); });
        ProjectProvider.When(project => project.Tracks.ItemRemoved).Subscribe(track => { if (track.Parts.Contains(mEditingPart)) SwitchEditingPart(null); });
        ProjectProvider.When(project => project.Tracks.ItemAdded).Subscribe(track => { if (track.Parts.Contains(mEditingPart)) SwitchEditingPart(mEditingPart); });
        mPianoWindow.PartProvider.ObjectChanged.Subscribe(() => { mPianoWindow.IsVisible = mPianoWindow.Part != null; mPropertySideBarContentProvider.SetPart(mPianoWindow.Part); }, s);

        mRightSideTabBar.SelectedTab.Modified.Subscribe(() => 
        {
            mRightSideBar.IsVisible = true;
            switch (mRightSideTabBar.SelectedTab.Value)
            {
                case SideBarTab.Properties:
                    mRightSideBar.SetContent(mPropertySideBarContentProvider.Content);
                    break;
                default:
                    mRightSideBar.IsVisible = false;
                    break;
            }
        });
        mRightSideBar.SetContent(mPropertySideBarContentProvider.Content);

        AddHandler(DragDrop.DropEvent, OnDrop);

        Menu = CreateMenu();

        mFunctionBar.GotFocus += (s, e) => { mPianoWindow.PianoScrollView.Focus(); };
        mFunctionBar.QuantizationChanged.Subscribe(mPianoWindow.Quantization.Set);
        mFunctionBar.QuantizationChanged.Subscribe(mTrackWindow.Quantization.Set);
        mDocument.StatusChanged += () => { mUndoMenuItem.IsEnabled = mDocument.Undoable(); mRedoMenuItem.IsEnabled = mDocument.Redoable(); };
        mAutoSaveTimer.Tick += (s, e) => { AutoSave(); };
        Settings.AutoSaveInterval.Modified.Subscribe(() => mAutoSaveTimer.Interval = new TimeSpan(0, 0, Settings.AutoSaveInterval), s);
        PathManager.MakeSureExist(PathManager.AutoSaveFolder);
        RecentFilesManager.Init();
        RecentFilesManager.RecentFilesChanged += (sender, args) => UpdateRecentFilesMenu();

        NewProject();
        CheckUpdate();
    }

    ~Editor()
    {
        s.DisposeAll();
    }

    public void SwitchEditingPart(IPart? part)
    {
        mLastPart = mEditingPart;
        mEditingPart = part;
        if (part == null)
        {
            mPianoWindow.Part = null;
        }
        else if (part is IMidiPart midiPart)
        {
            mPianoWindow.Part = midiPart;
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        mTrackWindow.Height = TrackWindowHeight;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.IsHandledByTextBox())
            return;

        e.Handled = true;
        if (e.Match(Key.Space))
        {
            ChangePlayState();
        }
        else if (e.Match(Key.Z, ModifierKeys.Ctrl))
        {
            Undo();
        }
        else if (e.Match(Key.Y, ModifierKeys.Ctrl))
        {
            Redo();
        }
        else if (e.Match(Key.Tab, ModifierKeys.Ctrl))
        {
            if (mLastPart != null && mDocument.Pushable())
            {
                var track = mLastPart.Track;
                if (track.Parts.Contains(mLastPart) && track.Project.Tracks.Contains(track))
                {
                    SwitchEditingPart(mLastPart);
                }
            }
        }
        else if (e.ModifierKeys() == ModifierKeys.None && e.Key >= Key.D1 && e.Key <= Key.D6)
        {
            mPianoWindow.PianoTool.Value = (PianoTool)(e.Key - Key.D1);
        }
        else
        {
            e.Handled = false;
        }
    }

    void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles()?.Select(s => s.TryGetLocalPath()!).Where(s => s != null);
        if (files == null)
            return;

        List<string> tlxs = [];
        string? projectFile = null;

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file);
            if (extension == ".tlx")
            {
                tlxs.Add(file);
            }
            else if (extension == ".zip" && Path.GetFileName(file).StartsWith("【vsqx分享平台】"))
            {
                using ZipArchive zip = ZipFile.OpenRead(file);
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (entry.FullName.StartsWith("【调音者："))
                    {
                        using Stream stream = entry.Open();
                        var tempFilePath = Path.Combine(Path.GetTempPath(), entry.FullName);
                        using (var tempFileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                        {
                            stream.CopyTo(tempFileStream);
                        }
                        LoadProject(tempFilePath);
                        break;
                    }
                }
            }
            else if (FormatsManager.GetAllImportFormats().Contains(extension.TrimStart('.')))
            {
                projectFile = file;
            }
        }

        if (projectFile != null)
        {
            e.Handled = true;
            SwitchProjectSafely(() =>
            {
                LoadProject(projectFile);
            });
        }
        else if (!tlxs.IsEmpty())
        {
            e.Handled = true;
            InstallExtensions(tlxs);
        }
    }

    void OnProjectWillChange()
    {
        if (Project == null)
            return;

        SwitchEditingPart(null);
        StopAutoSynthesis();
        mAutoSaveTimer.Stop();
        ClearAutoSaveFile();
    }

    void OnProjectChanged()
    {
        if (Project == null)
            return;

        StartAutoSynthesis();
        mAutoSaveTimer.Start();

        if (Project.Tracks.Count == 0)
            return;

        foreach (var part in Project.Tracks.SelectMany(track => track.Parts))
        {
            if (part is MidiPart midiPart)
            {
                SwitchEditingPart(midiPart);
                break;
            }
        }
    }

    void StartAutoSynthesis()
    {
        if (mTimer != null)
            return;

        var context = SynchronizationContext.Current ?? throw new Exception("Can not get SynchronizationContext!");
        mTimer = new(50);
        mTimer.Elapsed += (s, e) => { context.Post(_ => SynthesisNext(), null); };
        mTimer.Start();
    }

    void StopAutoSynthesis()
    {
        if (mTimer == null)
            return;

        mTimer.Stop();
        mTimer.Dispose();
        mTimer = null;
    }

    void SynthesisNext()
    {
        if (Project == null)
            return;

        List<ISynthesisPiece> pieces = new();
        foreach (var track in Project.Tracks)
        {
            foreach (var part in track.Parts)
            {
                if (part is not MidiPart midiPart)
                    continue;

                var piece = midiPart.FindNextNotCompletePiece(AudioEngine.CurrentTime);
                if (piece != null && piece.SynthesisStatus == SynthesisStatus.NotSynthesized)
                {
                    pieces.Add(piece);
                }
            }
        }

        var min = pieces.MinBy(piece => piece.StartTime());
        if (min == null)
            return;

        min.StartSynthesis();
        if (min.SynthesisStatus == SynthesisStatus.SynthesisSucceeded)
        {
            SynthesisNext();
        }
    }

    public void ClearAutoSaveFile()
    {
        mAutoSaveHead = default;
        foreach (var file in Directory.GetFiles(PathManager.AutoSaveFolder))
        {
            File.Delete(file);
        }
    }

    async void AutoSave()
    {
        if (mDocument.Project == null || mDocument.IsSaved || mAutoSaveHead == mDocument.Head)
            return;

        var projectInfo = mDocument.Project.GetInfo();

        try
        {
            var autoSavePath = Path.Combine(PathManager.AutoSaveFolder, DateTime.Now.ToString("yyyy-MM-dd_hh-mm-ss_") + Path.GetFileNameWithoutExtension(mDocument.Name) + ".tlp");

            await Task.Run(() =>
            {
                if (!FormatsManager.Serialize(projectInfo, "tlp", out var stream, out var error))
                {
                    Log.Error("Save file error: " + error);
                    return;
                }

                using (FileStream fileStream = new FileStream(autoSavePath, FileMode.Create))
                {
                    stream.CopyTo(fileStream);
                }

                foreach (var file in Directory.GetFiles(PathManager.AutoSaveFolder))
                {
                    if (file != autoSavePath)
                        File.Delete(file);
                }
            });

            mAutoSaveHead = mDocument.Head;
            Log.Debug("Project auto saved: " + autoSavePath);
        }
        catch (Exception ex)
        {
            Log.Debug("Write file error: " + ex);
        }
    }

    void NewProject()
    {
        SwitchProjectSafely(() =>
        {
            mDocument.SetProject(CreateProject(new ProjectInfo()
            {
                Tracks = [new()
                {
                    Name = "Track".Tr(TC.Document) + "_1",
                    Parts =
                    [
                        new MidiPartInfo()
                        {
                            Name = "Part".Tr(TC.Document) + "_1",
                            Dur = 64 * MusicTheory.RESOLUTION * 4
                        }
                    ]
                }]
            }));
        });
    }

    async void SwitchProjectSafely(Action SwitchProject)
    {
        if (mDocument.IsSaved)
        {
            SwitchProject();
            return;
        }

        var modal = new Dialog();
        modal.SetTitle("Tips".Tr(TC.Dialog));
        modal.SetMessage("The project has not been saved.\n Do you want to save it?".Tr(TC.Dialog));
        modal.AddButton("Cancel".Tr(TC.Dialog), ButtonType.Normal);
        modal.AddButton("No".Tr(TC.Dialog), ButtonType.Normal).Clicked += () => { SwitchProject(); };
        modal.AddButton("Save".Tr(TC.Dialog), ButtonType.Primary).Clicked += async () => { await SaveProject(); SwitchProject(); };
        modal.Topmost = true;
        await modal.ShowDialog(this.Window());
    }

    void LoadProject(string path)
    {
        if (!FormatsManager.Deserialize(path, out var info, out var error))
        {
            Log.Error("Deserialize file error: " + error);
            return;
        }

        mDocument.SetProject(CreateProject(info), path);
        RecentFilesManager.AddFile(path);
    }

    Project CreateProject(ProjectInfo info)
    {
        for (int i = 0; i < info.Tracks.Count; i++)
        {
            if (string.IsNullOrEmpty(info.Tracks[i].Color))
            {
                info.Tracks[i].Color = Style.GetNewColor(i);
            }
        }

        return new Project(info);
    }

    public void OpenProject()
    {
        SwitchProjectSafely(async () =>
        {
            var formats = FormatsManager.GetAllImportFormats();
            var patterns = new List<string>();
            foreach (var format in formats)
            {
                patterns.Add("*." + format);
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
                return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open File",
                AllowMultiple = false,
                FileTypeFilter = [new("Importable Formats") { Patterns = patterns }]
            });
            var path = files.IsEmpty() ? null : files[0].TryGetLocalPath();
            if (path == null)
                return;

            LoadProject(path);
        });
    }

    async public void OpenProjectByPath(string path)
    {
        if (!File.Exists(path))
        {
            var modal = new Dialog();
            modal.SetTitle("Tips".Tr(TC.Dialog));
            modal.SetMessage("The file failed to open because it does not exist.".Tr(TC.Dialog));
            modal.AddButton("OK".Tr(TC.Dialog), ButtonType.Primary);
            modal.Topmost = true;
            await modal.ShowDialog(this.Window());

            return;
        }

        LoadProject(path);
    }

    public async Task SaveProject()
    {
        if (!File.Exists(mDocument.Path) || Path.GetExtension(mDocument.Path) != ".tlp")
        {
            await SaveProjectAs();
            return;
        }
        SaveToFile(mDocument.Path);
        RecentFilesManager.AddFile(mDocument.Path);
    }

    public async Task SaveProjectAs()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File".Tr(TC.Dialog),
            DefaultExtension = ".tlp",
            SuggestedFileName = Path.GetFileNameWithoutExtension(mDocument.Name),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new("TuneLab Project".Tr(TC.Dialog)) { Patterns = ["*.tlp"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path == null)
            return;

        SaveToFile(path);
        RecentFilesManager.AddFile(path);
    }

    public async void ExportAs(string extension)
    {
        if (mDocument.Project == null)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export As".Tr(TC.Dialog),
            DefaultExtension = extension,
            SuggestedFileName = Path.GetFileNameWithoutExtension(mDocument.Name),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new("Project") { Patterns = ["*." + extension] }]
        });
        var path = file?.TryGetLocalPath();
        if (path == null)
            return;

        if (!FormatsManager.Serialize(mDocument.Project.GetInfo(), extension, out var stream, out var error))
        {
            Log.Error("Save file error: " + error);
            return;
        }

        using (FileStream fileStream = new FileStream(path, FileMode.Create))
        {
            stream.CopyTo(fileStream);
        }
        RecentFilesManager.AddFile(path);
    }

    void SaveToFile(string path)
    {
        if (mDocument.Project == null)
            return;

        if (!FormatsManager.Serialize(mDocument.Project.GetInfo(), "tlp", out var stream, out var error))
        {
            Log.Error("Save file error: " + error);
            return;
        }

        try
        {
            using (FileStream fileStream = new FileStream(path, FileMode.Create))
            {
                stream.CopyTo(fileStream);
            }

            ClearAutoSaveFile();

            mDocument.SetSavePath(path);
        }
        catch (Exception ex)
        {
            Log.Debug("Write file error: " + ex);
        }
    }

    public async void ExportMix()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File".Tr(TC.Dialog),
            DefaultExtension = ".wav",
            SuggestedFileName = Path.GetFileNameWithoutExtension(mDocument.Name),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new("WAVE File".Tr(TC.Dialog)) { Patterns = ["*.wav"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path == null)
            return;

        try
        {
            AudioEngine.ExportMaster(path, true);
        }
        catch (Exception ex)
        {
            await this.ShowMessage("Error".Tr(TC.Dialog), "Export failed: \n" + ex.Message);
        }
    }

    public void Undo()
    {
        mDocument.Undo();
    }

    public void Redo()
    {
        mDocument.Redo();
    }

    public void AddTrack()
    {
        var project = Project;
        if (project == null)
            return;

        project.NewTrack();
        project.Commit();
    }

    public void ImportAudio()
    {
        if (Project == null)
            return;

        TrackWindow.TrackScrollView.ImportAudioAt(0, Project.Tracks.Count);
    }

    public void ImportTrack()
    {
        if (Project == null)
            return;

        TrackWindow.TrackScrollView.ImportTrack();
    }

    public void ChangePlayState()
    {
        if (AudioEngine.IsPlaying) AudioEngine.Pause();
        else AudioEngine.Play();
    }

    struct Description
    {
        public string name { get; set; }
    }

    public async void InstallExtensions(IEnumerable<string> files)
    {
        List<string> installedExtension = [];
        foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var entry = ZipFile.OpenRead(file).GetEntry("description.json");
            if (entry != null)
            {
                var description = JsonSerializer.Deserialize<Description>(entry.Open());
                if (!string.IsNullOrEmpty(description.name))
                    name = description.name;
            }
            var dir = Path.Combine(PathManager.ExtensionsFolder, name);
            if (Directory.Exists(dir))
            {
                installedExtension.Add(file);
                continue;
            }

            try
            {
                ZipFileHelper.ExtractToDirectory(file, dir);
                ExtensionManager.Load(dir);
                await this.ShowMessage("Tips".Tr(TC.Dialog), name + " has been successfully installed!".Tr(TC.Dialog));
            }
            catch (Exception ex)
            {
                await this.ShowMessage("Error".Tr(TC.Dialog), "Installating " + name + " failed: \n".Tr(TC.Dialog) + ex.Message);
            }
        }

        if (installedExtension.IsEmpty())
            return;

        var dialog = new Dialog();
        dialog.SetTitle("Tips".Tr(TC.Dialog));
        dialog.SetMessage("Detected an installed extension. \nDo you want to restart and perform a reinstall?".Tr(TC.Dialog));
        dialog.AddButton("Yes".Tr(TC.Dialog), ButtonType.Normal).Clicked += () =>
        {
            List<string> args = ["-restart"];
            args.AddRange(installedExtension);
            string installer = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ExtensionInstaller.exe" : "ExtensionInstaller";
            ProcessHelper.CreateProcess(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, installer), args);
            this.Window().Close();
        };
        dialog.AddButton("No".Tr(TC.Dialog), ButtonType.Primary);
        await dialog.ShowDialog(this.Window());
    }

    private async void UpdateDialog(UpdateInfo mUpdateCheck, bool IsAutoCheck)
    {
        var dialog = new UpdateDialog();
        dialog.SetMessage("Version".Tr(TC.Dialog) + $": {mUpdateCheck.version}\n" + "Public Date".Tr(TC.Dialog) + $": {mUpdateCheck.publishedAt}");
        dialog.SetMDMessage(mUpdateCheck.description ?? "");
        if (IsAutoCheck)
            dialog.AddButton("Ignore".Tr(TC.Dialog), GUI.UpdateDialog.ButtonType.Normal).Clicked += () => AppUpdateManager.SaveIgnoreVersion(mUpdateCheck.version);
        dialog.AddButton("Later".Tr(TC.Dialog), GUI.UpdateDialog.ButtonType.Normal);
        dialog.AddButton("Download".Tr(TC.Dialog), GUI.UpdateDialog.ButtonType.Primary).Clicked += () =>
        {
            ProcessHelper.OpenUrl(mUpdateCheck.url);
        };
        await dialog.ShowDialog(this.Window());
    }

    public async void CheckUpdate(bool IsAutoCheck = true)
    {
        try
        {
            var mUpdateCheck = await AppUpdateManager.CheckForUpdate(IsAutoCheck);
            if (mUpdateCheck != null)
            {
                Log.Info($"Update available: {mUpdateCheck.version}");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    UpdateDialog(mUpdateCheck, IsAutoCheck);
                });
            }
            else
            {
                Log.Info("No update available.");
                if (!IsAutoCheck)
                    await Dispatcher.UIThread.InvokeAsync(async () =>
                    {
                        await this.ShowMessage("Update".Tr(TC.Dialog), "No updates at the moment.".Tr(TC.Dialog));
                    });
            }
        }
        catch (Exception ex)
        {
            Log.Error($"CheckUpdate: {ex.Message}");
            if (!IsAutoCheck)
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await this.ShowMessage("Check update failed".Tr(TC.Dialog), "An error occurred while checking for updates. Please check the log for more details.".Tr(TC.Dialog));
                });
            }
        }
    }

    [MemberNotNull(nameof(mUndoMenuItem))]
    [MemberNotNull(nameof(mRedoMenuItem))]
    [MemberNotNull(nameof(mRecentFilesMenu))]
    Menu CreateMenu()
    {
        var menu = new Menu() { Background = Style.BACK.ToBrush(), Height = 40 };
        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("File");
            {
                var menuItem = new MenuItem().SetTrName("New").SetAction(NewProject).SetShortcut(Key.N, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Open").SetAction(OpenProject).SetShortcut(Key.O, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem() { Foreground = Style.TEXT_LIGHT.ToBrush() }.SetTrName("Recent Files");
                foreach (var mRecentFile in RecentFilesManager.GetRecentFiles())
                {
                    var mRecentFilesMenuItem = new MenuItem().SetName(mRecentFile.FileName).SetAction(() =>
                    {
                        SwitchProjectSafely(() => OpenProjectByPath(mRecentFile.FilePath));
                        Menu.Close();
                    });
                    menuItem.Items.Add(mRecentFilesMenuItem);
                }

                if (menuItem.Items.Count == 0)
                {
                    var mRecentFilesMenuItem = new MenuItem().SetTrName("Empty");
                    mRecentFilesMenuItem.IsEnabled = false;
                    menuItem.Items.Add(mRecentFilesMenuItem);
                }

                mRecentFilesMenu = menuItem;
                menuBarItem.Items.Add(mRecentFilesMenu);
            }
            {
                var menuItem = new MenuItem().SetTrName("Save").SetAction(async () => { await SaveProject(); }).SetShortcut(Key.S, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Save As").SetAction(async () => { await SaveProjectAs(); }).SetShortcut(Key.S, ModifierKeys.Ctrl | ModifierKeys.Shift);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem() { Foreground = Style.TEXT_LIGHT.ToBrush() }.SetTrName("Export As (test)");
                foreach (var format in FormatsManager.GetAllExportFormats())
                {
                    var menuItem2 = new MenuItem().SetName(format).SetAction(() => ExportAs(format));
                    menuItem.Items.Add(menuItem2);
                }
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Export Mix").SetAction(ExportMix);
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Edit");
            {
                var menuItem = new MenuItem().SetTrName("Undo").SetAction(Undo).SetInputGesture(Key.Z, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
                mUndoMenuItem = menuItem;
            }
            {
                var menuItem = new MenuItem().SetTrName("Redo").SetAction(Redo).SetInputGesture(Key.Y, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
                mRedoMenuItem = menuItem;
            }
            {
                var menuItem = new MenuItem().SetTrName("Settings").SetAction(() =>
                {
                    var settingsWindow = new SettingsWindow();
                    settingsWindow.Show(this.Window());
                });
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Project");
            {
                var menuItem = new MenuItem().SetTrName("Add Track").SetAction(AddTrack);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Import Audio").SetAction(ImportAudio);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Import Track").SetAction(ImportTrack);
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Transport");
            {
                var menuItem = new MenuItem().
                    SetName("Play".Tr(TC.Menu)).
                    SetAction(ChangePlayState).
                    SetInputGesture(Key.Space);
                void UpdateHeader() => menuItem.SetName(AudioEngine.IsPlaying ? "Pause".Tr(TC.Menu) : "Play".Tr(TC.Menu));
                AudioEngine.PlayStateChanged += UpdateHeader;
                TranslationManager.CurrentLanguage.Modified.Subscribe(UpdateHeader);
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Extensions");
            {
                var menuItem = new MenuItem().SetTrName("Install/Update").SetAction(async () =>
                {
                    var topLevel = TopLevel.GetTopLevel(this);
                    if (topLevel == null)
                        return;
                    var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = "Open Tlx File",
                        AllowMultiple = false,
                        FileTypeFilter = [new("TuneLab Extension") { Patterns = ["*.tlx"] }]
                    });
                    if (files.IsEmpty()) return;
                    var fileList = files.Select(f => f.TryGetLocalPath()).Where(f => f != null).ToArray();
                    if (fileList != null) InstallExtensions(fileList);
                });
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false }.SetTrName("Help");
            {
                var menuItem = new MenuItem().SetTrName("TuneLab Forum").SetAction(() => ProcessHelper.OpenUrl("https://forum.tunelab.app"));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("TuneLab GitHub").SetAction(() => ProcessHelper.OpenUrl("https://github.com/LiuYunPlayer/TuneLab"));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Open TuneLab Folder").SetAction(() => ProcessHelper.OpenUrl(PathManager.TuneLabFolder));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Open Log").SetAction(() => ProcessHelper.OpenUrl(PathManager.LogFilePath));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetTrName("Check for Updates...").SetAction(() => CheckUpdate(false));
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        return menu;
    }

    MenuItem mUndoMenuItem;
    MenuItem mRedoMenuItem;
    public MenuItem mRecentFilesMenu;

    class PlayheadForProject : IPlayhead
    {
        public IActionEvent PosChanged => mPosChanged;

        public double Pos
        {
            get => mCursorPos;
            set
            {
                SyncCursorPos(value);
                if (mEditor.Project == null)
                    return;

                AudioEngine.Seek(mEditor.Project.TempoManager.GetTime(mCursorPos));
            }
        }

        public PlayheadForProject(Editor editor)
        {
            mEditor = editor;
            AudioEngine.ProgressChanged += OnAudioEngineProgress;
        }

        ~PlayheadForProject()
        {
            AudioEngine.ProgressChanged -= OnAudioEngineProgress;
        }

        void OnAudioEngineProgress()
        {
            if (mEditor.Project == null)
                return;

            var newCursorPos = mEditor.Project.TempoManager.GetTick(AudioEngine.CurrentTime);
            SyncCursorPos(newCursorPos);
        }

        void SyncCursorPos(double newCursorPos)
        {
            newCursorPos = Math.Max(0, newCursorPos);
            if (mCursorPos == newCursorPos)
                return;

            mCursorPos = newCursorPos;
            mPosChanged.Invoke();
        }

        double mCursorPos = 0;

        readonly Editor mEditor;
        readonly ActionEvent mPosChanged = new();
    }

    double mTrackWindowHeight = 240;
    double TrackWindowHeight
    {
        get => mTrackWindowHeight.Limit(mTrackWindow.MinHeight, Bounds.Height - mFunctionBar.Bounds.Height);
        set { mTrackWindowHeight = value; mTrackWindowHeight = TrackWindowHeight; mTrackWindow.Height = mTrackWindowHeight; }
    }

    private void UpdateRecentFilesMenu()
    {
        mRecentFilesMenu.Items.Clear();
        foreach (var mRecentFile in RecentFilesManager.GetRecentFiles())
        {
            var menuItem = new MenuItem().SetName(mRecentFile.FileName).SetAction(() => SwitchProjectSafely(() => OpenProjectByPath(mRecentFile.FilePath)));
            mRecentFilesMenu.Items.Add(menuItem);
        }
    }

    Timer? mTimer;
    readonly DispatcherTimer mAutoSaveTimer = new() { Interval = new TimeSpan(0, 0, Settings.AutoSaveInterval) };
    Head mAutoSaveHead;

    IPart? mEditingPart = null;
    IPart? mLastPart = null;

    readonly TrackWindow mTrackWindow;
    readonly FunctionBar mFunctionBar;
    readonly PianoWindow mPianoWindow;
    readonly SideBar mRightSideBar;
    readonly SideTabBar mRightSideTabBar;

    readonly PropertySideBarContentProvider mPropertySideBarContentProvider = new();

    readonly PlayheadForProject mPlayhead;

    readonly ProjectDocument mDocument = new();
    readonly DisposableManager s = new();
}
