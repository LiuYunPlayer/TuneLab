using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using TuneLab.Data;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Utils;
using TuneLab.GUI;
using TuneLab.Utils;

namespace TuneLab.UI;

internal class ParameterTabBar : Panel
{
    public event Action<string, ParameterButton.ButtonState>? StateChangeAsked;
    public ParameterButton PitchButton => mPitchButton;

    public interface IDependency
    {
        event Action? ActiveAutomationChanged;
        event Action? VisibleAutomationChanged;
        IProvider<IMidiPart> PartProvider { get; }
        string? ActiveAutomation { get; }
        bool IsAutomationVisible(string automationID);
    }

    public ParameterTabBar(IDependency dependency)
    {
        mDependency = dependency;

        Background = Back.ToBrush();

        mPitchButton = new ParameterButton(Color.Parse(ConstantDefine.PitchColor), ConstantDefine.PitchName);
        mPitchButton.State = ParameterButton.ButtonState.Visible;
        mPitchButton.StateChangeAsked += (state) => { mPitchButton.State = state; };

        mAutomationLayout = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(0, 9) }; ;
        Children.Add(mAutomationLayout);

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

    protected override Size MeasureOverride(Size availableSize)
    {
        mAutomationLayout.Measure(availableSize);
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        mAutomationLayout.Arrange(new Rect(finalSize));
        return finalSize;
    }

    void ResetAutomationButtons()
    {
        mAutomationButtons.Clear();
        mAutomationLayout.Children.Clear();

        if (Part == null)
            return;

        foreach (var kvp in Part.Voice.GetAutomationConfigs())
        {
            var config = kvp.Value;
            if (!mCacheParameterButtons.TryGetValue(kvp.Key, out var button))
            {
                button = new ParameterButton(Color.Parse(config.Color), config.Name);
                /*if(config.Name== Part.Voice.AutomationConfigs[0].Value.Name) button.Margin = new Thickness(0,0,12,0);*/
                button.Margin = new(6, 0);
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
        foreach (var kvp in Part.Voice.GetAutomationConfigs())
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

    public double AutoHeight => mAutomationLayout.DesiredSize.Height;
    IMidiPart? Part => mDependency.PartProvider.Object;

    Color Back => Style.INTERFACE;

    readonly WrapPanel mAutomationLayout;
    readonly ParameterButton mPitchButton;
    readonly OrderedMap<string, ParameterButton> mAutomationButtons = new();
    readonly Map<string, ParameterButton> mCacheParameterButtons = new();

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
