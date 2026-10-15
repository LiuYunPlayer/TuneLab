using System;
using System.Collections.Generic;
using TuneLab.GUI.Components;
using TuneLab.SDK;

namespace TuneLab.GUI.Controllers;

// 「+」菜单的候选 → 菜单项投影，列表（AddableElement）与键控容器（AddableKey）共用。候选的分组 / 分隔线与
// ComboBoxItem 同构，投影成同一套 DropDownItem 后交给 DropDownMenu，故 + 菜单与下拉在分组、分隔线、搜索上行为一致。
// 叶子的 Tag 携候选本身，选中后由控制器取其 Template 落默认值。
internal static class AddableMenu
{
    public static List<DropDownItem> Build(IReadOnlyList<AddableElement> addables)
    {
        var items = new List<DropDownItem>(addables.Count);
        foreach (var addable in addables)
        {
            if (addable.IsSeparator)
                items.Add(new DropDownItem() { Text = addable.Label ?? string.Empty, IsSeparator = true });
            else if (addable.SubItems is not null)
                items.Add(new DropDownItem() { Text = addable.Label ?? string.Empty, Children = Build(addable.SubItems) });
            else
                items.Add(new DropDownItem() { Text = addable.Label ?? string.Empty, Tag = addable });
        }
        return items;
    }

    // 键控候选要滤掉已存在的键（每键最多加一次）。过滤会让结构出现空洞，按「是不是因过滤才空」区分处理：
    // 原本就空的分组照样呈现（同下拉的空分组）；因过滤而变空的分组隐藏——里面的键都已加过，留着只是点不开的空壳；
    // 所辖区段因过滤而空了的分隔线也隐藏——否则会剩下孤零零的段标题、或两条线挨在一起。
    public static List<DropDownItem> Build(IReadOnlyList<AddableKey> addables, Func<string, bool> isPresent)
    {
        var items = new List<DropDownItem>(addables.Count);
        bool filtered = false;
        foreach (var addable in addables)
        {
            if (addable.IsSeparator)
            {
                items.Add(new DropDownItem() { Text = addable.Key.DisplayText ?? string.Empty, IsSeparator = true });
            }
            else if (addable.SubItems is not null)
            {
                var children = Build(addable.SubItems, isPresent);
                if (children.Count == 0 && addable.SubItems.Count > 0)
                {
                    filtered = true;
                    continue;
                }
                items.Add(new DropDownItem() { Text = addable.Key.DisplayText ?? string.Empty, Children = children });
            }
            else if (isPresent(addable.Key.Id))
            {
                filtered = true;
            }
            else
            {
                items.Add(new DropDownItem() { Text = addable.Key.DisplayText ?? addable.Key.Id, Tag = addable });
            }
        }
        return filtered ? DropEmptySections(items) : items;
    }

    // 去掉所辖区段为空的分隔线：区段 = 它之后、下一条分隔线（或末尾）之前的项。只在发生过过滤时调用，
    // 作者原样写下的结构不受影响。
    static List<DropDownItem> DropEmptySections(List<DropDownItem> items)
    {
        var result = new List<DropDownItem>(items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].IsSeparator)
            {
                int next = i + 1;
                if (next == items.Count || items[next].IsSeparator)
                    continue;   // 区段为空
            }
            result.Add(items[i]);
        }
        return result;
    }

    public static DropDownItem? FirstLeaf(IReadOnlyList<DropDownItem> items)
    {
        foreach (var item in items)
        {
            if (item.IsSeparator)
                continue;
            var leaf = item.IsGroup ? FirstLeaf(item.Children!) : item;
            if (leaf != null)
                return leaf;
        }
        return null;
    }

    public static int CountLeaves(IReadOnlyList<DropDownItem> items)
    {
        int count = 0;
        foreach (var item in items)
        {
            if (item.IsSeparator)
                continue;
            count += item.IsGroup ? CountLeaves(item.Children!) : 1;
        }
        return count;
    }
}
