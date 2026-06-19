using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.GUI.Components;
using TuneLab.Utils;
using Button = TuneLab.GUI.Components.Button;
using CheckBox = TuneLab.GUI.Components.CheckBox;

namespace TuneLab.GUI.Controllers;

// 有序可重复列表（PropertyArray）的面板渲染。数组「字段」本身由父 PropertyObjectController 的 Creator 带 key 标题；
// 字段内的元素行走本控制器——元素无 key 标签、行内紧凑布局（控件 + 悬浮删除钮），不复用对象字段「标题 + 分隔符」的排布。
// 元素以稳定 token 寻址（IDataPropertyArray 把 token 当 key），按 token keyed-diff：同 token 复用行（拖动中的 slider /
// 编辑中的文本框不被销毁），中插/删只增删一行。config = f(数据) 随数据变化重算，由面板级 reconcile 在每次 commit 后驱动。
internal abstract class ArrayControllerBase : StackPanel
{
    protected ArrayControllerBase()
    {
        Orientation = Orientation.Vertical;
        Background = Style.INTERFACE.ToBrush();
        Children.Add(mRowsPanel);
    }

    // 绑定到（新的）数组数据外观；切换数据时清空旧行（复用的 widget 仍绑在旧 token 上）。
    public void Bind(IDataPropertyArray array)
    {
        ResetRows();
        mArray = array;
    }

    public void Unbind()
    {
        ResetRows();
        mArray = null;
    }

    protected virtual bool Deletable => false;

    protected IDataPropertyArray Array => mArray ?? throw new InvalidOperationException("ArrayController has no data object.");

    // 删除请求（仅 Deletable 行的删除钮触发）：按当前 index 删元素并提交（成一个撤销单元）。
    public void RequestRemove(string token)
    {
        if (mArray == null)
            return;

        int index = mArray.Tokens.IndexOf(token);
        if (index < 0)
            return;

        mArray.RemoveAt(index);
        mArray.Commit();
    }

    // 元素行按 token keyed-diff 对齐到 (token[i], elements[i])。镜像 PropertyObjectController.Reconcile：
    // 同 token 同类型复用行仅更参数、token 消失则 dispose、新 token 建、同 token 换类型则换行；仅结构变化时重排布局。
    protected void ReconcileRows(IReadOnlyList<IControllerConfig> elements)
    {
        if (mArray == null)
            return;

        var tokens = mArray.Tokens;
        int n = Math.Min(tokens.Count, elements.Count);

        var nextOrder = new List<string>();
        var nextByToken = new Dictionary<string, ElementRow>();
        bool structureChanged = false;

        for (int i = 0; i < n; i++)
        {
            var token = tokens[i];
            var cfg = elements[i];
            if (nextByToken.ContainsKey(token))
                continue; // token 本就唯一；防御

            if (mRowsByToken.TryGetValue(token, out var existing) && existing.ConfigType == cfg.GetType())
            {
                existing.Update(cfg);
                mRowsByToken.Remove(token);
                nextByToken.Add(token, existing);
            }
            else
            {
                nextByToken.Add(token, new ElementRow(this, mArray, token, cfg, Deletable));
                structureChanged = true; // 新建（含同 token 换类型）
            }
            nextOrder.Add(token);
        }

        if (mRowsByToken.Count > 0)
            structureChanged = true; // 有 token 被删
        if (!mOrder.SequenceEqual(nextOrder))
            structureChanged = true; // 顺序变

        if (structureChanged)
        {
            foreach (var kvp in mRowsByToken)
            {
                mRowsPanel.Children.Remove(kvp.Value.Root);
                kvp.Value.Dispose();
            }
            mRowsByToken = nextByToken;
            mOrder = nextOrder;
            AlignRows();
        }
        else
        {
            mRowsByToken = nextByToken;
            mOrder = nextOrder;
        }
    }

