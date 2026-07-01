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
// 键控访问天生懒（DataPropertyObject 的 Object/GetValue 读不建、写物化 + presence），故无需 array 那套越界 seed 适配。
// 典型用法是「起步为空、+ 增项」（如多说话人音色里选要混入的 speaker）；插件若对 absent 返回 seed 键，则随编辑物化。
internal sealed class ExtensibleObjectController : StackPanel
{
    public ExtensibleObjectController()
    {
        Orientation = Orientation.Vertical;
        Background = Style.INTERFACE.ToBrush();
        Children.Add(mRowsPanel);

        mAddButton = ArrayControlsFactory.MakeTextButton("+", 0);
        mAddButton.Height = 30;
        mAddButton.Margin = new(24, 8);
        mAddButton.Clicked += OnAddButtonClicked;
        Children.Add(mAddButton);
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
        mAddButton.IsVisible = mAddableElements.Any(a => !mPresentKeys.Contains(a.Key.Id));
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
                nextByKey.Add(id, new KeyedRow(mDataObject, key, cfg, () => RemoveKey(id)));
                structureChanged = true;
            }
            nextOrder.Add(id);
        }

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
        if (mDataObject == null)
            return;

        // 候选 = 尚未存在的键。
        var candidates = mAddableElements.Where(a => !mPresentKeys.Contains(a.Key.Id)).ToList();
        if (candidates.Count == 0)
            return;

        if (candidates.Count == 1)
        {
            AddKey(candidates[0]);
            return;
        }

        var menu = new ContextMenu();
        foreach (var candidate in candidates)
        {
            var captured = candidate;
            menu.Items.Add(new MenuItem().SetName(captured.Key.DisplayText ?? captured.Key.Id).SetAction(() => AddKey(captured)));
        }
        mAddButton.OpenContextMenu(menu);
    }

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
    Dictionary<string, KeyedRow> mRowsByKey = new();
    List<string> mOrder = new();
}

// 单个命名条目行：标签（DisplayText）在上、条目控件在下，悬浮在右上角浮出删除钮（不挤占布局）。
sealed class KeyedRow : IDisposable
{
    public Control Root => mRoot;
    public Type ConfigType => mWidget.ConfigType;

    public KeyedRow(IDataPropertyObject dataObject, PropertyKey key, IControllerConfig config, Action onDelete)
    {
        mWidget = ElementWidget.Create(dataObject, key.Id, config);
        mTitle = ArrayControlsFactory.MakeRowTitle(key.DisplayText ?? key.Id);

        var content = new StackPanel { Orientation = Orientation.Vertical };
        content.Children.Add(mTitle);
        content.Children.Add(mWidget.View);

        mRoot = new LayerPanel { Background = Brushes.Transparent };
        mRoot.Children.Add(content);

        mDelete = ArrayControlsFactory.MakeIconButton("✕");
        mDelete.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        mDelete.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        mDelete.Margin = new(0, 4, 4, 0);
        mDelete.Opacity = 0;
        mDelete.IsHitTestVisible = false;
        mDelete.Clicked += onDelete;
        mRoot.Children.Add(mDelete);

        mRoot.PointerEntered += (_, _) => { mDelete.Opacity = 1; mDelete.IsHitTestVisible = true; };
        mRoot.PointerExited += (_, _) => { mDelete.Opacity = 0; mDelete.IsHitTestVisible = false; };
    }

    public void Update(PropertyKey key, IControllerConfig config)
    {
        mTitle.Content = key.DisplayText ?? key.Id;   // 语言切换等仅 DisplayText 变：重贴标签、不重建
        mWidget.Update(config);
    }

    public void Dispose() => mWidget.Dispose();

    readonly LayerPanel mRoot;
    readonly Label mTitle;
    readonly Button mDelete;
    readonly ElementWidget mWidget;
}
