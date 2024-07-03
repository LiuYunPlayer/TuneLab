using Avalonia.Controls.Documents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Event;

namespace TuneLab.Data;

internal interface ISelectable
{
    IActionEvent SelectionChanged { get; }
    bool IsSelected { get; set; }
}

internal interface ISelectableCollection<T> : IEnumerable<T> where T : ISelectable
{
    IMergableEvent SelectionChanged { get; }
}

internal static class ISelectableExtension
{
    public static void Select(this ISelectable selectable)
    {
        selectable.IsSelected = true;
    }

    public static void Deselect(this ISelectable selectable)
    {
        selectable.IsSelected = false;
    }

    public static void Inselect(this ISelectable selectable)
    {
        selectable.IsSelected = !selectable.IsSelected;
    }

    public static IReadOnlyCollection<T> AllSelectedItems<T>(this IEnumerable<T> selectableCollection) where T : ISelectable
    {
        List<T> results = new();
        foreach (var selectable in selectableCollection)
        {
            if (selectable.IsSelected)
                results.Add(selectable);
        }
        return results;
    }

    public static void SelectAllItems<T>(this IEnumerable<T> selectableCollection) where T : ISelectable
    {
        bool canMerge = selectableCollection is ISelectableCollection<T>;
        if (canMerge) ((ISelectableCollection<T>)selectableCollection).SelectionChanged.BeginMerge();
        foreach (var item in selectableCollection)
        {
            item.IsSelected = true;
        }
        if (canMerge) ((ISelectableCollection<T>)selectableCollection).SelectionChanged.EndMerge();
    }

    public static void DeselectAllItems<T>(this IEnumerable<T> selectableCollection) where T : ISelectable
    {
        bool canMerge = selectableCollection is ISelectableCollection<T>;
        if (canMerge) ((ISelectableCollection<T>)selectableCollection).SelectionChanged.BeginMerge();
        foreach (var item in selectableCollection)
        {
            item.IsSelected = false;
        }
        if (canMerge) ((ISelectableCollection<T>)selectableCollection).SelectionChanged.EndMerge();
    }

    public static bool HasSelectedItem<T>(this IEnumerable<T> selectableCollection) where T : ISelectable
    {
        foreach (var item in selectableCollection)
        {
            if (item.IsSelected)
                return true;
        }

        return false;
    }
}
