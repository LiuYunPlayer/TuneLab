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

namespace TuneLab.Views;

internal class Editor : DockPanel, PianoWindow.IDependency, TrackWindow.IDependency
{
    public Menu Menu { get; }
    public TrackWindow TrackWindow => mTrackWindow;
    public PianoWindow PianoWindow => mPianoWindow;
    public ProjectDocument Document => mDocument;
    public Project? Project => mDocument.Project;
    public IPlayhead Playhead => mPlayhead;
    public IProvider<IProject> ProjectProvider => mDocument.ProjectProvider;
    public IProvider<Part> EditingPart => mPianoWindow.PartProvider;
    public bool IsAutoPage => mFunctionBar.IsAutoPage.Value;
    public MenuItem mRecentFilesMenu;
    public Editor()
    {
        Background = Style.BACK.ToBrush();
        Focusable = true;
        IsTabStop = false;

        mPlayhead = new(this);

        mPianoWindow = new(this) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        mTrackWindow = new(this);
        mFunctionBar = new(mPianoWindow);
        mRightSideTabBar = new();
        mRightSideBar = new() { Width = 280 };

        Children.Add(mTrackWindow);
        DockPanel.SetDock(mTrackWindow, Dock.Top);

        Children.Add(mFunctionBar);
        DockPanel.SetDock(mFunctionBar, Dock.Top);

        //Children.Add(mRightSideTabBar);
        //DockPanel.SetDock(mRightSideTabBar, Dock.Right);

        var pianoLayer = new DockPanel() { ClipToBounds = true };
        {
            pianoLayer.Children.Add(mRightSideBar);
            DockPanel.SetDock(mRightSideBar, Dock.Right);

            pianoLayer.Children.Add(mPianoWindow);
        }
        Children.Add(pianoLayer);

        MinHeight = mFunctionBar.Height;

        mFunctionBar.Moved += y => TrackWindowHeight = y;
        ProjectProvider.ObjectWillChange.Subscribe(OnProjectWillChange, s);
        ProjectProvider.ObjectChanged.Subscribe(OnProjectChanged, s);
        ProjectProvider.When(project => project.Tracks.Any(track => track.Parts.ItemRemoved)).Subscribe(part => { if (part == mEditingPart) mPianoWindow.Part = null; });
        ProjectProvider.When(project => project.Tracks.Any(track => track.Parts.ItemAdded)).Subscribe(part => { if (part == mEditingPart) mPianoWindow.Part = mEditingPart; });
        ProjectProvider.When(project => project.Tracks.ItemRemoved).Subscribe(track => { if (track.Parts.Contains(mEditingPart)) mPianoWindow.Part = null; });
        ProjectProvider.When(project => project.Tracks.ItemAdded).Subscribe(track => { if (track.Parts.Contains(mEditingPart)) mPianoWindow.Part = mEditingPart; });
        mPianoWindow.PartProvider.ObjectChanged.Subscribe(() => { mPianoWindow.IsVisible = mPianoWindow.Part != null; mPropertySideBarContentProvider.SetPart(mPianoWindow.Part); }, s);

        mRightSideBar.SetContent(mPropertySideBarContentProvider.Content);

        AddHandler(DragDrop.DropEvent, OnDrop);

        Menu = CreateMenu();

        mFunctionBar.GotFocus += (s, e) => { mPianoWindow.PianoScrollView.Focus(); };
        mFunctionBar.QuantizationChanged.Subscribe(mPianoWindow.Quantization.Set);
        mFunctionBar.QuantizationChanged.Subscribe(mTrackWindow.Quantization.Set);
        mDocument.StatusChanged += () => { mUndoMenuItem.IsEnabled = mDocument.Undoable(); mRedoMenuItem.IsEnabled = mDocument.Redoable(); };
        mAutoSaveTimer.Tick += (s, e) => { AutoSave(); };
        PathManager.MakeSure(PathManager.AutoSaveFolder);
        RecentFilesManager.Init();
        RecentFilesManager.RecentFilesChanged += (sender, args) => UpdateRecentFilesMenu();

        NewProject();
    }

    ~Editor()
    {
        s.DisposeAll();
    }

