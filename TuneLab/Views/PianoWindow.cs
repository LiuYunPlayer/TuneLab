using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Base.Event;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Base.Science;
using TuneLab.Utils;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;

namespace TuneLab.Views;

internal class PianoWindow : Panel, PianoRoll.IDependency, PianoScrollView.IDependency, TimelineScrollView.IDependency, ParameterTabBar.IDependency, AutomationRenderer.IDependency, PlayheadLayer.IDependency, FunctionBar.IDependency
{
    public event Action? ActiveAutomationChanged;
    public event Action? VisibleAutomationChanged;
    public IActionEvent PianoToolChanged => mPianoToolChanged;
    public IActionEvent IsAutoPageChanged => mIsAutoPageChanged;
    public IActionEvent WaveformBottomChanged => mWaveformBottomChanged;
    public TickAxis TickAxis => mTickAxis;
    public PitchAxis PitchAxis => mPitchAxis;
    public IProvider<MidiPart> PartProvider => mPartProvider;
    public IProvider<ITimeline> TimelineProvider => mPartProvider;
    public MidiPart? Part
    {
        get => mPartProvider.Object;
        set => mPartProvider.Set(value);
    }
    public ParameterButton PitchButton => mParameterTabBar.PitchButton;
    public PianoScrollView PianoScrollView => mPianoScrollView;
    public IQuantization Quantization => mQuantization;
    public IPlayhead Playhead => mDependency.Playhead;
    public PianoTool PianoTool { get => mPianoTool; set { if (mPianoTool == value) return; mPianoTool = value; mPianoToolChanged.Invoke(); } }
    public bool IsAutoPage { get => mIsAutoPage; set { if (mIsAutoPage == value) return; mIsAutoPage = value; mIsAutoPageChanged.Invoke(); } }
    public string? ActiveAutomation
    {
        get
        {
            if (Part == null)
                return null;

            if (mActiveAutomation != null && IsAutomationVisible(mActiveAutomation))
                return mActiveAutomation;

            return ConstantDefine.PreCommonAutomationConfigs[0].Key;
        }
        set
        {
            mActiveAutomation = value;
            if (mActiveAutomation != null)
            {
                SetAutomationVisible(mActiveAutomation, true);
            }

            ActiveAutomationChanged?.Invoke();
        }
    }
    public IReadOnlyList<string> VisibleAutomations => mVisibleAutomations;
    public double WaveformBottom => mParameterTitleBar.Bounds.Top - TIME_AXIS_HEIGHT;
    public interface IDependency
    {
        IPlayhead Playhead { get; }
    }

    public PianoWindow(IDependency dependency)
    {
        mDependency = dependency;

        mTickAxis = new TickAxis();
        mPitchAxis = new PitchAxis();
        mQuantization = new Quantization(MusicTheory.QuantizationBase.Base_1, MusicTheory.QuantizationDivision.Division_8);

        mParameterTabBar = new ParameterTabBar(this);
        Children.Add(mParameterTabBar);

        mPianoScrollView = new PianoScrollView(this);
        Children.Add(mPianoScrollView);

        mPianoTimelineScrollView = new TimelineScrollView(this);
        Children.Add(mPianoTimelineScrollView);

        mPlayheadLayer = new PlayheadLayer(this);
        Children.Add(mPlayheadLayer);

        mParameterContainer = new ParameterContainer(this);
        Children.Add(mParameterContainer);

        mParameterTitleBar = new ParameterTitleBar();
        Children.Add(mParameterTitleBar);

        mPianoRoll = new PianoRoll(this);
        Children.Add(mPianoRoll);

        var box = new Border { Background = GUI.Style.DARK.ToBrush() };
        Children.Add(box);
        box.Arrange(new Rect(0, 0, ROLL_WIDTH, TIME_AXIS_HEIGHT));

        mParameterTabBar.StateChangeAsked += OnParameterTabBarStateChangeAsked;
        mParameterTitleBar.Moved += top => SetParameterHeight(Bounds.Height - top - PARAMETER_TITLE_BAR_HEIGHT - PARAMETER_TAB_BAR_HEIGHT);

        ClipToBounds = true;
        MinWidth = ROLL_WIDTH;
        MinHeight = TIME_AXIS_HEIGHT + PARAMETER_TITLE_BAR_HEIGHT + PARAMETER_TAB_BAR_HEIGHT;

        ActiveAutomation = ConstantDefine.PreCommonAutomationConfigs[0].Key;
    }

    ~PianoWindow()
    {

    }

    public bool IsAutomationVisible(string automationID)
    {
        if (Part == null)
            return false;

        if (!Part.IsEffectiveAutomation(automationID))
            return false;

        return mVisibleAutomations.Contains(automationID);
    }

