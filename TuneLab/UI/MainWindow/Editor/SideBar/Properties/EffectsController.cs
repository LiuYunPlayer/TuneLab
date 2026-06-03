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

// 效果链管理面板：列出当前 MidiPart 的效果器链，支持增/删/重排/bypass，并复用 PropertyObjectController 渲染每个 effect 的参数。
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
            // 链结构变化（增删/重排）整链重建；每个 effect 的参数/启用经逐字段绑定自动刷新与提交。
            mPart.Effects.ListModified.Subscribe(Rebuild, s);
        }

        Rebuild();
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

    static Button MakeTextButton(string text, double width)
    {
        var button = new Button();
        if (width > 0)
            button.Width = width;
        button.AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER, PressedColor = Style.INTERFACE } });
        button.AddContent(new() { Item = new TextItem() { Text = text, FontSize = 12 }, ColorSet = new() { Color = Colors.White } });
        return button;
    }

    // 单个 effect 的视图：标题行（bypass/类型/上移/下移/删除）+ 参数 PropertyObjectController。
    class EffectView
    {
        public Control Root => mRoot;

        public EffectView(EffectsController owner, IEffect effect, int index)
        {
            var bypass = new CheckBox() { Margin = new(24, 0, 8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            bypass.BindDataProperty(effect.IsEnabled, s);

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

            // 参数面板：逐字段绑定到 effect.Properties，值的下发/写回/撤销刷新全自动。
            mController = new PropertyObjectController();
            mController.SetConfig(effect.PropertyConfig, effect.Properties);

            mRoot = new StackPanel() { Orientation = Orientation.Vertical };
            mRoot.Children.Add(header);
            mRoot.Children.Add(mController);
            mRoot.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
        }

        public void Dispose()
        {
            s.DisposeAll();
            mController.ResetConfig();
        }

        readonly PropertyObjectController mController;
        readonly StackPanel mRoot;
        readonly DisposableManager s = new();
    }

    IMidiPart? mPart = null;
    Button? mAddButton = null;
    readonly List<EffectView> mEffectViews = new();
    readonly DisposableManager s = new();
}
