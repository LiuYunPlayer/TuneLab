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

namespace TuneLab.GUI.Controllers;

// 变长键控容器（ExtensibleObjectConfig）的面板渲染：值落为 PropertyObject。与 ListController 对称，但项是「命名键」——
// 每行带标签（PropertyKey.DisplayText）、按 key 寻址（非 token），底部 + 菜单从候选键里挑（隐藏已存在的键）。
// + 菜单与列表同用 DropDownMenu：候选分组成子菜单，子项全已加入的分组随之隐藏。
// 键控访问天生懒（DataPropertyObject 的 Object/GetValue 读不建、写物化 + presence），故无需 array 那套越界 seed 适配。
// 典型用法是「起步为空、+ 增项」（如多说话人音色里选要混入的 speaker）；插件若对 absent 返回 seed 键，则随编辑物化。
internal sealed class ExtensibleObjectController : StackPanel
{
    // 按钮作用域（见 ButtonRoot）：由外层 creator 设好，条目里的对象按 "路径/键" 继续往下传。
    internal ButtonRoot? ButtonRoot { get; set; }
    internal string ButtonPath { get; set; } = string.Empty;

    public ExtensibleObjectController()
    {
        Orientation = Orientation.Vertical;
        Background = Style.INTERFACE.ToBrush();
        Children.Add(mRowsPanel);

        mAddButton = ArrayControlsFactory.MakeTextButton("+", 0);
        mAddButton.Height = 30;
        mAddButton.Margin = new(24, 8);
        mAddButton.Clicked += OnAddButtonClicked;
        mAddMenu = new DropDownMenu(mAddButton);
        mAddMenu.ItemPicked += item => AddKey((AddableKey)item.Tag!);
        mAddHost = new Panel();
        mAddHost.Children.Add(mAddButton);
        mAddHost.Children.Add(mAddMenu);
        Children.Add(mAddHost);
    }

    public void Bind(IDataPropertyObject dataObject)
    {
        ResetRows();
        mDataObject = dataObject;
    }

    public void Unbind()
    {
        ResetRows();
        mDataObject = null;
    }

    public void Apply(ExtensibleObjectConfig config)
    {
        mAddableElements = config.AddableElements;
        ReconcileRows(config.Properties);

        // + 候选 = 尚未存在的键；全部已加 / 无候选 → 隐藏 +。
        mAddHost.IsVisible = AddableMenu.FirstLeaf(BuildItems(mAddableElements)) != null;
        // + 隐藏时末行会贴住下一个字段的分隔线：补上与普通字段控件同样的底边距（同 ListController）。
        mRowsPanel.Margin = new(0, 0, 0, mAddHost.IsVisible ? 0 : ArrayControllerBase.TrailingGap);
    }

    // 键控 keyed-diff：按 PropertyKey.Id 复用行（同 id 同类型仅更参数）、消失则 dispose、新建、换类型则换行。
    void ReconcileRows(IReadOnlyOrderedMap<PropertyKey, IControllerConfig> properties)
    {
        if (mDataObject == null)
            return;

        var nextOrder = new List<string>();
        var nextByKey = new Dictionary<string, KeyedRow>();
        var presentKeys = new HashSet<string>();
        bool structureChanged = false;

        foreach (var kvp in properties)
        {
            var key = kvp.Key;
            var id = key.Id;
            var cfg = kvp.Value;
            if (nextByKey.ContainsKey(id))
                continue; // key 唯一；防御

            presentKeys.Add(id);

            if (mRowsByKey.TryGetValue(id, out var existing) && existing.ConfigType == cfg.GetType())
            {
                existing.Update(key, cfg);   // 复用：更新参数（含 DisplayText 改动重贴标签）
                mRowsByKey.Remove(id);
                nextByKey.Add(id, existing);
            }
            else
            {
                nextByKey.Add(id, new KeyedRow(mDataObject, key, cfg, () => RemoveKey(id), ButtonRoot, ButtonPath + "/" + id));
                structureChanged = true;
            }
            nextOrder.Add(id);
        }
        for (int i = 0; i < nextOrder.Count; i++)
            nextByKey[nextOrder[i]].SetFirst(i == 0);

        if (mRowsByKey.Count > 0)
            structureChanged = true; // 有键被删
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

        mPresentKeys = presentKeys;
    }

