using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;
using TuneLab.Base.Properties;
using TuneLab.Utils;

namespace TuneLab.GUI.Controllers;

internal class PathInput : DockPanel, IDataValueController<string>
{
    public PickerOptions Options { get; set; } = new FilePickerOpenOptions();

    public IActionEvent ValueWillChange => ((IDataValueController<string>)mTextInput).ValueWillChange;
    public IActionEvent ValueChanged => ((IValueController<string>)mTextInput).ValueChanged;
    public IActionEvent ValueCommited => ((IValueController<string>)mTextInput).ValueCommited;

    public string Value => ((IValueController<string>)mTextInput).Value;

    public PathInput()
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
