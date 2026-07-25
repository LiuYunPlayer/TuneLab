using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
using CheckBox = TuneLab.GUI.Components.CheckBox;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;

namespace TuneLab.UI;

// 派生记录管理器（记录模型的权威呈现面）：一级按音频 part 分组、组内按 StartTimestamp 升序，逐条记录按状态呈现。
// 状态由 DerivationTaskManager.ResolveStatus 解析：排队/运行中/失败（有在飞任务）· 可应用（无任务且缓存命中）· 已失效（无任务且缓存缺失）。
// 无 Active/Records 分段、无孤儿组（记录随其 part 存亡）。删除记录非撤销，走「This cannot be undone」确认。
//
// 卡片：标题 + 触发时间 + 状态常驻；动作按钮（Apply/Delete/…）悬浮或展开时【浮层覆盖】在卡片右下角（不撑高 item）；
// 点头部展开另显详情（入参 + 产物摘要 + 可应用态的「同时套用速度/拍号」勾选框）。可应用卡片的结果懒加载。
//
// 高频重建防抖：运行中任务每进度 tick 都触发 Changed；若整列重建会打断正在悬浮的卡片（浮层/hover 丢失）。
// 故订阅 Changed 先做【构成签名】比对（记录集 + 各记录的在飞任务态，不含进度）：构成未变只【原地刷新】运行卡进度，不重建控件树。
internal sealed class DerivationSideBarContentProvider
{
    public IImage Icon => Assets.Derive.GetImage(Style.LIGHT_WHITE);
    public string Name => "Derivation".Tr(TC.Menu);
    public Control Root => mRoot;

    readonly StackPanel mList;
    readonly ScrollViewer mRoot;
    IProject? mProject;
    readonly Dictionary<IAudioPart, Control> mGroups = new();
    readonly List<Action> mRunningRefreshers = new();   // 运行卡进度原地刷新器（构成未变时调，避免重建）
    string mSignature = "";

    public DerivationSideBarContentProvider()
    {
        mList = new StackPanel { Orientation = Orientation.Vertical, Spacing = 12, Margin = new(12) };
        mRoot = new ScrollViewer { Content = mList, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Background = Style.BACK.ToBrush() };
        DerivationTaskManager.Changed.Subscribe(OnChanged);
        Rebuild();
    }

    public void SetProject(IProject? project)
    {
        mProject = project;
        Rebuild();
    }

    public void ScrollToPart(IAudioPart part)
    {
        if (mGroups.TryGetValue(part, out var group))
            group.BringIntoView();
    }

    // 构成未变（仅进度变）=> 原地刷新运行卡；构成变（增删记录/任务态跃迁/完成）=> 整列重建。
    void OnChanged()
    {
        if (ComputeSignature() == mSignature)
        {
            foreach (var refresh in mRunningRefreshers)
                refresh();
            return;
        }
        Rebuild();
    }

    // 构成签名：记录集 + 各记录在飞任务态（不含进度）。进度 tick 不改签名 => 不重建。
    string ComputeSignature()
    {
        if (mProject == null)
            return "";
        var taskState = new Dictionary<(IAudioPart, string), DerivationTaskState>();
        foreach (var task in DerivationTaskManager.Tasks)
            taskState[(task.Source, task.CacheKey)] = task.State;

        var sb = new StringBuilder();
        foreach (var track in mProject.Tracks)
            foreach (var part in track.Parts)
            {
                if (part is not IAudioPart audioPart || audioPart.DerivationRecords.Count == 0)
                    continue;
                sb.Append('|');
                foreach (var kvp in audioPart.DerivationRecords.OrderBy(k => k.Value.StartTimestamp))
                {
                    sb.Append(kvp.Key);
                    sb.Append(taskState.TryGetValue((audioPart, kvp.Key), out var st) ? (char)('a' + (int)st) : 'x');
                    sb.Append(',');
                }
            }
        return sb.ToString();
    }

