using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.I18N;

namespace TuneLab.Data;

internal class ProjectDocument : DataDocument
{
    public IActionEvent ProjectNameChanged => mProjectNameChanged;
    public IProvider<Project> ProjectProvider => mProject;
    public Project? Project => mProject;
    public string Name => mName;
    public bool IsSaved => mLastSavedHead == Head;
    public string Path => mPath;
    public ProjectDocument() 
    {
        mLastSavedHead = Head;
        mProject.ObjectWillChange.Subscribe(() =>
        {
            Project?.Detach();
            Project?.Dispose();
        });

        mProject.ObjectChanged.Subscribe(() =>
        {
            Project?.Attach(this);
        });

        mProject.When(project => project.Tracks.ItemAdded).Subscribe(track =>
        {
            var dir = AudioPartBaseDirectory();
            if (string.IsNullOrEmpty(dir))
                return;

            foreach (var audioPart in track.Parts.OfType<IAudioPart>())
            {
                audioPart.BaseDirectory.Value = dir;
            }
        });

        mProject.When(project => project.Tracks.Any(track => track.Parts.ItemAdded)).Subscribe(part =>
        {
            if (part is not IAudioPart audioPart)
                return;

            var dir = AudioPartBaseDirectory();
            if (string.IsNullOrEmpty(dir))
                return;

            audioPart.BaseDirectory.Value = dir;
        });
    }

    public void SetProject(Project project, string path = "")
    {
        Clear();
        mProject.Set(project);
        SetSavePath(path);
    }

    public void SetSavePath(string path)
    {
        mPath = path;
        ResetAudioPartBaseDirectory();
        mName = File.Exists(path) ? new FileInfo(path).Name : "Untitled Project".Tr(TC.Document);
        mLastSavedHead = Head;
        mProjectNameChanged?.Invoke();
    }

    string? AudioPartBaseDirectory()
    {
        if (Project == null)
            return null;

        return System.IO.Path.GetDirectoryName(mPath);
    }

    void ResetAudioPartBaseDirectory()
    {
        if (Project == null)
            return;

        var dir = AudioPartBaseDirectory();
        if (string.IsNullOrEmpty(dir))
            return;

        foreach (var audioPart in Project.AllAudioParts())
        {
            audioPart.BaseDirectory.Value = dir;
        }
    }

    string mPath = string.Empty;
    string mName = string.Empty;
    Head mLastSavedHead;
    readonly Owner<Project> mProject = new();
    readonly ActionEvent mProjectNameChanged = new();
}
