using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using TuneLab.Data;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;
using TuneLab.GUI;
using TuneLab.GUI.Controllers;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.Utils;

namespace TuneLab.UI;

// 属性侧栏的自动化「默认值」编辑：每条自动化一行 slider。
// 专用而非走通用属性面板，是为保留两条定制语义：① 拖动时合并脏标记（BeginMergeDirty/EndMergeDirty）避免每帧重合成；
// ② 编辑某条尚不存在的自动化时按需 AddAutomation。
internal class AutomationDefaultsController : StackPanel
{
    public IMidiPart? Part { get => mPart; set => SetPart(value); }

    public AutomationDefaultsController()
    {
        Orientation = Orientation.Vertical;
        Background = Style.INTERFACE.ToBrush();
    }

    void SetPart(IMidiPart? part)
    {
        s.DisposeAll();
        foreach (var row in mRows)
            row.Dispose();
        mRows.Clear();
        Children.Clear();

        mPart = part;
        if (mPart == null)
            return;

        foreach (var kvp in mPart.Voice.AutomationConfigs)
            mRows.Add(new Row(this, kvp.Key, kvp.Value));

        // 外部（undo/redo/preset）改默认值 → 刷新所有行显示。合并脏期间被抑制，提交后一次性刷新。
        mPart.Automations.Any(automation => automation.DefaultValue.Modified).Subscribe(Refresh, s);
    }

    void Refresh()
    {
        foreach (var row in mRows)
            row.Refresh();
    }

    double CurrentValue(string key, double defaultValue)
    {
        if (mPart != null && mPart.Automations.TryGetValue(key, out var automation))
            return automation.DefaultValue.Value;
        return defaultValue;
    }

    void BeginEdit()
    {
        if (mPart == null)
            return;

        mPart.BeginMergeDirty();
        mHead = mPart.Head;
    }

    void ChangeValue(string key, double value)
    {
        if (mPart == null)
            return;

        mPart.DiscardTo(mHead);
        if (!mPart.Automations.TryGetValue(key, out var automation))
            automation = mPart.AddAutomation(key);

        automation?.DefaultValue.Set(value);
    }

    void CommitValue(string key, double value)
    {
        if (mPart == null)
            return;

        ChangeValue(key, value);
        mPart.EndMergeDirty();
        mPart.Commit();
    }

    class Row : IDisposable
    {
        public Row(AutomationDefaultsController owner, string key, AutomationConfig config)
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
                Content = config.Name,
                Padding = new(24, 0),
            };
            owner.Children.Add(mTitle);

            mSlider = new SliderController() { Margin = new(24, 12) };
            mSlider.SetRange(config.MinValue, config.MaxValue);
            mSlider.SetDefaultValue(config.DefaultValue);
            mSlider.IsInterger = config.IsInterger;
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
        readonly string mKey;
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
