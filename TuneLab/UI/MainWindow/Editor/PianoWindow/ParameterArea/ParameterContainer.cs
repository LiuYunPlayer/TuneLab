using Avalonia;
using Avalonia.Controls;

namespace TuneLab.UI;

internal class ParameterContainer : Panel
{
    public AutomationRenderer AutomationRenderer => mAutomationRenderer;

    public ParameterContainer(AutomationRenderer.IDependency dependency)
    {
        mAutomationRenderer = new AutomationRenderer(dependency);
        Children.Add(mAutomationRenderer);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        mAutomationRenderer.Arrange(new(finalSize));
        return finalSize;
    }

    readonly AutomationRenderer mAutomationRenderer;
}
