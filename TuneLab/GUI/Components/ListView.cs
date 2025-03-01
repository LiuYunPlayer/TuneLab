using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace TuneLab.GUI.Components;

internal class ListView : ScrollView
{
    public new StackPanel Content => mContent;
    public Orientation Orientation
    {
        get => mContent.Orientation;
        set
        {
            mContent.Orientation = value;
            FitWidth = value == Orientation.Vertical;
            FitHeight = value == Orientation.Horizontal;
        }
    }

    public ListView()
    {
        Orientation = Orientation.Vertical;
        base.Content = mContent;
    }

    class ListViewStackPsnel : StackPanel
    {
        protected override Size ArrangeOverride(Size finalSize)
        {
            Controls children = base.Children;
            bool flag = Orientation == Orientation.Horizontal;
            Rect rect = new Rect(finalSize);
            double num = 0.0;
            double spacing = Spacing;
            int i = 0;
            for (int count = children.Count; i < count; i++)
            {
                Control control = children[i];
                if (control.IsVisible)
                {
                    if (flag)
                    {
                        rect = rect.WithX(rect.X + num);
                        num = control.DesiredSize.Width;
                        rect = rect.WithWidth(num).WithHeight(finalSize.Height);
                        num += spacing;
                    }
                    else
                    {
                        rect = rect.WithY(rect.Y + num);
                        num = control.DesiredSize.Height;
                        rect = rect.WithHeight(num).WithWidth(finalSize.Width);
                        num += spacing;
                    }

                    ArrangeChild(control, rect, finalSize, Orientation);
                }
            }

            //RaiseEvent(new RoutedEventArgs((Orientation == Orientation.Horizontal) ? HorizontalSnapPointsChanged : VerticalSnapPointsChanged));
            return finalSize;
        }

        internal virtual void ArrangeChild(Control child, Rect rect, Size panelSize, Orientation orientation)
        {
            child.Arrange(rect);
        }
    }

    readonly ListViewStackPsnel mContent = new();
}
