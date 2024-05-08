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

namespace TuneLab.Views;

internal class Editor : DockPanel, PianoWindow.IDependency, TrackWindow.IDependency
{
    public Menu Menu { get; }
    public TrackWindow TrackWindow => mTrackWindow;
    public PianoWindow PianoWindow => mPianoWindow;
    public ProjectDocument Document => mDocument;
    public Project? Project => mDocument.Project;
    public IPlayhead Playhead => mPlayhead;
    public IProvider<Project> ProjectProvider => mDocument.ProjectProvider;
    public IProvider<Part> EditingPart => mPianoWindow.PartProvider;

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

        mFunctionBar.GotFocus += (s, e) => { mPianoWindow.PianoGrid.Focus(); };
        mDocument.StatusChanged += () => { mUndoMenuItem.IsEnabled = mDocument.Undoable(); mRedoMenuItem.IsEnabled = mDocument.Redoable(); };
        mAutoSaveTimer.Tick += (s, e) => { AutoSave(); };
        PathManager.MakeSure(PathManager.AutoSaveFolder);

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
        var path = e.Data.GetFiles()?.FirstOrDefault()?.TryGetLocalPath();
        if (path == null)
            return;

        var extension = Path.GetExtension(path);
        if (extension == ".tlx")
        {
            e.Handled = true;
            InstallTLX(path);
        }
        else if (FormatsManager.GetAllImportFormats().Contains(extension.TrimStart('.')))
        {
            e.Handled = true;
            if (!FormatsManager.Deserialize(path, out var info, out var error))
            {
                Log.Error("Open file error: " + error);
                return;
            }

            mDocument.SetProject(new Project(info), path);
        }
        else
        {
            Trace.WriteLine("Unsupported file");
        }
    }

    void OnProjectWillChange()
    {
        if (Project == null)
            return;

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

    async void NewProject()
    {
        if (mDocument.IsSaved)
        {
            CreateAndSwitchNewProject();
            return;
        }

        var modal = new Dialog();
        modal.SetTitle("Tips");
        modal.SetMessage("The project has not been saved.\n Do you want to save it?");
        modal.AddButton("Cancel", ButtonType.Normal);
        modal.AddButton("No", ButtonType.Normal).Clicked += () => { CreateAndSwitchNewProject(); };
        modal.AddButton("Save", ButtonType.Primary).Clicked += async () => { await SaveProject(); CreateAndSwitchNewProject(); };
        modal.Topmost = true;
        await modal.ShowDialog(this.Window());
    }

    void CreateAndSwitchNewProject()
    {
        mDocument.SetProject(new Project(new ProjectInfo() { Tracks = [new() { Name = "Track_1", Parts = [new MidiPartInfo() { Name = "Part_1", Dur = 64 * MusicTheory.RESOLUTION * 4 }] }] }));
    }

    public void LoadProject(string path)
    {
        if (!FormatsManager.Deserialize(path, out var info, out var error))
        {
            Log.Error("Open file error: " + error);
            return;
        }

        mDocument.SetProject(new Project(info), path);
    }

    public async void OpenProject()
    {
        if (mDocument.IsSaved)
        {
            OpenAndSwitchProject();
            return;
        }

        var modal = new Dialog();
        modal.SetTitle("Tips");
        modal.SetMessage("The project has not been saved.\n Do you want to save it?");
        modal.AddButton("Cancel", ButtonType.Normal);
        modal.AddButton("No", ButtonType.Normal).Clicked += () => { OpenAndSwitchProject(); };
        modal.AddButton("Save", ButtonType.Primary).Clicked += async () => { await SaveProject(); OpenAndSwitchProject(); };
        modal.Topmost = true;
        await modal.ShowDialog(this.Window());
    }

    async void OpenAndSwitchProject()
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
    }

    public async Task SaveProject()
    {
        if (!File.Exists(mDocument.Path) || Path.GetExtension(mDocument.Path) != ".tlp")
        {
            await SaveProjectAs();
            return;
        }
        SaveToFile(mDocument.Path);
    }

    public async Task SaveProjectAs()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File",
            DefaultExtension = ".tlp",
            SuggestedFileName = Path.GetFileNameWithoutExtension(mDocument.Name),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new("TuneLab Project") { Patterns = ["*.tlp"] }]
        });
        var path = file?.TryGetLocalPath();
        if (path == null)
            return;

        SaveToFile(path);
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
            Title = "Export As",
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
            Title = "Save File",
            DefaultExtension = ".wav",
            SuggestedFileName = Path.GetFileNameWithoutExtension(mDocument.Name),
            ShowOverwritePrompt = true,
            FileTypeChoices = [new("WAVE File") { Patterns = ["*.wav"] }]
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
            await this.ShowMessage("Error", "Export failed: \n" + ex.Message);
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

        TrackWindow.TrackGrid.ImportAudioAt(0, Project.Tracks.Count);
    }

    public void ChangePlayState()
    {
        if (AudioEngine.IsPlaying) AudioEngine.Pause();
        else AudioEngine.Play();
    }

    public async void InstallTLX(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        var dir = Path.Combine(PathManager.ExtensionsFolder, name);

        if (Directory.Exists(dir))
        {
            var dialog = new Dialog();
            dialog.SetTitle("Tips");
            dialog.SetMessage("The extension is already installed. \nDo you want to restart and perform a reinstall?");
            dialog.AddButton("Yes", ButtonType.Normal).Clicked += () => { 
                Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ExtensionInstaller.exe"), [Environment.ProcessId.ToString(), filePath]); 
            };
            dialog.AddButton("No", ButtonType.Primary);
            await dialog.ShowDialog(this.Window());
            return;
        }

        try
        {
            ZipFile.ExtractToDirectory(filePath, dir);
            ExtensionManager.Load(dir);
            await this.ShowMessage("Tips", name + " has been successfully installed!");
        }
        catch (Exception ex)
        {
            await this.ShowMessage("Error", "Installating " + name + " failed: \n" + ex.Message);
        }
    }

    [MemberNotNull(nameof(mUndoMenuItem))]
    [MemberNotNull(nameof(mRedoMenuItem))]
    Menu CreateMenu()
    {
        var menu = new Menu() { Background = Style.BACK.ToBrush(), Height = 40 };
        {
            var menuBarItem = new MenuItem { Header = "File", Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false };
            {
                var menuItem = new MenuItem().SetName("New").SetAction(NewProject).SetShortcut(new(Key.N, KeyModifiers.Control));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("Open").SetAction(OpenProject).SetShortcut(new(Key.O, KeyModifiers.Control));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("Save").SetAction(async () => { await SaveProject(); }).SetShortcut(new(Key.S, KeyModifiers.Control));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("Save As").SetAction(async () => { await SaveProjectAs(); }).SetShortcut(new(Key.S, KeyModifiers.Control | KeyModifiers.Shift));
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem() { Header = "Export As (test)", Foreground = Style.TEXT_LIGHT.ToBrush() };
                foreach (var format in FormatsManager.GetAllExportFormats())
                {
                    var menuItem2 = new MenuItem().SetName(format).SetAction(() => ExportAs(format));
                    menuItem.Items.Add(menuItem2);
                }
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("Export Mix").SetAction(ExportMix);
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Header = "Edit", Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false };
            {
                var menuItem = new MenuItem().SetName("Undo").SetAction(Undo).SetInputGesture(new(Key.Z, KeyModifiers.Control));
                menuBarItem.Items.Add(menuItem);
                mUndoMenuItem = menuItem;
            }
            {
                var menuItem = new MenuItem().SetName("Redo").SetAction(Redo).SetInputGesture(new(Key.Y, KeyModifiers.Control));
                menuBarItem.Items.Add(menuItem);
                mRedoMenuItem = menuItem;
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Header = "Project", Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false };
            {
                var menuItem = new MenuItem().SetName("Add Track").SetAction(AddTrack);
                menuBarItem.Items.Add(menuItem);
            }
            {
                var menuItem = new MenuItem().SetName("Import Audio").SetAction(ImportAudio);
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Header = "Part", Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false };
            {
                var menuItem = new MenuItem() { Header = "Set Voice" };
                var allEngines = VoicesManager.GetAllVoiceEngines();
                for (int i = 0; i < allEngines.Count; i++)
                {
                    var type = allEngines[i];
                    var infos = VoicesManager.GetAllVoiceInfos(type);
                    if (infos == null)
                        continue;

                    var engine = new MenuItem() { Header = string.IsNullOrEmpty(type) ? "Built-In" : type };
                    {
                        foreach (var info in infos)
                        {
                            var voice = new MenuItem().
                                SetName(info.Value.Name).
                                SetAction(() =>
                                {
                                    var part = PianoWindow.Part;
                                    if (part == null)
                                        return;

                                    part.Voice.Set(new VoiceInfo() { Type = type, ID = info.Key });
                                    part.Commit();
                                });
                            engine.Items.Add(voice);
                        }
                    }
                    menuItem.Items.Add(engine);
                }
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        {
            var menuBarItem = new MenuItem { Header = "Transport", Foreground = Style.TEXT_LIGHT.ToBrush(), Focusable = false };
            {
                var menuItem = new MenuItem().
                    SetName("Play").
                    SetAction(ChangePlayState).
                    SetInputGesture(new(Key.Space));
                AudioEngine.PlayStateChanged += () =>
                {
                    menuItem.Header = AudioEngine.IsPlaying ? "Pause" : "Play";
                };
                menuBarItem.Items.Add(menuItem);
            }
            menu.Items.Add(menuBarItem);
        }

        return menu;
    }

    MenuItem mUndoMenuItem;
    MenuItem mRedoMenuItem;

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
            AudioEngine.Progress += OnAudioEngineProgress;
        }

        ~PlayheadForProject()
        {
            AudioEngine.Progress -= OnAudioEngineProgress;
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
