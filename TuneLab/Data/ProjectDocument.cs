using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.I18N;

namespace TuneLab.Data;

internal class ProjectDocument : DataDocument
{
    public IActionEvent ProjectNameChanged => mProjectNameChanged;
    public IHolder<Project> ProjectHolder => mProject;
    public Project? Project => mProject;
    public string Name => mName;
    public bool IsSaved => mLastSavedHead == Head;
    public string Path => mPath;
    // 非空 = 当前工程来自崩溃恢复，且它当时所在的文件仍然存在 → 可以「存回原位」。
    // 恢复本身不绑这个路径（见 SetRecovered），要不要覆盖原文件由用户显式决定。
    public string? RecoveredOriginalPath => mRecoveredOriginalPath;
    public ProjectDocument() 
    {
        mLastSavedHead = Head;
        mProject.WillModify.Subscribe(() =>
        {
            Project?.Detach();
            Project?.Dispose();
        });

        mProject.Modified.Subscribe(() =>
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

        mProject.When(project => project.Tracks.WhenAny(track => track.Parts.ItemAdded)).Subscribe(part =>
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
        // 有了真实保存路径就回到"从保存路径推基准目录"，清掉显式来源——否则内存里的解析基准会与
        // "把这个文件重新打开一次"所得的基准不一致，成为一处看不见的分歧。
        mBaseDirectoryOverride = null;
        mRecoveredOriginalPath = null;
        ResetAudioPartBaseDirectory();
        mName = File.Exists(path) ? new FileInfo(path).Name : "Untitled Project".Tr(TC.Document);
        mLastSavedHead = Head;
        mProjectNameChanged?.Invoke();
    }

    // 崩溃恢复专用：工程【保持未保存态】（无保存路径，故一次 Ctrl+S 不会覆盖原文件），
    // 但展示名与解析基准来自自动保存的元数据。originalPath 为空（工程从未保存过）时不设基准，
    // 相对音频引用无法解析——那是"未保存工程没有基准目录"的必然结果，如实降级不猜测。
    public void SetRecovered(string displayName, string? originalPath)
    {
        mPath = string.Empty;
        mRecoveredOriginalPath = File.Exists(originalPath) ? originalPath : null;
        mBaseDirectoryOverride = mRecoveredOriginalPath == null
            ? null
            : System.IO.Path.GetDirectoryName(mRecoveredOriginalPath);
        ResetAudioPartBaseDirectory();
        mName = displayName;
        mLastSavedHead = Head;
        mProjectNameChanged?.Invoke();
    }

    // 「保存路径」与「基准目录」是两回事：崩溃恢复就是"有基准、无保存路径"的合法组合。
    // 取值规则 = 显式来源优先，缺省才从保存路径推。
    string? AudioPartBaseDirectory()
    {
        if (Project == null)
            return null;

        return mBaseDirectoryOverride ?? System.IO.Path.GetDirectoryName(mPath);
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
    string? mBaseDirectoryOverride = null;
    string? mRecoveredOriginalPath = null;
    Head mLastSavedHead;
    readonly Holder<Project> mProject = new();
    readonly ActionEvent mProjectNameChanged = new();
}
