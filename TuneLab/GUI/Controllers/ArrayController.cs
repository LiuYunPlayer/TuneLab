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

    // 把当前展示的 seed（越界虚拟行）物化为真实元素：数组从现长补齐到声明数、各取 element config 默认值。
    // 任何结构操作（+ / 删除）前先调用——否则只动一个元素会令插件因 present 而不再 reseed、其余 seed 行塌掉。
    protected void MaterializeSeed()
    {
        if (mArray == null)
            return;
        for (int i = mArray.Count; i < mElements.Count; i++)
            mArray.Add(mElements[i].GetDefaultValue());
    }

    // 删除某行：先物化整段 seed（保 presence、其余行不塌），再按身份删该元素、提交（成一个撤销单元）。
    // 真实行用 token（跨重排仍指向同一元素）；seed 行无 token，物化后用其位置。
    void RemoveRow(int position, string? token)
    {
        if (mArray == null)
            return;

        MaterializeSeed();
        int index = token != null ? mArray.Tokens.IndexOf(token) : position;
        if (index < 0 || index >= mArray.Count)
            return;

        mArray.RemoveAt(index);
        mArray.Commit();
    }

    // 行的删除回调（Deletable 时非 null）：真实行按 token、seed 行按位置。
    protected Action? DeleteCallback(int position, string? token)
        => Deletable ? () => RemoveRow(position, token) : null;

    // 元素行 keyed-diff 对齐到 elements.Count 行（镜像 PropertyObjectController.Reconcile）：
    //   现存位（i < 数据元素数）绑真实 token——身份稳定，中插/删只增删一行；
    //   越界（seed）位（i ≥ 数据元素数）绑 SeedPositionView——读默认、首次写物化整段 seed（presence 语义）。
    // 同 key 同类型复用行仅更参数、key 消失则 dispose、新 key 建、同 key 换类型则换行；仅结构变化时重排布局。
    // 复合 seed 元素物化前不渲染虚拟行（仅标量 seed 虚拟绑定，见 SeedPositionView）。
    protected void ReconcileRows(IReadOnlyList<IControllerConfig> elements)
    {
        if (mArray == null)
            return;

        mElements = elements;

        // 多选复合数组：注入各元素默认值，供「缺位=默认」读取 + 「编辑补齐」填短成员（单选真实数组无此需求）。
        if (mArray is MultipleDataPropertyArray multiArray)
            multiArray.SetElementDefaults(elements.Select(e => e.GetDefaultValue()).ToList());

        var tokens = mArray.Tokens;
        int dataCount = tokens.Count;
        IReadOnlyList<PropertyValue>? seedDefaults = null;

        var nextOrder = new List<string>();
        var nextByKey = new Dictionary<string, ElementRow>();
        bool structureChanged = false;

        for (int i = 0; i < elements.Count; i++)
        {
            var cfg = elements[i];
            string key;
            IDataPropertyObject host;
            Action? onDelete;

            if (i < dataCount)
            {
                key = tokens[i];
                host = mArray;
                onDelete = DeleteCallback(i, tokens[i]);   // 真实行：按 token 删
            }
            else
            {
                if (cfg is not IValueConfig)
                    continue; // 复合 seed 元素：物化前不渲染
                key = SeedKeyPrefix + i;
                seedDefaults ??= elements.Select(e => e.GetDefaultValue()).ToList();
                host = new SeedPositionView(mArray, i, seedDefaults);
                onDelete = DeleteCallback(i, null);        // seed 行：删除前物化、按位置删
            }

            if (nextByKey.ContainsKey(key))
                continue; // key 本就唯一；防御

            if (mRowsByKey.TryGetValue(key, out var existing) && existing.ConfigType == cfg.GetType())
            {
                existing.Update(cfg);
                mRowsByKey.Remove(key);
                nextByKey.Add(key, existing);
            }
            else
            {
                nextByKey.Add(key, new ElementRow(host, key, cfg, onDelete));
                structureChanged = true; // 新建（含同 key 换类型 / seed 位物化为真实位）
            }
            nextOrder.Add(key);
        }

        if (mRowsByKey.Count > 0)
            structureChanged = true; // 有 key 被删
        if (!mOrder.SequenceEqual(nextOrder))
            structureChanged = true; // 顺序变

        if (structureChanged)
        {
            foreach (var kvp in mRowsByKey)
            {
                mRowsPanel.Children.Remove(kvp.Value.Root);
                kvp.Value.Dispose();
            }
            mRowsByKey = nextByKey;
            mOrder = nextOrder;
            AlignRows();
        }
        else
        {
            mRowsByKey = nextByKey;
            mOrder = nextOrder;
        }
    }

    // 虚拟（seed）行键前缀：以 '@' 起头，与真实元素 token（"e0"、"e1"…）绝不撞。
    const string SeedKeyPrefix = "@seed:";

    // 增量对齐 mRowsPanel.Children 到 mOrder：复用行留原位（不失焦/不打断拖动），仅移动错位的、插入新建的、移除尾部多余。
    void AlignRows()
    {
        int index = 0;
        foreach (var token in mOrder)
        {
            var view = mRowsByKey[token].Root;
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
        foreach (var kvp in mRowsByKey)
            kvp.Value.Dispose();
        mRowsByKey = new();
        mOrder = new();
        mRowsPanel.Children.Clear();
    }

    protected IDataPropertyArray? mArray;
    IReadOnlyList<IControllerConfig> mElements = [];   // 当前各元素 config（用于 seed 物化默认值）
    readonly StackPanel mRowsPanel = new() { Orientation = Orientation.Vertical };
    Dictionary<string, ElementRow> mRowsByKey = new();
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
        MaterializeSeed();   // 先把当前展示的 seed 行物化为真实元素，再追加——否则其余 seed 行会塌掉
        Array.Add(addable.Template.GetDefaultValue());
        Array.Commit();
    }

    readonly Button mAddButton;
    IReadOnlyList<AddableElement> mAddableElements = [];
}

