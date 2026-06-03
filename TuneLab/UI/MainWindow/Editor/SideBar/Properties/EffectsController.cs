using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TuneLab.Data;
using TuneLab.Extensions.Effect;
using TuneLab.Foundation.Property;
using TuneLab.Primitives.Property;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.SDK.Base;
using TuneLab.Foundation.Utils;
using TuneLab.Foundation.Event;
using TuneLab.Utils;
using TuneLab.I18N;
using Button = TuneLab.GUI.Components.Button;
using CheckBox = TuneLab.GUI.Components.CheckBox;
using IEffect = TuneLab.Data.IEffect;

namespace TuneLab.UI;

// 效果链管理面板：列出当前 MidiPart 的效果器链，支持增/删/重排/bypass，并复用 ObjectController 渲染每个 effect 的参数。
// 时间轴自动化编辑（per-effect 自动化曲线）本版未做，参数以 Properties 面板为准。
internal class EffectsController : StackPanel
{
    public EffectsController()
    {
        Orientation = Orientation.Vertical;
        Background = Style.INTERFACE.ToBrush();
    }

    public void SetPart(IMidiPart? part)
    {
        s.DisposeAll();
        mPart = part;

        if (mPart != null)
        {
            mPart.Effects.ListModified.Subscribe(Rebuild, s);
            mPart.Effects.Any(effect => effect.Modified).Subscribe(OnEffectModified, s);
        }

        Rebuild();
    }

    void OnEffectModified()
    {
        // effect 参数/启用经外部（如 undo/redo）改动：刷新展示值。
        foreach (var view in mEffectViews)
            view.Refresh();
    }

    void Rebuild()
    {
        foreach (var view in mEffectViews)
            view.Dispose();
        mEffectViews.Clear();
        Children.Clear();

        if (mPart == null)
            return;

        for (int i = 0; i < mPart.Effects.Count; i++)
        {
            var view = new EffectView(this, mPart.Effects[i], i);
            mEffectViews.Add(view);
            Children.Add(view.Root);
        }

        mAddButton = MakeTextButton("+ " + "Add Effect".Tr(TC.Property), 0);
        mAddButton.Height = 30;
        mAddButton.Margin = new(24, 8);
        mAddButton.Clicked += OnAddButtonClicked;
        Children.Add(mAddButton);
    }

    void OnAddButtonClicked()
    {
        if (mPart == null)
            return;

        var menu = new ContextMenu();
        var engines = EffectManager.GetAllEffectEngines();
        if (engines.Count == 0)
        {
            menu.Items.Add(new MenuItem().SetName("No effect installed".Tr(TC.Property)).SetAction(() => { }));
        }
        else
        {
            foreach (var type in engines)
            {
                var captured = type;
                menu.Items.Add(new MenuItem().SetName(captured).SetAction(() => AddEffect(captured)));
            }
        }

        mAddButton?.OpenContextMenu(menu);
    }

    void AddEffect(string type)
    {
        if (mPart == null)
            return;

        var effect = mPart.CreateEffect(new() { Type = type });
        mPart.InsertEffect(mPart.Effects.Count, effect);
        mPart.Commit();
    }

    void RemoveEffect(IEffect effect)
    {
        if (mPart == null)
            return;

        mPart.RemoveEffect(effect);
        mPart.Commit();
    }

    void MoveEffect(IEffect effect, int delta)
    {
        if (mPart == null)
            return;

        int index = mPart.Effects.IndexOf(effect);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= mPart.Effects.Count)
            return;

