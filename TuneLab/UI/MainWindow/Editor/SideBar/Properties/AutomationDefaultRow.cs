using System;
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
internal sealed class AutomationDefaultRow : StackPanel, IDisposable
{
    public AutomationDefaultRow(IMidiPart part, AutomationKey key, string keyName, AutomationConfig config)
    {
        mPart = part;
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
        mSlider.SetRange(config.MinValue, config.MaxValue);
        mSlider.SetDefaultValue(config.DefaultValue);
        Children.Add(mSlider);

        Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });

        mSlider.ValueWillChange.Subscribe(OnValueWillChange, s);
        mSlider.ValueChanged.Subscribe(OnValueChanged, s);
        mSlider.ValueCommitted.Subscribe(OnValueCommitted, s);

        Refresh();
    }

    public void Refresh()
    {
        mSlider.Display(mPart.GetEffectiveAutomation(mKey)?.DefaultValue.Value ?? mConfig.DefaultValue);
    }

    void OnValueWillChange()
    {
        mPart.BeginMergeDirty();    // 合成批量：拖动期间不每帧重合成
        // 懒建一次（轨不存在则现建），随后对其默认值开通知 merge：拖动期间只发可忽略中间态、不发结果态 Modified，
        // 否则外部刷新订阅（DefaultValue.Modified）会在拖动中回写 slider，与拖动相互打架→抖动。
        var automation = mPart.GetEffectiveAutomation(mKey) ?? mPart.AddEffectiveAutomation(mKey);
        if (automation != null)
        {
            automation.DefaultValue.BeginMergeNotify();
            mMerging = true;
        }
        // mHead 在 AddAutomation 之后捕获：ValueChanged 的 DiscardTo(mHead) 只回退本次拖动的值写入，不回退建轨本身。
        mHead = mPart.Head;
    }

    void OnValueChanged()
    {
        mPart.DiscardTo(mHead);
        mPart.GetEffectiveAutomation(mKey)?.DefaultValue.Set(mSlider.Value);
    }

    void OnValueCommitted()
    {
        OnValueChanged();
        EndMerge();
        mPart.EndMergeDirty();
        mPart.Commit();
    }

    // 退出通知 merge（幂等）：仅在已进入时 EndMergeNotify，避免无配对多发 / merge 计数泄漏（含编辑中途被释放的兜底）。
    void EndMerge()
    {
        if (!mMerging)
            return;
        mMerging = false;
        mPart.GetEffectiveAutomation(mKey)?.DefaultValue.EndMergeNotify();
    }

    public void Dispose()
    {
        EndMerge();
        s.DisposeAll();
    }

    readonly IMidiPart mPart;
    readonly AutomationKey mKey;
    readonly AutomationConfig mConfig;
    readonly SliderController mSlider;
    Head mHead;
    bool mMerging;
    readonly DisposableManager s = new();
}