    public void SetAutomationVisible(string automationID, bool isVisible)
    {
        mVisibleAutomations.Remove(automationID);

        if (isVisible)
            mVisibleAutomations.Add(automationID);

        VisibleAutomationChanged?.Invoke();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        mTickAxis.ViewLength = Bounds.Width - ROLL_WIDTH;
        mPitchAxis.ViewLength = Bounds.Height - TIME_AXIS_HEIGHT - PARAMETER_TAB_BAR_HEIGHT;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(Math.Max(availableSize.Width, MinWidth), Math.Max(availableSize.Height, MinHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double parameterHeight = mParameterHeight.Limit(0, finalSize.Height - TIME_AXIS_HEIGHT - PARAMETER_TAB_BAR_HEIGHT - PARAMETER_TITLE_BAR_HEIGHT);
        mPianoScrollView.Arrange(new Rect(finalSize).Adjusted(ROLL_WIDTH, TIME_AXIS_HEIGHT, 0, -PARAMETER_TAB_BAR_HEIGHT));
        mPianoTimelineScrollView.Arrange(new Rect(ROLL_WIDTH, 0, finalSize.Width - ROLL_WIDTH, TIME_AXIS_HEIGHT));
        mPlayheadLayer.Arrange(new Rect(ROLL_WIDTH, 0, finalSize.Width - ROLL_WIDTH, finalSize.Height - PARAMETER_TAB_BAR_HEIGHT));
        mParameterContainer.Arrange(new Rect(ROLL_WIDTH, finalSize.Height - PARAMETER_TAB_BAR_HEIGHT - parameterHeight, finalSize.Width - ROLL_WIDTH, parameterHeight));
        mParameterTitleBar.Arrange(new Rect(ROLL_WIDTH, finalSize.Height - PARAMETER_TAB_BAR_HEIGHT - parameterHeight - PARAMETER_TITLE_BAR_HEIGHT, finalSize.Width - ROLL_WIDTH, PARAMETER_TITLE_BAR_HEIGHT));
        mPianoRoll.Arrange(new Rect(0, TIME_AXIS_HEIGHT, ROLL_WIDTH, finalSize.Height - PARAMETER_TAB_BAR_HEIGHT - TIME_AXIS_HEIGHT));
        mParameterTabBar.Arrange(new Rect(0, finalSize.Height - PARAMETER_TAB_BAR_HEIGHT, finalSize.Width, PARAMETER_TAB_BAR_HEIGHT));
        return finalSize;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.IsHandledByTextBox())
            return;

        switch (PianoScrollView.OperationState)
        {
            case PianoScrollView.State.None:
                e.Handled = true;
                if (e.Match(Key.Delete))
                {
                    PianoScrollView.Delete();
                }
                else if (e.Match(Key.C, ModifierKeys.Ctrl))
                {
                    PianoScrollView.Copy();
                }
                else if (e.Match(Key.X, ModifierKeys.Ctrl))
                {
                    PianoScrollView.Cut();
                }
                else if (e.Match(Key.V, ModifierKeys.Ctrl))
                {
                    PianoScrollView.Paste();
                }
                else if (e.Match(Key.A, ModifierKeys.Ctrl))
                {
                    switch (mPianoTool)
                    {
                        case PianoTool.Note:
                            Part?.Notes.SelectAllItems();
                            break;
                        case PianoTool.Vibrato:
                            Part?.Vibratos.SelectAllItems();
                            break;
                        default:
                            break;
                    }
                    
                }
                else if (e.Match(Key.Up, ModifierKeys.Shift))
                {
                    PianoScrollView.OctaveUp();
                }
                else if (e.Match(Key.Down, ModifierKeys.Shift))
                {
                    PianoScrollView.OctaveDown();
                }
                else if (e.Match(Key.Up))
                {
                    PianoScrollView.ChangeKey(+1);
                }
                else if (e.Match(Key.Down))
                {
                    PianoScrollView.ChangeKey(-1);
                }
                else
                {
                    e.Handled = false;
                }
                break;
        }
    }

    void SetParameterHeight(double height)
    {
        mParameterHeight = height.Limit(0, Bounds.Height - TIME_AXIS_HEIGHT - PARAMETER_TAB_BAR_HEIGHT - PARAMETER_TITLE_BAR_HEIGHT);
        mWaveformBottomChanged.Invoke();
        InvalidateArrange();
    }

    void OnParameterTabBarStateChangeAsked(string automationID, ParameterButton.ButtonState state)
    {
        if (state == ParameterButton.ButtonState.Edit)
            ActiveAutomation = automationID;
        else
            SetAutomationVisible(automationID, state == ParameterButton.ButtonState.Visible);
    }

    const double TIME_AXIS_HEIGHT = 48;
    const double ROLL_WIDTH = 64;
    const double PARAMETER_TAB_BAR_HEIGHT = 42;
    const double PARAMETER_TITLE_BAR_HEIGHT = 20;

    double mParameterHeight = 200;

    string? mActiveAutomation;
    readonly List<string> mVisibleAutomations = new();
    PianoTool mPianoTool = PianoTool.Note;
    bool mIsAutoPage = false;

    readonly ActionEvent mPianoToolChanged = new();
    readonly ActionEvent mIsAutoPageChanged = new();
    readonly ActionEvent mWaveformBottomChanged = new();

    readonly Quantization mQuantization;
    readonly TickAxis mTickAxis;
    readonly PitchAxis mPitchAxis;

    readonly PianoScrollView mPianoScrollView;
    readonly TimelineScrollView mPianoTimelineScrollView;
    readonly PlayheadLayer mPlayheadLayer;
    readonly ParameterContainer mParameterContainer;
    readonly ParameterTitleBar mParameterTitleBar;
    readonly PianoRoll mPianoRoll;
    readonly ParameterTabBar mParameterTabBar;

    readonly Owner<MidiPart> mPartProvider = new();

    readonly IDependency mDependency;
}