    void AlignRows()
    {
        int index = 0;
        foreach (var id in mOrder)
        {
            var view = mRowsByKey[id].Root;
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

    void OnAddButtonClicked()
    {
        if (mDataObject == null || mAddMenu.JustClosed)
            return;

        // 候选 = 尚未存在的键。
        var items = BuildItems(mAddableElements);
        int count = AddableMenu.CountLeaves(items);
        if (count == 0)
            return;

        if (count == 1)
        {
            AddKey((AddableKey)AddableMenu.FirstLeaf(items)!.Tag!);
            return;
        }

        mAddMenu.Open(items, 0);
    }

    List<DropDownItem> BuildItems(IReadOnlyList<AddableKey> addables) => AddableMenu.Build(addables, mPresentKeys.Contains);

    // 加键 = 写入该键的默认值（物化键 + 其所在容器），提交。插件随后 present 该键 → 行出现。
    void AddKey(AddableKey addable)
    {
        if (mDataObject == null)
            return;
        mDataObject.SetValue(addable.Key.Id, addable.Template.GetDefaultValue());
        mDataObject.Commit();
    }

    // 删键 = 移除该键（presence 翻回 absent），提交。
    void RemoveKey(string id)
    {
        if (mDataObject == null)
            return;
        mDataObject.RemoveValue(id);
        mDataObject.Commit();
    }

    void ResetRows()
    {
        foreach (var kvp in mRowsByKey)
            kvp.Value.Dispose();
        mRowsByKey = new();
        mOrder = new();
        mPresentKeys = new();
        mRowsPanel.Children.Clear();
    }

    IDataPropertyObject? mDataObject;
    IReadOnlyList<AddableKey> mAddableElements = [];
    HashSet<string> mPresentKeys = new();
    readonly StackPanel mRowsPanel = new() { Orientation = Orientation.Vertical };
    readonly Button mAddButton;
    readonly DropDownMenu mAddMenu;
    readonly Panel mAddHost;
    Dictionary<string, KeyedRow> mRowsByKey = new();
    List<string> mOrder = new();
}

// 单个命名条目行：标签（DisplayText）在上、条目控件在下；悬浮整条时右侧外浮出删除钮（同 ElementRow 的 FloatingDelete，
// 不占控件宽度、不遮控件）。键控条目自带标签，不另加序号条。间距：条目上下各留 EntryPadding；名字与下方控件
// 之间留 TitleGap，但只在控件真有内容时——空对象条目只有名字，再留就成了下方独宽的一截。分隔线同 ElementRow：每条下沿一条、首条头顶一条，
// 左右缩进到控件边距内、与数组元素的线对齐。
sealed class KeyedRow : IDisposable
{
    public Control Root => mRoot;
    public Type ConfigType => mWidget.ConfigType;

    public KeyedRow(IDataPropertyObject dataObject, PropertyKey key, IControllerConfig config, Action onDelete, ButtonRoot? buttonRoot, string buttonPath)
    {
        mWidget = ElementWidget.Create(dataObject, key.Id, config, buttonRoot, buttonPath);
        mTitle = ArrayControlsFactory.MakeRowTitle(key.DisplayText ?? key.Id);

        var content = new StackPanel { Orientation = Orientation.Vertical, Margin = new(0, ArrayControlsFactory.EntryPadding) };
        content.Children.Add(mTitle);
        content.Children.Add(mWidget.View);
        // 按控件实际渲染高度判「有无内容」而非看 config：条件面板可能让它中途变空或长出字段。
        mWidget.View.SizeChanged += (_, e) => UpdateTitleGap(e.NewSize.Height);
        UpdateTitleGap(0);

        mTopLine = ArrayControlsFactory.MakeLine();
        mTopLine.Margin = new(ArrayControlsFactory.Gutter, 0);
        var bottomLine = ArrayControlsFactory.MakeLine();
        bottomLine.Margin = new(ArrayControlsFactory.Gutter, 0);
        var entry = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(mTopLine, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(bottomLine, Avalonia.Controls.Dock.Bottom);
        entry.Children.Add(mTopLine);
        entry.Children.Add(bottomLine);
        entry.Children.Add(content);

        mRoot = new Panel { Background = Brushes.Transparent };   // 透明底：空白处也收得到悬浮
        mRoot.Children.Add(entry);
        // 条目横贯面板全宽、控件自带左右 Gutter 边距：锚在条目上时往回退一个 Gutter，钮才落在控件右沿外侧（与 ElementRow 一致）。
        mDelete = new FloatingDelete(mRoot, mRoot, ArrayControlsFactory.DeleteOffset - ArrayControlsFactory.Gutter, onDelete);
    }

    public void SetFirst(bool first) => mTopLine.IsVisible = first;

    void UpdateTitleGap(double contentHeight) => mTitle.Margin = new(0, 0, 0, contentHeight > 0 ? ArrayControlsFactory.TitleGap : 0);

    public void Update(PropertyKey key, IControllerConfig config)
    {
        mTitle.Content = key.DisplayText ?? key.Id;   // 语言切换等仅 DisplayText 变：重贴标签、不重建
        mWidget.Update(config);
    }

    public void Dispose()
    {
        mDelete.Dispose();
        mWidget.Dispose();
    }

    readonly Panel mRoot;
    readonly Label mTitle;
    readonly Border mTopLine;
    readonly FloatingDelete mDelete;
    readonly ElementWidget mWidget;
}