// 单个元素行：LayerPanel 层叠 = 元素控件（填满整行）+ 可删时右侧悬浮删除钮（浮于控件之上、不挤占布局，
// 与其他同类控件观感一致）。删除钮平时透明、悬浮整行才显，故元素控件始终占满行宽、无右侧留白。
sealed class ElementRow : IDisposable
{
    public Control Root => mRoot;
    public Type ConfigType => mWidget.ConfigType;

    public ElementRow(IDataPropertyObject host, string bindKey, IControllerConfig config, Action? onDelete)
    {
        // host = 现存位的数组外观（bindKey = token）或越界位的 SeedPositionView（bindKey 仅作行身份、视图按 position 寻址）。
        mWidget = ElementWidget.Create(host, bindKey, config);

        mRoot = new LayerPanel { Background = Brushes.Transparent, Margin = new(24, 6, 24, 6) };
        mRoot.Children.Add(mWidget.View);   // 底层：控件填满整行

        if (onDelete != null)
        {
            mDelete = ArrayControlsFactory.MakeIconButton("✕");
            mDelete.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
            mDelete.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            mDelete.Margin = new(0, 0, 4, 0);
            mDelete.Opacity = 0; // 悬浮才显（透明度而非 IsVisible，浮层不占布局、行内不抖）
            mDelete.IsHitTestVisible = false; // Opacity 0 仍可命中，平时关掉点击，免拦控件右侧
            mDelete.Clicked += onDelete;
            mRoot.Children.Add(mDelete);   // 顶层：浮于控件右侧之上

            mRoot.PointerEntered += (_, _) => { mDelete.Opacity = 1; mDelete.IsHitTestVisible = true; };
            mRoot.PointerExited += (_, _) => { mDelete.Opacity = 0; mDelete.IsHitTestVisible = false; };
        }
    }

    public void Update(IControllerConfig config) => mWidget.Update(config);

    public void Dispose() => mWidget.Dispose();

    readonly LayerPanel mRoot;
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
        ExtensibleObjectConfig c => new NestedExtensibleObjectElement(dataObject, token, c),
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
            mController.ShowRandomButton = config.Randomizable;
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

    // 数组套变长键控对象元素：嵌 ExtensibleObjectController，导航进 dataObject.Object(token)。
    sealed class NestedExtensibleObjectElement : ElementWidget
    {
        public NestedExtensibleObjectElement(IDataPropertyObject dataObject, string token, ExtensibleObjectConfig config)
        {
            mController.Bind(dataObject.Object(token));
            mController.Apply(config);
        }

