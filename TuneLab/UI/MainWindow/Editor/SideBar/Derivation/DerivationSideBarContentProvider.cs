using System;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TuneLab.Data;
using TuneLab.Extensions.Derivers;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;
using CheckBox = Avalonia.Controls.CheckBox;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;

namespace TuneLab.UI;

// 中央派生任务面板（权威面）：列出全部派生任务（运行中 / 待应用 / 失败），逐条 status + 动作
//（运行中→取消；待应用→应用 / 丢弃；失败→丢弃）。位置无关，天然扛住多任务 / part 移动 / part 删除。
// 数据源 = DerivationTaskManager（会话态、不持久）；订阅其 Changed 重建行。
internal sealed class DerivationSideBarContentProvider
{
    public IImage Icon => Assets.AutoPage.GetImage(Style.LIGHT_WHITE);
    public string Name => "Derivation".Tr(TC.Menu);
    public Control Root => mRoot;

    readonly StackPanel mList;
    readonly Control mRoot;
    IProject? mProject;

    public DerivationSideBarContentProvider()
    {
        mList = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Margin = new(12) };
        mRoot = new ScrollViewer { Content = mList, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Background = Style.INTERFACE.ToBrush() };
        DerivationTaskManager.Changed.Subscribe(Rebuild);
        Rebuild();
    }

    public void SetProject(IProject? project) => mProject = project;

    void Rebuild()
    {
        mList.Children.Clear();
        if (DerivationTaskManager.Tasks.Count == 0)
        {
            mList.Children.Add(new TextBlock
            {
                Text = "No derivation tasks.".Tr(TC.Menu),
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                FontSize = 12,
                Margin = new(0, 8, 0, 0),
            });
            return;
        }

        // 快照迭代：动作会修改任务列表（应用/丢弃移除条目）。
        foreach (var task in System.Linq.Enumerable.ToArray(DerivationTaskManager.Tasks))
            mList.Children.Add(BuildRow(task));
    }

    Control BuildRow(DerivationTask task)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new(10) };
        panel.Children.Add(new TextBlock { Text = task.TaskLabel, Foreground = Style.TEXT_LIGHT.ToBrush(), FontSize = 13, FontWeight = FontWeight.Bold });

        switch (task.State)
        {
            case DerivationTaskState.Running:
                panel.Children.Add(StatusText(FormatRunning(task)));
                panel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = task.Progress, Height = 4 });
                panel.Children.Add(Actions((("Cancel".Tr(TC.Dialog), false, () => DerivationTaskManager.Cancel(task)))));
                break;

            case DerivationTaskState.PendingApply:
            {
                panel.Children.Add(StatusText("Ready to apply".Tr(TC.Menu)));
                CheckBox? tempoCheck = null;
                if (task.Result is { } r && (r.Tempos is { Count: > 0 } || r.TimeSignatures is { Count: > 0 }))
                {
                    tempoCheck = new CheckBox { Content = "Apply detected tempo / time signature".Tr(TC.Menu), Foreground = Style.LIGHT_WHITE.ToBrush(), FontSize = 11 };
                    panel.Children.Add(tempoCheck);
                }
                panel.Children.Add(Actions(
                    ("Apply".Tr(TC.Menu), true, () => ApplyTask(task, tempoCheck?.IsChecked == true)),
                    ("Discard".Tr(TC.Menu), false, () => DerivationTaskManager.Discard(task))));
                break;
            }

            case DerivationTaskState.Failed:
                panel.Children.Add(StatusText((("Failed".Tr(TC.Menu)) + ": " + (task.Message ?? "")), error: true));
                panel.Children.Add(Actions((("Discard".Tr(TC.Menu), false, () => DerivationTaskManager.Discard(task)))));
                break;
        }

        return new Border
        {
            Background = Style.BACK.Opacity(0.4).ToBrush(),
            CornerRadius = new(6),
            Child = panel,
        };
    }

    void ApplyTask(DerivationTask task, bool applyTimeline)
    {
        if (mProject == null)
            return;
        var options = new DerivedResultApplier.Options
        {
            ApplyDetectedTempo = applyTimeline,
            ApplyDetectedTimeSignature = applyTimeline,
        };
        int newTracks = DerivationTaskManager.Apply(task, mProject, options);
        if (newTracks == 0 && (task.Result?.Tempos == null && task.Result?.TimeSignatures == null))
            Log.Warning("Derivation produced no landable material (no-op).");
    }

    static TextBlock StatusText(string text, bool error = false) => new()
    {
        Text = text,
        Foreground = (error ? Style.SYNTHESIS_FAILED : Style.LIGHT_WHITE.Opacity(0.7)).ToBrush(),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
    };

    static string FormatRunning(DerivationTask task)
    {
        var text = "Running".Tr(TC.Menu);
        if (!string.IsNullOrEmpty(task.Message))
            text = task.Message!;
        return text + string.Format(" ({0}%)", (int)(task.Progress * 100));
    }

    static Control Actions(params (string Text, bool Primary, Action OnClick)[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        foreach (var (text, primary, onClick) in buttons)
        {
            var button = new Button { MinWidth = 64, Height = 26 };
            button.AddContent(new() { Item = new BorderItem() { CornerRadius = 6 }, ColorSet = new() { Color = primary ? Style.BUTTON_PRIMARY : Style.BUTTON_NORMAL, HoveredColor = primary ? Style.BUTTON_PRIMARY_HOVER : Style.BUTTON_NORMAL_HOVER } });
            button.AddContent(new() { Item = new TextItem() { Text = text }, ColorSet = new() { Color = primary ? Colors.White : Style.LIGHT_WHITE } });
            button.Clicked += onClick;
            panel.Children.Add(button);
        }
        return panel;
    }
}