    void Rebuild()
    {
        mList.Children.Clear();
        mGroups.Clear();
        mRunningRefreshers.Clear();

        int groups = 0;
        if (mProject != null)
        {
            foreach (var track in mProject.Tracks)
                foreach (var part in track.Parts)
                {
                    if (part is not IAudioPart audioPart || audioPart.DerivationRecords.Count == 0)
                        continue;
                    var group = BuildGroup(audioPart);
                    mGroups[audioPart] = group;
                    mList.Children.Add(group);
                    groups++;
                }
        }

        if (groups == 0)
        {
            mList.Children.Add(new TextBlock
            {
                Text = "No derivation records.".Tr(TC.Menu),
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                FontSize = 12,
                Margin = new(0, 8, 0, 0),
            });
        }

        mSignature = ComputeSignature();
    }

    Control BuildGroup(IAudioPart part)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6 };
        panel.Children.Add(new TextBlock
        {
            Text = part.Name.Value,
            Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(),
            FontSize = 11,
            FontWeight = FontWeight.Bold,
            Margin = new(2, 0, 0, 2),
        });

        foreach (var kvp in part.DerivationRecords.OrderBy(kvp => kvp.Value.StartTimestamp))
            panel.Children.Add(BuildRow(part, kvp.Key, kvp.Value));

        return panel;
    }

    Control BuildRow(IAudioPart source, string cacheKey, DerivationRecordInfo record)
    {
        var status = DerivationTaskManager.ResolveStatus(source, cacheKey, out var task);

        // ── 内容层：头部（标题+时间+状态，点击展开）+ 详情（展开才显）──
        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new(10) };

        var header = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, Background = Colors.Transparent.ToBrush() };
        header.Children.Add(new TextBlock { Text = DisplayTitle(record), Foreground = Style.TEXT_LIGHT.ToBrush(), FontSize = 13, FontWeight = FontWeight.Bold });
        header.Children.Add(new TextBlock { Text = FormatTimestamp(record.StartTimestamp), Foreground = Style.LIGHT_WHITE.Opacity(0.45).ToBrush(), FontSize = 10 });
        var statusLine = BuildStatusLine(status, task);   // 可应用态返回 null（历史永久、无「待应用」瞬态）
        if (statusLine != null)
            header.Children.Add(statusLine);
        content.Children.Add(header);

        var detailsPanel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4, IsVisible = false, Margin = new(0, 2, 0, 0) };
        detailsPanel.Children.Add(DetailLabel("Parameters".Tr(TC.Menu)));
        detailsPanel.Children.Add(DetailText(FormatParameters(record.Parameters)));
        content.Children.Add(detailsPanel);

        // ── 浮层：仅主动作按钮浮在卡片右下角（无暗色底框、不撑高 item；hover/展开时显）。Delete 走右键菜单 ──
        var actions = new StackPanel
        {
            Orientation = Orientation.Vertical, Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new(0, 0, 8, 8), IsVisible = false,
        };
        if (status != DerivationRecordStatus.Applicable)
            BuildPrimary(actions, source, cacheKey, record, status, task);   // 非可应用态不依赖结果，直接建

        bool expanded = false, hovered = false, loaded = false;

        void EnsureLoaded()
        {
            if (loaded)
                return;
            loaded = true;
            if (status != DerivationRecordStatus.Applicable)
                return;
            if (AudioDerivationCacheManager.TryGet(cacheKey, out var result))
                BuildApplicable(actions, detailsPanel, source, cacheKey, result);
            else
                BuildPrimary(actions, source, cacheKey, record, DerivationRecordStatus.Invalidated, null);   // 缓存刚失效
        }

        void UpdateReveal()
        {
            bool reveal = hovered || expanded;
            if (reveal)
                EnsureLoaded();
            actions.IsVisible = reveal;
            detailsPanel.IsVisible = expanded;
        }

        var stack = new Panel();
        stack.Children.Add(content);
        stack.Children.Add(actions);

        var card = new Border
        {
            Background = Style.INTERFACE.ToBrush(),
            CornerRadius = new(6),
            Child = stack,
        };
        // 可删除态（非在飞）：Delete 走右键菜单，不占 item。
        if (status is DerivationRecordStatus.Failed or DerivationRecordStatus.Applicable or DerivationRecordStatus.Invalidated)
        {
            var menu = new Avalonia.Controls.ContextMenu();
            menu.Items.Add(new MenuItem().SetName("Delete".Tr(TC.Menu)).SetAction(() => ConfirmDelete(source, cacheKey, DisplayTitle(record))));
            card.ContextMenu = menu;
        }
        card.PointerEntered += (_, _) => { hovered = true; UpdateReveal(); };
        card.PointerExited += (_, _) => { hovered = false; UpdateReveal(); };
        // 展开切换只挂头部 + 仅左键（右键要弹删除菜单、中键不该触发）：点浮层按钮也不会误触（在 actions 层、不冒泡到此）。
        header.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(header).Properties.IsLeftButtonPressed)
                return;
            expanded = !expanded;
            UpdateReveal();
        };
        return card;
    }

    // 主动作按钮（单个，非在飞态不依赖结果）。Delete 走右键菜单、不在此。
    void BuildPrimary(StackPanel host, IAudioPart source, string cacheKey, DerivationRecordInfo record, DerivationRecordStatus status, DerivationTask? task)
    {
        host.Children.Clear();
        switch (status)
        {
            case DerivationRecordStatus.Queued:
            case DerivationRecordStatus.Running:
                host.Children.Add(Actions(("Cancel".Tr(TC.Dialog), false, () => DerivationTaskManager.Cancel(task!))));
                break;
            case DerivationRecordStatus.Failed:
                host.Children.Add(Actions(("Retry".Tr(TC.Menu), true, () => { DerivationTaskManager.DiscardFailed(task!); ReRun(source, record); })));
                break;
            case DerivationRecordStatus.Invalidated:
                host.Children.Add(Actions(("Re-run".Tr(TC.Menu), true, () => ReRun(source, record))));
                break;
        }
    }

    // 可应用态（依赖已加载结果）：详情补产物摘要 +（含速度/拍号时）勾选框 / （全空时）不可落地提示；浮层仅 Apply（Delete 走右键）。
    void BuildApplicable(StackPanel actions, StackPanel detailsPanel, IAudioPart source, string cacheKey, DerivedResult result)
    {
        actions.Children.Clear();

        detailsPanel.Children.Add(DetailLabel("Artifacts".Tr(TC.Menu)));
        detailsPanel.Children.Add(DetailText(FormatArtifacts(result)));

        bool hasParts = result.Tracks.Any(t => t.Parts.Count > 0);
        bool hasTimeline = result.Tempos.Count > 0 || result.TimeSignatures.Count > 0;

        if (!hasParts && !hasTimeline)
        {
            detailsPanel.Children.Add(new TextBlock
            {
                Text = "No landable content in this result.".Tr(TC.Menu),
                Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
            });
            return;   // 无可落地内容 => 无 Apply 按钮（删除走右键菜单）
        }

        // 勾选框在展开详情里（浮层只放按钮）；仅当产物含速度/拍号才出现，产物只有速度/拍号（无 part）时默认勾选。
        CheckBox? tempoCheck = null;
        if (hasTimeline)
        {
            tempoCheck = new CheckBox { IsChecked = !hasParts };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(tempoCheck);
            row.Children.Add(new TextBlock
            {
                Text = "Apply detected tempo / time signature".Tr(TC.Menu),
                Foreground = Style.LIGHT_WHITE.ToBrush(),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });
            detailsPanel.Children.Add(row);
        }

        actions.Children.Add(Actions(("Apply".Tr(TC.Menu), true, () => ApplyRecord(source, cacheKey, tempoCheck?.IsChecked == true))));
    }

    void ApplyRecord(IAudioPart source, string cacheKey, bool applyTimeline)
    {
        if (mProject == null)
            return;
        var options = new DerivedResultApplier.Options { ApplyDetectedTempo = applyTimeline, ApplyDetectedTimeSignature = applyTimeline };
        var result = DerivationTaskManager.Apply(source, cacheKey, mProject, options);
        if (!result.CacheAvailable)
        {
            Log.Warning("Derivation cache unavailable at apply time (invalidated); re-run required.");
            Rebuild();
        }
        else if (result.NewTrackCount == 0 && !applyTimeline)
        {
            Log.Warning("Derivation produced no landable material (no-op).");
        }
    }

    void ReRun(IAudioPart source, DerivationRecordInfo record)
        => DerivationTaskManager.Submit(source, record.EngineId, record.EngineDisplayName, record.Label, record.Parameters);

    async void ConfirmDelete(IAudioPart source, string cacheKey, string title)
    {
        var dialog = new Dialog();
        dialog.SetTitle("Delete Derivation Record".Tr(TC.Dialog));
        dialog.SetMessage(string.Format("Delete \"{0}\"? This cannot be undone.".Tr(TC.Dialog), title));
        dialog.AddButton("Cancel".Tr(TC.Dialog), Dialog.ButtonType.Normal);
        dialog.AddButton("Delete".Tr(TC.Dialog), Dialog.ButtonType.Primary).Clicked += () => DerivationTaskManager.DeleteRecord(source, cacheKey);
        await dialog.ShowDialog(mRoot.Window());
    }

    // ── 呈现辅助 ──

    // 状态行（可应用态返回 null——历史永久、无「待应用」瞬态，applicable 由 hover 出现 Apply 表达）。
    // Running：注册进度原地刷新器（构成未变的进度 tick 只更新此处，不重建卡片）。
    Control? BuildStatusLine(DerivationRecordStatus status, DerivationTask? task)
    {
        switch (status)
        {
            case DerivationRecordStatus.Queued:
                return StatusText("Queued".Tr(TC.Menu));
            case DerivationRecordStatus.Running:
            {
                var text = StatusText(FormatRunning(task!));
                var bar = new ProgressBar { Minimum = 0, Maximum = 1, Value = task!.Progress, Height = 4 };
                mRunningRefreshers.Add(() => { text.Text = FormatRunning(task!); bar.Value = task!.Progress; });
                var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
                stack.Children.Add(text);
                stack.Children.Add(bar);
                return stack;
            }
            case DerivationRecordStatus.Failed:
                return StatusText("Failed".Tr(TC.Menu) + ": " + (task!.Message ?? ""), error: true);
            case DerivationRecordStatus.Applicable:
                return null;
            default:
                return StatusText("Cache unavailable (invalidated)".Tr(TC.Menu));
        }
    }

    static string DisplayTitle(DerivationRecordInfo record)
        => string.IsNullOrEmpty(record.Label) ? record.EngineDisplayName : record.Label;

    static string FormatTimestamp(double unixSeconds)
        => unixSeconds <= 0 ? "" : DateTimeOffset.FromUnixTimeSeconds((long)unixSeconds).LocalDateTime.ToString("yyyy-MM-dd HH:mm");

    static string FormatParameters(PropertyObject properties)
    {
        // 用与缓存键/工程同一套 PropertyObject→JSON 转换渲染，值类型（数/串/布尔/嵌套）忠实可读。
        if (PropertyJsonUtils.ToJson(properties) is not Newtonsoft.Json.Linq.JObject json || !json.HasValues)
            return "—";
        return string.Join("\n", json.Properties().Select(p => p.Name + ": " + p.Value.ToString(Newtonsoft.Json.Formatting.None)));
    }

    static string FormatArtifacts(DerivedResult result)
    {
        var parts = result.Tracks.SelectMany(t => t.Parts).ToList();
        int midiParts = parts.OfType<DerivedMidiPart>().Count();
        int notes = parts.OfType<DerivedMidiPart>().Sum(p => p.Notes.Count);
        int audioParts = parts.OfType<DerivedAudioPart>().Count();

        var lines = new List<string> { string.Format("Tracks: {0}".Tr(TC.Menu), result.Tracks.Count) };
        if (midiParts > 0)
            lines.Add(string.Format("MIDI parts: {0} ({1} notes)".Tr(TC.Menu), midiParts, notes));
        if (audioParts > 0)
            lines.Add(string.Format("Audio parts: {0}".Tr(TC.Menu), audioParts));
        if (result.Tempos.Count > 0)
            lines.Add(string.Format("Detected tempo points: {0}".Tr(TC.Menu), result.Tempos.Count));
        if (result.TimeSignatures.Count > 0)
            lines.Add(string.Format("Detected time signatures: {0}".Tr(TC.Menu), result.TimeSignatures.Count));
        if (parts.Count == 0)
            lines.Add("(no parts)".Tr(TC.Menu));
        return string.Join("\n", lines);
    }

    static TextBlock DetailLabel(string text) => new()
    {
        Text = text,
        Foreground = Style.LIGHT_WHITE.Opacity(0.5).ToBrush(),
        FontSize = 10,
        FontWeight = FontWeight.Bold,
    };

    static TextBlock DetailText(string text) => new()
    {
        Text = text,
        Foreground = Style.LIGHT_WHITE.Opacity(0.75).ToBrush(),
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
    };

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
