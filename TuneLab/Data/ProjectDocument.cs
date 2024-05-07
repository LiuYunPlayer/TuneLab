using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;

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
    }

    public void SetProject(Project project, string path = "")
    {
        Clear();
        SetSavePath(path);
        mProject.Set(project);
    }

    public void SetSavePath(string path)
    {
        mPath = path;
        mName = File.Exists(path) ? new FileInfo(path).Name : "Untitled Project";
        mLastSavedHead = Head;
        mProjectNameChanged?.Invoke();
    }

    string mPath = string.Empty;
    string mName = string.Empty;
    Head mLastSavedHead;
    readonly Owner<Project> mProject = new();
    readonly ActionEvent mProjectNameChanged = new();
}
