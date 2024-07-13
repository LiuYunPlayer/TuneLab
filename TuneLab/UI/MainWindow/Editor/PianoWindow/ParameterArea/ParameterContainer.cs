using Avalonia;
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.GUI;
using TuneLab.Base.Utils;

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