        mPart.RemoveEffect(effect);
        mPart.InsertEffect(target, effect);
        mPart.Commit();
    }

    void SetBypass(IEffect effect, bool isEnabled)
    {
        if (mPart == null)
            return;

        effect.IsEnabled.Set(isEnabled);
        mPart.Commit();
    }

    void CommitProperty(IEffect effect, PropertyPath path, PropertyValue value)
    {
        if (mPart == null)
            return;

        effect.Properties.SetValue(path.GetKey(), value);
        mPart.Commit();
    }

    static Button MakeTextButton(string text, double width)
    {
        var button = new Button();
        if (width > 0)
            button.Width = width;
        button.AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER, PressedColor = Style.INTERFACE } });
        button.AddContent(new() { Item = new TextItem() { Text = text, FontSize = 12 }, ColorSet = new() { Color = Colors.White } });
        return button;
    }

    // 单个 effect 的视图：标题行（bypass/类型/上移/下移/删除）+ 参数 ObjectController。
    class EffectView
    {
        public Control Root => mRoot;

        public EffectView(EffectsController owner, IEffect effect, int index)
        {
            mOwner = owner;
            mEffect = effect;

            var bypass = new CheckBox() { Margin = new(24, 0, 8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            bypass.Display(effect.IsEnabled.Value);
            bypass.ValueCommitted.Subscribe(() => owner.SetBypass(effect, bypass.Value));
            mBypass = bypass;

            var typeLabel = new Label()
            {
                Content = string.IsNullOrEmpty(effect.Type) ? "(unknown)" : effect.Type,
                FontSize = 12,
                Foreground = Style.LIGHT_WHITE.ToBrush(),
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };

            var up = MakeTextButton("↑", 28); up.Height = 28; up.Clicked += () => owner.MoveEffect(effect, -1);
            var down = MakeTextButton("↓", 28); down.Height = 28; down.Clicked += () => owner.MoveEffect(effect, 1);
            var remove = MakeTextButton("✕", 28); remove.Height = 28; remove.Clicked += () => owner.RemoveEffect(effect);

            var buttons = new StackPanel() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new(0, 0, 24, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            buttons.Children.Add(up);
            buttons.Children.Add(down);
            buttons.Children.Add(remove);

            var header = new DockPanel() { Height = 38, Background = Style.BACK.ToBrush(), LastChildFill = true };
            DockPanel.SetDock(bypass, Dock.Left);
            DockPanel.SetDock(buttons, Dock.Right);
            header.Children.Add(bypass);
            header.Children.Add(buttons);
            header.Children.Add(typeLabel);

            mController = new ObjectController();
            mController.SetConfig(effect.PropertyConfig);
            mController.ValueCommitted.Subscribe(OnValueCommitted);
            DisplayValues();

            mRoot = new StackPanel() { Orientation = Orientation.Vertical };
            mRoot.Children.Add(header);
            mRoot.Children.Add(mController);
            mRoot.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        }

        public void Refresh()
        {
            mBypass.Display(mEffect.IsEnabled.Value);
            DisplayValues();
        }

        void DisplayValues()
        {
            DisplayValues(mEffect.PropertyConfig, new PropertyPath());
        }

        void DisplayValues(ObjectConfig config, PropertyPath path)
        {
            var properties = mEffect.Properties.GetInfo();
            foreach (var kvp in config.Properties)
            {
                var propertyPath = path.Combine(kvp.Key);
                if (kvp.Value is ObjectConfig objectConfig)
                {
                    DisplayValues(objectConfig, propertyPath);
                }
                else if (kvp.Value is IValueConfig valueConfig)
                {
                    var key = propertyPath.GetKey();
                    var value = properties.GetValue(key, valueConfig.DefaultValue);
                    mController.Display(key, value);
                }
            }
        }

        void OnValueCommitted(PropertyPath path, PropertyValue value)
        {
            mOwner.CommitProperty(mEffect, path, value);
        }

        public void Dispose()
        {
            mController.ValueCommitted.Unsubscribe(OnValueCommitted);
            mController.ResetConfig();
        }

        readonly EffectsController mOwner;
        readonly IEffect mEffect;
        readonly CheckBox mBypass;
        readonly ObjectController mController;
        readonly StackPanel mRoot;
    }

    IMidiPart? mPart = null;
    Button? mAddButton = null;
    readonly List<EffectView> mEffectViews = new();
    readonly DisposableManager s = new();
}
