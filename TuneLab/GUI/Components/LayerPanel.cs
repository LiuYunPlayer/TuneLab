using Avalonia;
using Avalonia.Controls;

namespace TuneLab.GUI.Components;

internal class LayerPanel : Panel
{
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            child.Arrange(new(finalSize));
        }

        return finalSize;
    }
}
