using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using TuneLab.Data;
using TuneLab.Extensions.Effect;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
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
            var view = new EffectView(this, mPart, mPart.Effects[i], i);
            mEffectViews.Add(view);
            Children.Add(view.Root);
        }

        mAddButton = MakeTextButton("+ " + "Add Effect".Tr(TC.Property), 0);
        mAddButton.Height = 30;
        mAddButton.Margin = new(24, 8);
        mAddButton.Clicked += OnAddButtonClicked;
        Children.Add(mAddButton);

        // Add Effect 按钮下方收尾分隔线（与各 effect 块、面板其他区块的分界一致）。
        Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
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

    // 单个 effect 的视图：标题行（bypass/类型/上移/下移/删除）+ 参数 PropertyObjectController +
    // 该 effect 连续自动化轨的默认值行（直接混在参数后，不另立标题——effect 块本身已有类型表头）。
    class EffectView
    {
        public Control Root => mRoot;

        public EffectView(EffectsController owner, IMidiPart part, IEffect effect, int index)
        {
            mPart = part;
            mEffect = effect;
            mIndex = index;

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

            // 标题行底色用 INTERFACE，与侧栏其他控件块一致（不再用更深的 BACK）。
            var header = new DockPanel() { Height = 38, Background = Style.INTERFACE.ToBrush(), LastChildFill = true };
            DockPanel.SetDock(bypass, Dock.Left);
            DockPanel.SetDock(buttons, Dock.Right);
            header.Children.Add(bypass);
            header.Children.Add(buttons);
            header.Children.Add(typeLabel);

            // 参数面板：逐字段绑定到 effect.Properties，值的下发/写回/撤销刷新全自动。
            // 条件面板：参数 commit 后按当前值重算整棵 config 并 keyed-diff 到控件树（显隐/换控件/选项随值变都是 config 的涌现）。
            mController = new PropertyObjectController();
            mController.SetConfig(effect.PropertyConfig, effect.Properties);
            effect.Properties.Modified.Subscribe(ReconcileController, s);

            // 自动化默认值外部（undo/redo/preset）改动 → 刷新所有行（结构不变，无需重建）。
            effect.Automations.WhenAny(automation => automation.DefaultValue.Modified).Subscribe(RefreshAutomationRows, s);

            mRoot = new StackPanel() { Orientation = Orientation.Vertical };
            mRoot.Children.Add(header);
            // 分界线放在标题行【下方】（标题行与参数之间）：既给出标题↔参数的分界，又避免它叠到上一块底部 / 面板分隔线上造成双线。
            mRoot.Children.Add(new Border() { Height = 1, Background = Style.BACK.ToBrush() });
            mRoot.Children.Add(mController);
            mRoot.Children.Add(mAutomationContainer);

            RebuildAutomationRows();
        }

        // 参数值 commit：数据对象不变，按当前值重算 config 并 keyed-diff 复用控件；自动化轨集合也随当前值涌现（条件轨显隐），故一并重建默认值行。
        // 重算 defer 到下一 UI 调度：commit 可能发生在控件自身事件回调链中（如 ComboBox 的 SelectionChanged），
        // 同步重算会重入修改控件集合（Avalonia ComboBox 在其 SelectionChanged 中 Clear/重填 Items 会抛异常）。
        // pending 标志合并一拍内的多次触发；dispose 后 pending 的回调命中 Reconcile 的空数据对象保护，安全空转。
        void ReconcileController()
        {
            if (mReconcilePending)
                return;
            mReconcilePending = true;
            Dispatcher.UIThread.Post(() =>
            {
                mReconcilePending = false;
                mController.Reconcile(mEffect.PropertyConfig);
                RebuildAutomationRows();
            });
        }

        // 该 effect 的连续自动化轨默认值行（分段轨无默认基线、跳过）；混在参数控件后，按 AutomationKey.Effect 路由。
        void RebuildAutomationRows()
        {
            foreach (var row in mAutomationRows)
                row.Dispose();
            mAutomationRows.Clear();
            mAutomationContainer.Children.Clear();

            foreach (var kvp in mEffect.AutomationConfigs)
            {
                if (kvp.Value.IsPiecewise)
                    continue;

                var row = new AutomationDefaultRow(mPart, AutomationKey.Effect(mIndex, kvp.Key), kvp.Key, kvp.Value);
                mAutomationRows.Add(row);
                mAutomationContainer.Children.Add(row);
            }
        }

        void RefreshAutomationRows()
        {
            foreach (var row in mAutomationRows)
                row.Refresh();
        }

        public void Dispose()
        {
            s.DisposeAll();
            foreach (var row in mAutomationRows)
                row.Dispose();
            mAutomationRows.Clear();
            mController.ResetConfig();
        }

        readonly IMidiPart mPart;
        readonly IEffect mEffect;
        readonly int mIndex;
        readonly PropertyObjectController mController;
        readonly StackPanel mAutomationContainer = new() { Orientation = Orientation.Vertical };
        readonly List<AutomationDefaultRow> mAutomationRows = new();
        readonly StackPanel mRoot;
        bool mReconcilePending = false;
        readonly DisposableManager s = new();
    }

    IMidiPart? mPart = null;
    Button? mAddButton = null;
    readonly List<EffectView> mEffectViews = new();
    readonly DisposableManager s = new();
}
