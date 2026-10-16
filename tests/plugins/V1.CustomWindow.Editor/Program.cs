using System.Drawing;
using System.Text.Json;

namespace TuneLab.TestPlugins.V1CustomWindow.Editor;

// 插件自己的设置界面（独立进程）：自己布局、自己管数据，与 TuneLab 进程完全无关。
// 命令行：--data <json 路径>（读写的数据文件）。点 Save 写回数据文件，退出码 0 = 已保存、1 = 取消。
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        string? dataPath = ArgValue(args, "--data");
        if (dataPath == null)
            return 2;

        ApplicationConfiguration.Initialize();
        using var form = new EditorForm(dataPath) { StartPosition = FormStartPosition.CenterScreen };
        return form.ShowDialog() == DialogResult.OK ? 0 : 1;
    }

    static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

// 数据：exe 与插件约定的 JSON（这是作者自己的私有格式，宿主不读不写）。
sealed class EditorData
{
    public string Name { get; set; } = "default";
    public int Level { get; set; } = 3;
    public bool Enabled { get; set; } = true;
}

sealed class EditorForm : Form
{
    public EditorForm(string dataPath)
    {
        var data = ReadData(dataPath);
        Text = "V1 Custom Window (separate process)";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = true;   // 独立程序：在任务栏有自己的入口
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var name = new TextBox { Text = data.Name, Width = 220 };
        var level = new TrackBar { Minimum = 0, Maximum = 10, Value = Math.Clamp(data.Level, 0, 10), Width = 220, TickFrequency = 1 };
        var enabled = new CheckBox { Text = "Enabled", Checked = data.Enabled, AutoSize = true };
        var hint = new Label
        {
            Text = "This is the plugin's own exe (WinForms, its own process).\nLayout and data (" + Path.GetFileName(dataPath) + ") never go through TuneLab.",
            AutoSize = true,
            ForeColor = Color.DimGray,
        };

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        grid.Controls.Add(new Label { Text = "Name", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        grid.Controls.Add(name, 1, 0);
        grid.Controls.Add(new Label { Text = "Level", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        grid.Controls.Add(level, 1, 1);
        grid.Controls.Add(enabled, 1, 2);
        grid.Controls.Add(hint, 0, 3);
        grid.SetColumnSpan(hint, 2);

        var save = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += (_, _) => File.WriteAllText(dataPath, JsonSerializer.Serialize(new EditorData { Name = name.Text, Level = level.Value, Enabled = enabled.Checked }));
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Bottom };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);

        var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        root.Controls.Add(grid, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
        AcceptButton = save;
        CancelButton = cancel;
    }

    static EditorData ReadData(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<EditorData>(File.ReadAllText(path)) ?? new() : new();
        }
        catch
        {
            return new();
        }
    }
}