    // 增量对齐 mRowsPanel.Children 到 mOrder：复用行留原位（不失焦/不打断拖动），仅移动错位的、插入新建的、移除尾部多余。
    void AlignRows()
    {
        int index = 0;
        foreach (var token in mOrder)
        {
            var view = mRowsByToken[token].Root;
            int current = mRowsPanel.Children.IndexOf(view);
            if (current == index)
            {
                index++;
                continue;
            }
            if (current >= 0)
                mRowsPanel.Children.RemoveAt(current);
            mRowsPanel.Children.Insert(index, view);
            index++;
        }
        while (mRowsPanel.Children.Count > index)
            mRowsPanel.Children.RemoveAt(mRowsPanel.Children.Count - 1);
    }

    void ResetRows()
    {
        foreach (var kvp in mRowsByToken)
            kvp.Value.Dispose();
        mRowsByToken = new();
        mOrder = new();
        mRowsPanel.Children.Clear();
    }

    protected IDataPropertyArray? mArray;
    readonly StackPanel mRowsPanel = new() { Orientation = Orientation.Vertical };
    Dictionary<string, ElementRow> mRowsByToken = new();
    List<string> mOrder = new();
}

// 定长数组：长度 = config 声明数，逐位渲染、不可增删；第 i 位由 Elements[i] 渲染并绑定到 token[i]。
// 数据应有 Elements.Count 个元素（由「恢复默认」walker 递归 seed）；若短则渲染现有位、不在浏览时写入数据（保 presence）。
internal sealed class ArrayController : ArrayControllerBase
{
    public void Apply(ArrayConfig config) => ReconcileRows(config.Elements);
}

// 变长列表：行可删（悬浮显删除钮）、底部 + 按钮添加。AddableElements 单项直加该类型默认值、多项弹下拉按 Label 选。
internal sealed class ListController : ArrayControllerBase
{
    public ListController()
    {
        mAddButton = ArrayControlsFactory.MakeTextButton("+", 0);
        mAddButton.Height = 30;
        mAddButton.Margin = new(24, 8);
        mAddButton.Clicked += OnAddButtonClicked;
        Children.Add(mAddButton); // 置于 mRowsPanel 之后
    }

    protected override bool Deletable => true;

    public void Apply(ListConfig config)
    {
        mAddableElements = config.AddableElements;
        ReconcileRows(config.Elements);
        mAddButton.IsVisible = mAddableElements.Count > 0; // 无候选（如达上限）→ 隐藏 +
    }

    void OnAddButtonClicked()
    {
        if (mArray == null || mAddableElements.Count == 0)
            return;

        if (mAddableElements.Count == 1)
        {
            AddElement(mAddableElements[0]);
            return;
        }

        // 多候选：弹下拉按类型名（Label）选。
        var menu = new ContextMenu();
        foreach (var addable in mAddableElements)
        {
            var captured = addable;
            menu.Items.Add(new MenuItem().SetName(captured.Label ?? string.Empty).SetAction(() => AddElement(captured)));
        }
        mAddButton.OpenContextMenu(menu);
    }

    void AddElement(AddableElement addable)
    {
        Array.Add(addable.Template.GetDefaultValue());
        Array.Commit();
    }

    readonly Button mAddButton;
    IReadOnlyList<AddableElement> mAddableElements = [];
}

// 单个元素行：DockPanel 容器 = 元素控件（填充）+ 可删时右侧悬浮删除钮。删除钮平时透明（不改行宽，避免悬浮抖动）。
sealed class ElementRow : IDisposable
{
    public Control Root => mRoot;
    public Type ConfigType => mWidget.ConfigType;

    public ElementRow(ArrayControllerBase owner, IDataPropertyArray array, string token, IControllerConfig config, bool deletable)
    {
        mWidget = ElementWidget.Create(array, token, config);

        mRoot = new DockPanel { LastChildFill = true, Margin = new(24, 6, 24, 6) };

        if (deletable)
        {
            mDelete = ArrayControlsFactory.MakeIconButton("✕");
            mDelete.Opacity = 0; // 悬浮才显（用透明度而非 IsVisible，保留占位、行内不抖）
            mDelete.Clicked += () => owner.RequestRemove(token);
            DockPanel.SetDock(mDelete, Dock.Right);
            mRoot.Children.Add(mDelete);

            mRoot.PointerEntered += (_, _) => mDelete.Opacity = 1;
            mRoot.PointerExited += (_, _) => mDelete.Opacity = 0;
        }

        mRoot.Children.Add(mWidget.View);
    }

