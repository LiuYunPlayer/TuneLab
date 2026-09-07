using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.GUI.Controllers;

internal class PathPicker : DockPanel, IDataValueController<string>
{
    public PickerOptions Options { get; set; } = new FilePickerOpenOptions();

    public IActionEvent ValueWillChange => ((IDataValueController<string>)mTextInput).ValueWillChange;
    public IActionEvent ValueChanged => ((IValueController<string>)mTextInput).ValueChanged;
    public IActionEvent ValueCommitted => ((IValueController<string>)mTextInput).ValueCommitted;

    public string Value => ((IValueController<string>)mTextInput).Value;

    public PathPicker()
    {
        var button = new Components.Button() { Width = 28, Height = 28, Margin = new(12, 0, 0, 0) }.
            AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BACK } }).
            AddContent(new() { Item = new TextItem() { Text = "..." }, ColorSet = new() { Color = Colors.White } });

        button.Clicked += async () =>
        {
            if (Options is FilePickerOpenOptions filePickerOpenOptions)
            {
                var file = await this.OpenFile(filePickerOpenOptions);
                if (file == null)
                    return;

                mTextInput.Value = file;
            }
            else if (Options is FolderPickerOpenOptions folderPickerOpenOptions)
            {
                var file = await this.OpenFolder(folderPickerOpenOptions);
                if (file == null)
                    return;

                mTextInput.Value = file;
            }
        };

        this.AddDock(button, Dock.Right);
        this.AddDock(mTextInput);
    }

    // SDK 的 PathPickerConfig → Avalonia 选择器参数（SDK 不引 Avalonia，这层转换归宿主）。
    // Target 走 default 兜底按文件处理：将来 SDK 加了新目标成员，老宿主退化成文件选择器而不是崩。
    public void Apply(PathPickerConfig config)
    {
        if (config.Target == PathPickerTarget.Folder && config.FileTypes.Count > 0)
            WarnIgnoredFileTypes(config);

        Options = config.Target switch
        {
            PathPickerTarget.Folder => new FolderPickerOpenOptions() { Title = config.PickerTitle },
            _ => new FilePickerOpenOptions()
            {
                Title = config.PickerTitle,
                // 空 = 不过滤（系统选择器默认允许任意文件）。
                FileTypeFilter = config.FileTypes.Count == 0
                    ? null
                    : [.. config.FileTypes.Select(t => new FilePickerFileType(t.Name) { Patterns = [.. t.Patterns] })],
            },
        };
    }

    // 文件夹选择器没有文件类型下拉（Avalonia 的 FolderPickerOpenOptions 根本没有这个属性），故 FileTypes
    // 在 Folder 目标下被忽略。忽略得静悄悄，插件作者就以为自己的过滤器生效了——所以在日志里说一声。
    // 按过滤器名去重：Apply 在每次面板 reconcile（用户每改一格值）都会跑，不去重会把日志刷满。
    static void WarnIgnoredFileTypes(PathPickerConfig config)
    {
        var names = string.Join(", ", config.FileTypes.Select(t => t.Name));
        if (!sWarnedFileTypes.Add(names))
            return;
        Log.Warning(string.Format(
            "PathPickerConfig declares file types ({0}) but targets a folder; folder pickers have no file-type filter, so they are ignored.", names));
    }

    static readonly HashSet<string> sWarnedFileTypes = new(StringComparer.Ordinal);

    SingleLineTextController mTextInput = new();

    public void Display(string value)
    {
        ((IValueController<string>)mTextInput).Display(value);
    }

    public void DisplayNull()
    {
        ((IValueController<string>)mTextInput).DisplayNull();
    }

    public void DisplayMultiple()
    {
        ((IValueController<string>)mTextInput).DisplayMultiple();
    }
}