    public void SwitchEditingPart(IPart? part)
    {
        var midiPart = part as MidiPart;
        mEditingPart = midiPart;
        mPianoWindow.Part = midiPart;
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
                    Name = "Track_1",
                    Parts =
                    [
                        new MidiPartInfo()
                        {
                            Name = "Part_1",
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
        modal.SetTitle("Tips");
        modal.SetMessage("The project has not been saved.\n Do you want to save it?");
        modal.AddButton("Cancel", ButtonType.Normal);
        modal.AddButton("No", ButtonType.Normal).Clicked += () => { SwitchProject(); };
        modal.AddButton("Save", ButtonType.Primary).Clicked += async () => { await SaveProject(); SwitchProject(); };
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
                ZipFile.ExtractToDirectory(file, dir);
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
        dialog.AddButton("Yes".Tr(TC.Dialog), ButtonType.Normal).Clicked += () => {
            List<string> args = ["-restart"];
            args.AddRange(installedExtension);
            ProcessHelper.CreateProcess(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtensionInstaller.exe"), args);
            this.Window().Close();
        };
        dialog.AddButton("No".Tr(TC.Dialog), ButtonType.Primary);
        await dialog.ShowDialog(this.Window());
    }

    [MemberNotNull(nameof(mUndoMenuItem))]
    [MemberNotNull(nameof(mRedoMenuItem))]
    Menu CreateMenu()
    {
        var menu = new Menu() { Background = Style.BACK.ToBrush(), Height = 40 };
        {
            var menuBarItem = new MenuItem { Header = "File".Tr(TC.Menu), Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false };
            {
                var menuItem = new MenuItem().SetName("New".Tr(TC.Menu)).SetAction(NewProject).SetShortcut(Key.N, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("Open".Tr(TC.Menu)).SetAction(OpenProject).SetShortcut(Key.O, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
            }
            { 
                var menuItem = new MenuItem() { Header = "Recent Files".Tr(TC.Menu), Foreground = Style.TEXT_LIGHT.ToBrush() };
                foreach (var mRecentFile in RecentFilesManager.GetRecentFiles())
                {
                    var mRecentFilesMenuItem = new MenuItem().SetName(mRecentFile.FileName).SetAction(() => OpenProjectByPath(mRecentFile.FilePath));
                    menuItem.Items.Add(mRecentFilesMenuItem);
                }

                if (menuItem.Items.Count == 0)
                {
                    var mRecentFilesMenuItem = new MenuItem().SetName("Empty".Tr(TC.Menu));
                    mRecentFilesMenuItem.IsEnabled = false;
                    menuItem.Items.Add(mRecentFilesMenuItem);
                }

                mRecentFilesMenu = menuItem;
                menuBarItem.Items.Add(mRecentFilesMenu);
            }
            {
                var menuItem = new MenuItem().SetName("Save".Tr(TC.Menu)).SetAction(async () => { await SaveProject(); }).SetShortcut(Key.S, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("Save As".Tr(TC.Menu)).SetAction(async () => { await SaveProjectAs(); }).SetShortcut(Key.S, ModifierKeys.Ctrl | ModifierKeys.Shift);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem() { Header = "Export As (test)".Tr(TC.Menu), Foreground = Style.TEXT_LIGHT.ToBrush() };
                foreach (var format in FormatsManager.GetAllExportFormats())
                {
                    var menuItem2 = new MenuItem().SetName(format).SetAction(() => ExportAs(format));
                    menuItem.Items.Add(menuItem2);
                }
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("Export Mix".Tr(TC.Menu)).SetAction(ExportMix);
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Header = "Edit".Tr(TC.Menu), Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false };
            {
                var menuItem = new MenuItem().SetName("Undo".Tr(TC.Menu)).SetAction(Undo).SetInputGesture(Key.Z, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
                mUndoMenuItem = menuItem;
            }
            {
                var menuItem = new MenuItem().SetName("Redo".Tr(TC.Menu)).SetAction(Redo).SetInputGesture(Key.Y, ModifierKeys.Ctrl);
                menuBarItem.Items.Add(menuItem);
                mRedoMenuItem = menuItem;
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Header = "Project".Tr(TC.Menu), Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false };
            {
                var menuItem = new MenuItem().SetName("Add Track".Tr(TC.Menu)).SetAction(AddTrack);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("Import Audio".Tr(TC.Menu)).SetAction(ImportAudio);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("Import Track".Tr(TC.Menu)).SetAction(ImportTrack);
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Header = "Transport".Tr(TC.Menu), Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false };
            {
                var menuItem = new MenuItem().
                    SetName("Play".Tr(TC.Menu)).
                    SetAction(ChangePlayState).
                    SetInputGesture(Key.Space);
                AudioEngine.PlayStateChanged += () =>
                {
                    menuItem.Header = AudioEngine.IsPlaying ? "Pause".Tr(TC.Menu) : "Play".Tr(TC.Menu);
                };
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Header = "Help".Tr(TC.Menu), Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false };
            {
                var menuItem = new MenuItem().SetName("TuneLab Forum".Tr(TC.Menu)).SetAction(() => ProcessHelper.OpenUrl("https://forum.tunelab.app"));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("TuneLab GitHub".Tr(TC.Menu)).SetAction(() => ProcessHelper.OpenUrl("https://github.com/LiuYunPlayer/TuneLab"));
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        return menu;
    }

    MenuItem mUndoMenuItem;
    MenuItem mRedoMenuItem;
    MenuItem mRecentFilesMenuItem;

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
            var menuItem = new MenuItem().SetName(mRecentFile.FileName).SetAction(() => OpenProjectByPath(mRecentFile.FilePath));
            mRecentFilesMenu.Items.Add(menuItem);
        }
    }

    Timer? mTimer;
    readonly DispatcherTimer mAutoSaveTimer = new() { Interval = new TimeSpan(0, 0, 10) };
    Head mAutoSaveHead;

    MidiPart? mEditingPart = null;

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