    public void Update(IControllerConfig config) => mWidget.Update(config);

    public void Dispose() => mWidget.Dispose();

    readonly DockPanel mRoot;
    readonly Button? mDelete;
    readonly ElementWidget mWidget;
}

// 元素控件分发件：为单个 element config 在 (dataObject, token) 处建并双向绑定一个无标题控件。
// 复用各控件件（Slider/Text/ComboBox/CheckBox）+ IDataPropertyObject 字段绑定（token 当 key）；
// 复合元素递归——对象元素嵌 PropertyObjectController（其内部字段自带 key 标签）、数组元素嵌 Array/ListController。
internal abstract class ElementWidget : IDisposable
{
    public abstract Control View { get; }
    public abstract Type ConfigType { get; }
    public abstract void Update(IControllerConfig config);
    public virtual void Dispose() => s.DisposeAll();

    protected readonly DisposableManager s = new();

    public static ElementWidget Create(IDataPropertyObject dataObject, string token, IControllerConfig config) => config switch
    {
        SliderConfig c => new SliderElement(dataObject, token, c),
        TextBoxConfig c => new TextElement(dataObject, token, c),
        ComboBoxConfig c => new ComboElement(dataObject, token, c),
        CheckBoxConfig c => new CheckElement(dataObject, token, c),
        ObjectConfig c => new ObjectElement(dataObject, token, c),
        ArrayConfig c => new NestedArrayElement(dataObject, token, c),
        ListConfig c => new NestedListElement(dataObject, token, c),
        _ => new UnknownElement(config),
    };