        public override Control View => mController;
        public override Type ConfigType => typeof(ExtensibleObjectConfig);
        public override void Update(IControllerConfig config) => mController.Apply((ExtensibleObjectConfig)config);
        public override void Dispose()
        {
            base.Dispose();
            mController.Unbind();
        }

        readonly ExtensibleObjectController mController = new();
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

// 借壳数据对象（app 侧）：把整套文档身份（撤销/Modified/merge/Head）转发给被包裹对象。
// 等价 Hosting.Foundation 内部的 IDataObject.Wrapper，但那是 internal、跨程序集不可用，故此处自备一份供 seed 视图复用。
abstract class ForwardingDataObject(IDataObject inner) : IDataObject
{
    public IModifiedEvent Modified => inner.Modified;
    public IModifiedEvent WillModify => inner.WillModify;
    public Head Head => inner.Head;
    public void Attach(IDataObject parent) => inner.Attach(parent);
    public void Detach() => inner.Detach();
    public IDisposable MergeNotify() => inner.MergeNotify();
    public void BeginMergeNotify() => inner.BeginMergeNotify();
    public void EndMergeNotify() => inner.EndMergeNotify();
    public bool Pushable() => inner.Pushable();
    public bool Commit() => inner.Commit();
    public bool Discard() => inner.Discard();
    public bool DiscardTo(Head head) => inner.DiscardTo(head);
    public bool Undo() => inner.Undo();
    public bool Redo() => inner.Redo();
}

// 越界（seed/虚拟）位置的数据外观：表示数组 position（≥ 现长）处尚未物化的元素槽。
// 读：返回默认值（浏览不弄脏文档，对称标量字段「缺席读默认」）。
// 写：先把整段 seed 物化——数组从现长补齐到 seedDefaults.Count 个元素、各取对应 element config 默认值（含本位），
//     再对 position 落写入值。如此「首次写入即物化整段 seed」，物化后 present-count = N，插件不再 reseed、显示稳定不塌；
//     下次 reconcile 用真实 token 行替换本虚拟行。提交由控件绑定的提交周期承担（与对象懒建 GetOrCreateObject 同，不在此 commit）。
sealed class SeedPositionView : ForwardingDataObject, IDataPropertyObject
{
    public SeedPositionView(IDataPropertyArray array, int position, IReadOnlyList<PropertyValue> seedDefaults) : base(array)
    {
        mArray = array;
        mPosition = position;
        mSeedDefaults = seedDefaults;
    }

    bool Real => mPosition < mArray.Count;

    void Materialize()
    {
        for (int i = mArray.Count; i < mSeedDefaults.Count; i++)
            mArray.Add(mSeedDefaults[i]);
    }

    public PropertyValue GetValue(string key, PropertyValue defaultValue)
        => Real ? mArray.GetValue(mArray.Tokens[mPosition], defaultValue) : defaultValue;

    public void SetValue(string key, PropertyValue value)
    {
        if (!Real)
            Materialize();
        mArray.SetValue(mArray.Tokens[mPosition], value);
    }

    // 复合 seed 元素物化前不渲染虚拟行（见 ReconcileRows），故下列导航仅物化后（Real）走到；
    // 防御：未物化先物化再下钻（标量 seed 流程不会触发）。
    public IDataPropertyObject Object(string key) { if (!Real) Materialize(); return mArray.Object(mArray.Tokens[mPosition]); }
    public IDataPropertyArray Array(string key) { if (!Real) Materialize(); return mArray.Array(mArray.Tokens[mPosition]); }
    public void RemoveValue(string key) { } // seed 位删除由控制器 RemoveRow 处理，标量 seed 流程不触发

    readonly IDataPropertyArray mArray;
    readonly int mPosition;
    readonly IReadOnlyList<PropertyValue> mSeedDefaults;
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

    // 键控条目的行标题（取 PropertyKey.DisplayText）：复刻属性面板字段标题样式。
    public static Label MakeRowTitle(string text)
    {
        return new Label
        {
            Content = text,
            Height = 26,
            FontSize = 12,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
            Foreground = Style.LIGHT_WHITE.ToBrush(),
            Padding = new(24, 0),
        };
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
            case ExtensibleObjectConfig addableObjectConfig:
            {
                // 变长键控容器默认值 = 当前已声明键的默认值拼成的对象（同 ObjectConfig）。
                var map = new Map<string, PropertyValue>();
                foreach (var kvp in addableObjectConfig.Properties)
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
