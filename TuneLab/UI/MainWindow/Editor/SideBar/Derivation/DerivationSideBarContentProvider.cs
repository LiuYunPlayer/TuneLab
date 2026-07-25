using System.Collections.Generic;
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

// 派生记录面板（记录模型）：列出工程内各音频 part 的持久派生记录，逐条按状态呈现 + 动作。
// 一条记录的状态由「是否有在飞任务」+「缓存是否命中」共同解析：
//   排队 / 运行中 / 失败（有在飞任务）· 可应用（无任务且缓存命中）· 已失效（无任务且缓存缺失，源在可重跑）。
// 数据源 = 各 part 的持久记录账本（DerivationRecords）+ DerivationTaskManager 的在飞任务；订阅 Manager.Changed 重建。
//
// 【过渡形态】：stage③ 将重设计为「一级按 part 分组、组内按 StartTimestamp 排、删除『This cannot be undone』」的记录管理器。
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

    public void SetProject(IProject? project)
    {
        mProject = project;
        Rebuild();
    }

    void Rebuild()
    {
        mList.Children.Clear();

        // 在飞任务按 (源, 缓存键) 索引，供记录解析其运行时态。
        var liveTasks = new Dictionary<(IAudioPart, string), DerivationTask>();
        foreach (var task in DerivationTaskManager.Tasks)
            liveTasks[(task.Source, task.CacheKey)] = task;

        int rows = 0;
        if (mProject != null)
        {
            foreach (var track in mProject.Tracks)
                foreach (var part in track.Parts)
                {
                    if (part is not IAudioPart audioPart)
                        continue;
                    foreach (var kvp in audioPart.DerivationRecords)
                    {
                        liveTasks.TryGetValue((audioPart, kvp.Key), out var task);
                        mList.Children.Add(BuildRow(audioPart, kvp.Key, kvp.Value, task));
                        rows++;
                    }
                }
        }

        if (rows == 0)
        {
            mList.Children.Add(new TextBlock
            {
                Text = "No derivation records.".Tr(TC.Menu),
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                FontSize = 12,
                Margin = new(0, 8, 0, 0),
            });
        }
    }

    Control BuildRow(IAudioPart source, string cacheKey, DerivationRecordInfo record, DerivationTask? task)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new(10) };
        var title = string.IsNullOrEmpty(record.Label) ? record.EngineDisplayName : record.Label;
        panel.Children.Add(new TextBlock { Text = title, Foreground = Style.TEXT_LIGHT.ToBrush(), FontSize = 13, FontWeight = FontWeight.Bold });

        if (task != null)
        {
            switch (task.State)
            {
                case DerivationTaskState.Queued:
                    panel.Children.Add(StatusText("Queued".Tr(TC.Menu)));
                    panel.Children.Add(Actions(("Cancel".Tr(TC.Dialog), false, () => DerivationTaskManager.Cancel(task))));
                    break;
                case DerivationTaskState.Running:
                    panel.Children.Add(StatusText(FormatRunning(task)));
                    panel.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = task.Progress, Height = 4 });
                    panel.Children.Add(Actions(("Cancel".Tr(TC.Dialog), false, () => DerivationTaskManager.Cancel(task))));
                    break;
                case DerivationTaskState.Failed:
                    panel.Children.Add(StatusText("Failed".Tr(TC.Menu) + ": " + (task.Message ?? ""), error: true));
                    panel.Children.Add(Actions(
                        // 先消解该失败任务再重跑，避免同 (源, 键) 残留失败任务与新任务并存。
                        ("Retry".Tr(TC.Menu), true, () => { DerivationTaskManager.DiscardFailed(task); ReRun(source, record); }),
                        ("Dismiss".Tr(TC.Menu), false, () => DerivationTaskManager.DiscardFailed(task))));
                    break;
            }
        }
        else if (AudioDerivationCacheManager.Contains(cacheKey))
        {
            panel.Children.Add(StatusText("Ready to apply".Tr(TC.Menu)));
            var tempoCheck = new CheckBox { Content = "Apply detected tempo / time signature".Tr(TC.Menu), Foreground = Style.LIGHT_WHITE.ToBrush(), FontSize = 11 };
            panel.Children.Add(tempoCheck);
            panel.Children.Add(Actions(
                ("Apply".Tr(TC.Menu), true, () => ApplyRecord(source, cacheKey, tempoCheck.IsChecked == true)),
                ("Delete".Tr(TC.Menu), false, () => DerivationTaskManager.DeleteRecord(source, cacheKey))));
        }
        else
        {
            // 缓存缺失（换机 / 淘汰 / 未跑完就存了工程）：已失效，源在可重跑。
            panel.Children.Add(StatusText("Cache unavailable (invalidated)".Tr(TC.Menu)));
            panel.Children.Add(Actions(
                ("Re-run".Tr(TC.Menu), true, () => ReRun(source, record)),
                ("Delete".Tr(TC.Menu), false, () => DerivationTaskManager.DeleteRecord(source, cacheKey))));
        }

        return new Border
        {
            Background = Style.BACK.Opacity(0.4).ToBrush(),
            CornerRadius = new(6),
            Child = panel,
        };
    }

    void ApplyRecord(IAudioPart source, string cacheKey, bool applyTimeline)
    {
        if (mProject == null)
            return;
        var options = new DerivedResultApplier.Options
        {
            ApplyDetectedTempo = applyTimeline,
            ApplyDetectedTimeSignature = applyTimeline,
        };
        var result = DerivationTaskManager.Apply(source, cacheKey, mProject, options);
        if (!result.CacheAvailable)
        {
            Log.Warning("Derivation cache unavailable at apply time (invalidated); re-run required.");
            Rebuild();   // 缓存刚被淘汰 => 该条转「已失效」，刷新面板
        }
        else if (result.NewTrackCount == 0)
        {
            Log.Warning("Derivation produced no landable material (no-op).");
        }
    }

    void ReRun(IAudioPart source, DerivationRecordInfo record)
        => DerivationTaskManager.Submit(source, record.EngineId, record.EngineDisplayName, record.Label, record.Parameters);

    static TextBlock StatusText(string text, bool error = false) => new()
    {
        Text = text,
        Foreground = (error ? Style.SYNTHESIS_FAILED : Style.LIGHT_WHITE.Opacity(0.7)).ToBrush(),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
    };

    static string FormatRunning(DerivationTask task)
    {
        var text = string.IsNullOrEmpty(task.Message) ? "Running".Tr(TC.Menu) : task.Message!;
        return text + string.Format(" ({0}%)", (int)(task.Progress * 100));
    }

    static Control Actions(params (string Text, bool Primary, System.Action OnClick)[] buttons)
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