    sealed class SliderElement : ElementWidget
    {
        public SliderElement(IDataPropertyObject dataObject, string token, SliderConfig config)
        {
            mController = new SliderController { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            Apply(config);
            mController.BindDataProperty(dataObject.NumberField(token, config.DefaultValue), s);
        }

        void Apply(SliderConfig config)
        {
            mController.SetRange(config.MinValue, config.MaxValue);
            mController.SetDefaultValue(config.DefaultValue);
            mController.IsInteger = config.IsInteger;
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(SliderConfig);
        public override void Update(IControllerConfig config) => Apply((SliderConfig)config);

        readonly SliderController mController;
    }

    sealed class TextElement : ElementWidget
    {
        public TextElement(IDataPropertyObject dataObject, string token, TextBoxConfig config)
        {
            mController = new SingleLineTextController { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, IsPassword = config.IsPassword };
            mController.BindDataProperty(dataObject.StringField(token, config.DefaultValue), s);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(TextBoxConfig);
        public override void Update(IControllerConfig config) => mController.IsPassword = ((TextBoxConfig)config).IsPassword;

        readonly SingleLineTextController mController;
    }

    sealed class ComboElement : ElementWidget
    {
        public ComboElement(IDataPropertyObject dataObject, string token, ComboBoxConfig config)
        {
            mDataObject = dataObject;
            mToken = token;
            mConfig = config;
            mController = new ComboBoxController { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            BindWith(config);
        }

        // SetConfig（自洽显示 default）后紧接 bind（bind 内 Refresh 把当前数据值按三态显示），复刻 ComboBoxCreator 的成对范式。
        void BindWith(ComboBoxConfig config)
        {
            mController.SetConfig(config);
            mController.BindDataProperty(mDataObject.ValueField(mToken, config.DefaultOption.Value), s);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(ComboBoxConfig);

        // 选项未变则跳过（SetConfig 会 Clear + 重填打断选中）；选项变则解旧绑定 → 重新 SetConfig+bind 显示回当前数据值。
        public override void Update(IControllerConfig config)
        {
            var combo = (ComboBoxConfig)config;
            if (combo.Options.SequenceEqual(mConfig.Options))
                return;
            mConfig = combo;
            s.DisposeAll();
            BindWith(combo);
        }

        readonly IDataPropertyObject mDataObject;
        readonly string mToken;
        readonly ComboBoxController mController;
        ComboBoxConfig mConfig;
    }

    sealed class CheckElement : ElementWidget
    {
        public CheckElement(IDataPropertyObject dataObject, string token, CheckBoxConfig config)
        {
            mController = new CheckBox { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            mController.BindDataProperty(dataObject.BoolField(token, config.DefaultValue), s);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(CheckBoxConfig);
        public override void Update(IControllerConfig config) { }

        readonly CheckBox mController;
    }

    // 对象元素：嵌 PropertyObjectController 渲染该元素子字段（带各自 key 标签），导航进 dataObject.Object(token)。
    sealed class ObjectElement : ElementWidget
    {
        public ObjectElement(IDataPropertyObject dataObject, string token, ObjectConfig config)
        {
            mController = new PropertyObjectController();
            mController.SetConfig(config, dataObject.Object(token));
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(ObjectConfig);
        public override void Update(IControllerConfig config) => mController.Reconcile((ObjectConfig)config);
        public override void Dispose()
        {
            base.Dispose();
            mController.ResetConfig();
        }

        readonly PropertyObjectController mController;
    }

    // 数组套数组元素：嵌 ArrayController/ListController，导航进 dataObject.Array(token)。
    sealed class NestedArrayElement : ElementWidget
    {
        public NestedArrayElement(IDataPropertyObject dataObject, string token, ArrayConfig config)
        {
            mController.Bind(dataObject.Array(token));
            mController.Apply(config);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(ArrayConfig);
        public override void Update(IControllerConfig config) => mController.Apply((ArrayConfig)config);
        public override void Dispose()
        {
            base.Dispose();
            mController.Unbind();
        }

        readonly ArrayController mController = new();
    }

    sealed class NestedListElement : ElementWidget
    {
        public NestedListElement(IDataPropertyObject dataObject, string token, ListConfig config)
        {
            mController.Bind(dataObject.Array(token));
            mController.Apply(config);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(ListConfig);
        public override void Update(IControllerConfig config) => mController.Apply((ListConfig)config);
        public override void Dispose()
        {
            base.Dispose();
            mController.Unbind();
        }

        readonly ListController mController = new();
    }

    sealed class UnknownElement : ElementWidget
    {
        public UnknownElement(IControllerConfig config)
        {
            mConfigType = config.GetType();
            mView = new Label { Content = $"(unsupported element: {mConfigType.Name})", FontSize = 12, Foreground = Style.LIGHT_WHITE.ToBrush(), Margin = new(0, 6) };
        }

        public override Control View => mView;
        public override Type ConfigType => mConfigType;
        public override void Update(IControllerConfig config) { }

        readonly Label mView;
        readonly Type mConfigType;
    }
}

internal static class ArrayControlsFactory
{
    // 文本按钮：复刻 EffectsController 的 BorderItem + TextItem 配色，与侧栏其余按钮一致。
    public static Button MakeTextButton(string text, double width)
    {
        var button = new Button();
        if (width > 0)
            button.Width = width;
        button.AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER, PressedColor = Style.INTERFACE } });
        button.AddContent(new() { Item = new TextItem() { Text = text, FontSize = 12 }, ColorSet = new() { Color = Colors.White } });
        return button;
    }

    public static Button MakeIconButton(string glyph)
    {
        var button = MakeTextButton(glyph, 24);
        button.Height = 24;
        return button;
    }
}

// config → 默认 PropertyValue 的递归求值：叶子取 IValueConfig.DefaultValue，复合（对象/数组/列表）递归各成员拼合。
// 供「恢复默认 / 应用 preset」walker 与 ListController 的「+ 添加元素」（新元素 seed 默认值）共用。
internal static class ControllerConfigDefaults
{
    public static PropertyValue GetDefaultValue(this IControllerConfig config)
    {
        switch (config)
        {
            case ObjectConfig objectConfig:
            {
                var map = new Map<string, PropertyValue>();
                foreach (var kvp in objectConfig.Properties)
                    map.Add(kvp.Key.Id, kvp.Value.GetDefaultValue());
                return PropertyValue.Create(new PropertyObject(map));
            }
            case ArrayConfig arrayConfig:
                return new PropertyArray(arrayConfig.Elements.Select(e => e.GetDefaultValue()).ToList());
            case ListConfig listConfig:
                return new PropertyArray(listConfig.Elements.Select(e => e.GetDefaultValue()).ToList());
            case IValueConfig valueConfig:
                return valueConfig.DefaultValue;
            default:
                return PropertyValue.Null;
        }
    }
}
