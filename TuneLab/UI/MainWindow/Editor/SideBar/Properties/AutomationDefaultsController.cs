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

// 属性侧栏的自动化「默认值」编辑：每条自动化一行 slider。voice 与各 effect 的自动化统一在此，
// voice 一组、每个 effect 一组（分隔符 + effect 名表头），与底部参数栏的分组一致。
// 专用而非走通用属性面板，是为保留两条定制语义：① 拖动时合并脏标记（BeginMergeDirty/EndMergeDirty）避免每帧重合成；
// ② 编辑某条尚不存在的自动化时按需 AddAutomation（voice 走 part、effect 走对应 effect，经 AutomationKey 路由）。
internal class AutomationDefaultsController : StackPanel
{
    public IMidiPart? Part { get => mPart; set { mPart = value; Rebuild(); } }

    public AutomationDefaultsController()
    {
        Orientation = Orientation.Vertical;
        Background = Style.INTERFACE.ToBrush();
    }

    void Rebuild()
    {
        s.DisposeAll();
        foreach (var row in mRows)
            row.Dispose();
        mRows.Clear();
        Children.Clear();

        if (mPart == null)
            return;

        AddGroup(null, mPart.Voice.AutomationConfigs, -1);
        for (int i = 0; i < mPart.Effects.Count; i++)
            AddGroup(mPart.Effects[i].Type, mPart.Effects[i].AutomationConfigs, i);

        // 默认值外部（undo/redo/preset）改动 → 刷新所有行；自动化轨增减 / effect 链变化 → 重建。
        mPart.Automations.WhenAny(automation => automation.DefaultValue.Modified).Subscribe(Refresh, s);
        foreach (var effect in mPart.Effects)
            effect.Automations.WhenAny(automation => automation.DefaultValue.Modified).Subscribe(Refresh, s);
        mPart.Effects.ListModified.Subscribe(Rebuild, s);
    }

    void AddGroup(string? header, IReadOnlyOrderedMap<string, AutomationConfig> configs, int effectIndex)
    {
        if (configs.Count == 0)
            return;

        if (header != null)
        {
            Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
            Children.Add(new Label()
            {
                Height = 26,
                FontSize = 12,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Foreground = Style.LIGHT_WHITE.Opacity(0.6).ToBrush(),
                Content = string.IsNullOrEmpty(header) ? "(effect)" : header,
                Padding = new(24, 0),
            });
        }

        foreach (var kvp in configs)
        {
            var key = effectIndex < 0 ? AutomationKey.Voice(kvp.Key) : AutomationKey.Effect(effectIndex, kvp.Key);
            mRows.Add(new Row(this, key, kvp.Key, kvp.Value));
        }
    }

    void Refresh()
    {
        foreach (var row in mRows)
            row.Refresh();
    }

    double CurrentValue(AutomationKey key, double defaultValue)
    {
        return mPart?.GetEffectiveAutomation(key)?.DefaultValue.Value ?? defaultValue;
    }

    void BeginEdit()
    {
        if (mPart == null)
            return;

        mPart.BeginMergeDirty();
        mHead = mPart.Head;
    }

    void ChangeValue(AutomationKey key, double value)
    {
        if (mPart == null)
            return;

        mPart.DiscardTo(mHead);
        var automation = mPart.GetEffectiveAutomation(key) ?? mPart.AddEffectiveAutomation(key);
        automation?.DefaultValue.Set(value);
    }

    void CommitValue(AutomationKey key, double value)
    {
        if (mPart == null)
            return;

        ChangeValue(key, value);
        mPart.EndMergeDirty();
        mPart.Commit();
    }

    class Row : IDisposable
    {
        public Row(AutomationDefaultsController owner, AutomationKey key, string keyName, AutomationConfig config)
        {
            mOwner = owner;
            mKey = key;
            mConfig = config;

            mTitle = new Label()
            {
                Height = 30,
                FontSize = 12,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                Foreground = Style.LIGHT_WHITE.ToBrush(),
                Content = config.DisplayText ?? keyName,
                Padding = new(24, 0),
            };
            owner.Children.Add(mTitle);

            mSlider = new SliderController() { Margin = new(24, 12) };
            mSlider.SetRange(config.MinValue, config.MaxValue);
            mSlider.SetDefaultValue(config.DefaultValue);
            owner.Children.Add(mSlider);

            mBorder = new Border() { Height = 1, Background = Style.BACK.ToBrush() };
            owner.Children.Add(mBorder);

            mSlider.ValueWillChange.Subscribe(OnValueWillChange, s);
            mSlider.ValueChanged.Subscribe(OnValueChanged, s);
            mSlider.ValueCommitted.Subscribe(OnValueCommitted, s);

            Refresh();
        }

        public void Refresh()
        {
            mSlider.Display(mOwner.CurrentValue(mKey, mConfig.DefaultValue));
        }

        void OnValueWillChange() => mOwner.BeginEdit();
        void OnValueChanged() => mOwner.ChangeValue(mKey, mSlider.Value);
        void OnValueCommitted() => mOwner.CommitValue(mKey, mSlider.Value);

        public void Dispose()
        {
            s.DisposeAll();
        }

        readonly AutomationDefaultsController mOwner;
        readonly AutomationKey mKey;
        readonly AutomationConfig mConfig;
        readonly Label mTitle;
        readonly SliderController mSlider;
        readonly Border mBorder;
        readonly DisposableManager s = new();
    }

    IMidiPart? mPart = null;
    Head mHead;
    readonly List<Row> mRows = new();
    readonly DisposableManager s = new();
}
