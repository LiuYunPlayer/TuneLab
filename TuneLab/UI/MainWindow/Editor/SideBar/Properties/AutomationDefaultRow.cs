using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.UI;

// 一条自动化「默认值」编辑行（标题 + slider）：voice 默认值面板与各 effect 块共用。
// 自含两条定制语义：① 拖动时合并脏标记（BeginMergeDirty/EndMergeDirty）避免每帧重合成；
// ② 编辑某条尚不存在的自动化时按需 AddAutomation（经 AutomationKey 路由 voice/effect）。
// 外部（undo/redo/preset）改默认值由容器订阅后调 Refresh()。
//
// 支持多 part（同引擎多选）：值三态合并（各 part 默认值全等显该值、否则 "-"），编辑扇出到所有 part——
// 各 part 按需懒建轨、共享文档撤销根（DiscardTo/Commit 走首 part 即作用全文档），整段编辑归一个撤销单元。
// 单 part 即 N=1 特例（effect 块仍用单 part 构造）。
internal sealed class AutomationDefaultRow : StackPanel, IDisposable
{
    public AutomationDefaultRow(IMidiPart part, AutomationKey key, string keyName, AutomationConfig config)
        : this(new[] { part }, key, keyName, config) { }

    public AutomationDefaultRow(IReadOnlyList<IMidiPart> parts, AutomationKey key, string keyName, AutomationConfig config)
    {
        mParts = parts;
        mKey = key;
        mConfig = config;
        Orientation = Orientation.Vertical;

        Children.Add(new Label()
        {
            Height = 30,
            FontSize = 12,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            Content = keyName,
            Padding = new(24, 0),
        });

        mSlider = new SliderController() { Margin = new(24, 12) };
        mSlider.SetScale(config.Scale);
        mSlider.NumberFormat = config.Format;
        mSlider.SetDefaultValue(config.DefaultValue);
        mSlider.ShowRandomButton = config.Randomizable;
        mSlider.SetBoundLabels(config.MinLabel, config.MaxLabel);
        Children.Add(mSlider);

        Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });

        mSlider.ValueWillChange.Subscribe(OnValueWillChange, s);
        mSlider.ValueChanged.Subscribe(OnValueChanged, s);
        mSlider.ValueCommitted.Subscribe(OnValueCommitted, s);

        Refresh();
    }

    public void Refresh()
    {
        if (mParts.Count == 0)
        {
            mSlider.DisplayNull();
            return;
        }
        // 三态：各 part 当前默认值（轨不存在取 config 默认）全等显该值，否则 "-"。
        var first = ValueOf(mParts[0]);
        for (int i = 1; i < mParts.Count; i++)
        {
            if (ValueOf(mParts[i]) != first)
            {
                mSlider.DisplayMultiple();
                return;
            }
        }
        mSlider.Display(first);
    }

    double ValueOf(IMidiPart part) => part.GetEffectiveAutomation(mKey)?.DefaultValue.Value ?? mConfig.DefaultValue;

    void OnValueWillChange()
    {
        // 各 part：合成批量（拖动期间不每帧重合成）+ 懒建一次轨 + 对其默认值开通知 merge（拖动只发可忽略中间态、不发结果态，
        // 否则外部刷新订阅会在拖动中回写 slider 与拖动打架）。mHead 在建轨之后捕获：DiscardTo 只回退本次拖动写值、不回退建轨。
        foreach (var part in mParts)
        {
            part.BeginMergeDirty();
            var automation = part.GetEffectiveAutomation(mKey) ?? part.AddEffectiveAutomation(mKey);
            automation?.DefaultValue.BeginMergeNotify();
        }
        mMerging = true;
        mHead = mParts.Count > 0 ? mParts[0].Head : default;
    }

    void OnValueChanged()
    {
        if (mParts.Count == 0)
            return;
        // 全 part 共享同一文档撤销栈：DiscardTo(首 part Head) 回退该 head 之后全文档的拖动写值，再把新值扇出到各 part。
        mParts[0].DiscardTo(mHead);
        foreach (var part in mParts)
            part.GetEffectiveAutomation(mKey)?.DefaultValue.Set(mSlider.Value);
    }

    void OnValueCommitted()
    {
        OnValueChanged();
        EndMerge();
        foreach (var part in mParts)
            part.EndMergeDirty();
        if (mParts.Count > 0)
            mParts[0].Commit();
    }

    // 退出通知 merge（幂等）：仅在已进入时 EndMergeNotify，避免无配对多发 / merge 计数泄漏（含编辑中途被释放的兜底）。
    void EndMerge()
    {
        if (!mMerging)
            return;
        mMerging = false;
        foreach (var part in mParts)
            part.GetEffectiveAutomation(mKey)?.DefaultValue.EndMergeNotify();
    }

    public void Dispose()
    {
        EndMerge();
        s.DisposeAll();
    }

    readonly IReadOnlyList<IMidiPart> mParts;
    readonly AutomationKey mKey;
    readonly AutomationConfig mConfig;
    readonly SliderController mSlider;
    Head mHead;
    bool mMerging;
    readonly DisposableManager s = new();
}
