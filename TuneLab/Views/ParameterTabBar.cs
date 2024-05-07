using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.GUI;
using TuneLab.Data;
using TuneLab.Base.Utils;
using TuneLab.Base.Event;
using TuneLab.Utils;

namespace TuneLab.Views;

internal class ParameterTabBar : Panel
{
    public event Action<string, ParameterButton.ButtonState>? StateChangeAsked;
    public ParameterButton PitchButton => mPitchButton;

    public interface IDependency
    {
        event Action? ActiveAutomationChanged;
        event Action? VisibleAutomationChanged;
        IProvider<MidiPart> PartProvider { get; }
        string? ActiveAutomation { get; }
        bool IsAutomationVisible(string automationID);
    }

    public ParameterTabBar(IDependency dependency)
    {
        mDependency = dependency;

        Background = Back.ToBrush();

        mParameterLayout = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 9), Spacing = 12 };
        Children.Add(mParameterLayout);
        mPitchButton = new ParameterButton(Color.Parse(ConstantDefine.PitchColor), ConstantDefine.PitchName);
        mPitchButton.State = ParameterButton.ButtonState.Visible;
        mPitchButton.StateChangeAsked += (state) => { mPitchButton.State = state; };
        mParameterLayout.Children.Add(mPitchButton);
        mParameterLayout.Children.Add(new Border() { Background = Style.DARK.ToBrush(), Width = 2, Height = 18, CornerRadius = new CornerRadius(1) });
        mAutomationLayout = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0), Spacing = 12 };
        mParameterLayout.Children.Add(mAutomationLayout);

        mDependency.PartProvider.ObjectChanged.Subscribe(OnPartChanged, s);
        mDependency.PartProvider.When(p => p.Voice.Modified).Subscribe(OnAutomationConfigsChanged, s);
        mDependency.ActiveAutomationChanged += SyncAutomationButtonsState;
        mDependency.VisibleAutomationChanged += SyncAutomationButtonsState;

        OnPartChanged();
    }

    ~ParameterTabBar()
    {
        s.DisposeAll();
        mDependency.ActiveAutomationChanged -= SyncAutomationButtonsState;
        mDependency.VisibleAutomationChanged -= SyncAutomationButtonsState;
    }

    public void SetState(string automationID, ParameterButton.ButtonState state)
    {
        if (mAutomationButtons.ContainsKey(automationID))
            mAutomationButtons[automationID].State = state;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double horizontalMargin = (finalSize.Width - mParameterLayout.DesiredSize.Width) / 2;
        mParameterLayout.Arrange(new Rect(finalSize).Adjusted(horizontalMargin, 0, -horizontalMargin, 0));
        return finalSize;
    }

    void ResetAutomationButtons()
    {
        mAutomationButtons.Clear();
        mAutomationLayout.Children.Clear();

        if (Part == null)
            return;

        foreach (var kvp in Part.Voice.AutomationConfigs)
        {
            var config = kvp.Value;
            if (!mCacheParameterButtons.TryGetValue(kvp.Key, out var button))
            {
                button = new ParameterButton(Color.Parse(config.Color), config.Name);
                mCacheParameterButtons.Add(kvp.Key, button);
            }
            mAutomationButtons.Add(kvp.Key, button);
        }

        foreach (var kvp in mAutomationButtons)
        {
            var automationID = kvp.Key;
            var button = kvp.Value;
            button.StateChangeAsked += (state) =>
            {
                StateChangeAsked?.Invoke(automationID, state);
            };
            mAutomationLayout.Children.Add(button);
        }
    }

    void SyncAutomationButtonsState()
    {
        if (Part == null)
            return;

        var activeAutomation = mDependency.ActiveAutomation;
        foreach (var kvp in Part.Voice.AutomationConfigs)
        {
            var automationID = kvp.Key;
            if (activeAutomation == automationID)
                SetState(automationID, ParameterButton.ButtonState.Edit);
            else
                SetState(automationID, mDependency.IsAutomationVisible(automationID) ? ParameterButton.ButtonState.Visible : ParameterButton.ButtonState.Off);
        }
    }

    void OnPartChanged()
    {
        OnAutomationConfigsChanged();
    }

    void OnAutomationConfigsChanged()
    {
        ResetAutomationButtons();
        SyncAutomationButtonsState();
    }

    MidiPart? Part => mDependency.PartProvider.Object;

    Color Back => Style.INTERFACE;

    readonly StackPanel mParameterLayout;
    readonly StackPanel mAutomationLayout;
    readonly ParameterButton mPitchButton;
    readonly OrderedMap<string, ParameterButton> mAutomationButtons = new();
    readonly Map<string, ParameterButton> mCacheParameterButtons = new();

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
