using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation.Event;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.Foundation.Science;
using TuneLab.Utils;
using TuneLab.SDK.Format.DataInfo;
using TuneLab.SDK.Voice;
using TuneLab.SDK.Base;
using TuneLab.GUI.Components;
using TuneLab.Foundation.Utils;
using TuneLab.Configs;
using Avalonia.Threading;

namespace TuneLab.UI;

internal class PianoWindow : DockPanel, PianoRoll.IDependency, PianoScrollView.IDependency, TimelineView.IDependency, ParameterTabBar.IDependency, AutomationRenderer.IDependency, PlayheadLayer.IDependency
{
    public event Action? ActiveAutomationChanged;
    public event Action? VisibleAutomationChanged;
    public IActionEvent WaveformBottomChanged => mWaveformBottomChanged;
    public TickAxis TickAxis => mTickAxis;
    public PitchAxis PitchAxis => mPitchAxis;
    public IHolder<IMidiPart> PartHolder => mPartHolder;
    public IHolder<ITimeline> TimelineHolder => mPartHolder;
    public IMidiPart? Part
    {
        get => mPartHolder.Value;
        set => mPartHolder.Set(value);
    }
    public ParameterButton PitchButton => mParameterTabBar.PitchButton;
    public PianoScrollView PianoScrollView => mPianoScrollView;
    public AutomationRenderer AutomationRenderer => mParameterContainer.AutomationRenderer;
    public IQuantization Quantization => mQuantization;
    public IPlayhead Playhead => mDependency.Playhead;
    public INotifiableProperty<PianoTool> PianoTool => mDependency.PianoTool;
    public INotifiableProperty<PlayScrollTarget> PlayScrollTarget => mDependency.PlayScrollTarget;
    public AutomationKey? ActiveAutomation
    {
        get
        {
            if (Part == null)
                return null;

            if (mActiveAutomation.HasValue && IsAutomationVisible(mActiveAutomation.Value))
                return mActiveAutomation.Value;

            return AutomationKey.Voice(ConstantDefine.PreCommonAutomationConfigs[0].Key);
        }
        set
        {
            Part?.DeselectAllAutomationPoints();
            mActiveAutomation = value;
            if (mActiveAutomation.HasValue)
            {
                SetAutomationVisible(mActiveAutomation.Value, true);
            }

            ActiveAutomationChanged?.Invoke();
        }
    }
    public IReadOnlyList<AutomationKey> VisibleAutomations => mVisibleAutomations;
    public double WaveformBottom => mPianoScrollView.Bounds.Height - mParameterTitleBar.Bounds.Height - mParameterContainer.Bounds.Height;
    public interface IDependency
    {
        IPlayhead Playhead { get; }
        INotifiableProperty<PianoTool> PianoTool { get; }
        INotifiableProperty<PlayScrollTarget> PlayScrollTarget { get; }
    }

    public PianoWindow(IDependency dependency)
    {
        mDependency = dependency;
        mParameterHeight = GetStoredParameterHeight();

        mTickAxis = new TickAxis();
        mPitchAxis = new PitchAxis();
        mQuantization = new Quantization(MusicTheory.QuantizationBase.Base_1, MusicTheory.QuantizationDivision.Division_8);

        mParameterTabBar = new ParameterTabBar(this);
        this.AddDock(mParameterTabBar, Dock.Bottom);

        var leftPanel = new DockPanel() { Width = ROLL_WIDTH, ClipToBounds = true };
        {
            var box = new Border { Background = GUI.Style.DARK.ToBrush(), Height = TIME_AXIS_HEIGHT };
            leftPanel.AddDock(box, Dock.Top);

            mPianoRoll = new PianoRoll(this);
            leftPanel.AddDock(mPianoRoll);
        }
        this.AddDock(leftPanel, Dock.Left);

        var layerPanel = new LayerPanel() { ClipToBounds = true };
        {
            var pianoLayer = new DockPanel();
            {
                mPianoTimelineView = new TimelineView(this);
                pianoLayer.AddDock(mPianoTimelineView, Dock.Top);

                var pianoScrollViewPanel = new LayerPanel() { ClipToBounds = true };
                {
                    mPianoScrollView = new PianoScrollView(this);
                    pianoScrollViewPanel.Children.Add(mPianoScrollView);

                    mParameterLayer = new DockPanel() { LastChildFill = false, MinHeight = PARAMETER_TITLE_BAR_HEIGHT };
                    {
                        mParameterContainer = new ParameterContainer(this) { Height = mParameterHeight };
                        mParameterLayer.AddDock(mParameterContainer, Dock.Bottom);

                        mParameterTitleBar = new ParameterTitleBar() { Height = PARAMETER_TITLE_BAR_HEIGHT };
                        mParameterLayer.AddDock(mParameterTitleBar, Dock.Bottom);
                    }
                    pianoScrollViewPanel.Children.Add(mParameterLayer);
                }
                pianoLayer.AddDock(pianoScrollViewPanel);

                pianoScrollViewPanel.SizeChanged += (s, e) =>
                {
                    mTickAxis.ViewLength = e.NewSize.Width;
                    mPitchAxis.ViewLength = e.NewSize.Height;
                };
            }
            layerPanel.Children.Add(pianoLayer);

            mPlayheadLayer = new PlayheadLayer(this);
            layerPanel.Children.Add(mPlayheadLayer);
        }
        this.AddDock(layerPanel);

        mParameterTabBar.StateChangeAsked += OnParameterTabBarStateChangeAsked;
        mParameterTitleBar.Moved += top =>
        {
            mParameterHeight = mParameterLayer.Bounds.Height - top - mParameterTitleBar.Bounds.Height;
            CorrectParameterHeight(true);
        };
        mParameterLayer.SizeChanged += (s, e) => { CorrectParameterHeight(false); };
        AttachedToVisualTree += (s, e) => BindWindowState();

        ClipToBounds = true;

        ActiveAutomation = AutomationKey.Voice(ConstantDefine.PreCommonAutomationConfigs[0].Key);
    }

    ~PianoWindow()
    {
        s.DisposeAll();
    }

    public bool IsAutomationVisible(AutomationKey automation)
    {
        if (Part == null)
            return false;

        if (!Part.IsEffectiveAutomation(automation))
            return false;

        return mVisibleAutomations.Contains(automation);
    }

    public void SetAutomationVisible(AutomationKey automation, bool isVisible)
    {
        mVisibleAutomations.Remove(automation);

        if (isVisible)
            mVisibleAutomations.Add(automation);

        VisibleAutomationChanged?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.IsHandledByTextBox())
            return;

        var automationRenderer = mParameterContainer.AutomationRenderer;
        if (automationRenderer.IsOperating)
            return;

        bool preferAutomationAnchorActions = PianoTool.Value == UI.PianoTool.Anchor && automationRenderer.IsHover;

        switch (PianoScrollView.OperationState)
        {
            case PianoScrollView.State.None:
                e.Handled = true;
                if (e.Match(Key.Delete))
                {
                    if (preferAutomationAnchorActions)
                        automationRenderer.DeleteSelectedAnchors();
                    else
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
                    switch (PianoTool.Value)
                    {
                        case UI.PianoTool.Note:
                            Part?.Notes.SelectAllItems();
                            break;
                        case UI.PianoTool.Vibrato:
                            Part?.Vibratos.SelectAllItems();
                            break;
                        case UI.PianoTool.Anchor:
                            if (automationRenderer.IsHover)
                                automationRenderer.SelectAllAnchors();
                            else
                            {
                                Part?.DeselectAllAutomationPoints();
                                automationRenderer.InvalidateVisual();
                                automationRenderer.RefreshAnchorValueInput();
                                Part?.Pitch.SelectAllAnchors();
                            }
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


    void CorrectParameterHeight(bool saveHeight = false)
    {
        var displayHeight = mParameterHeight.Limit(0, mParameterLayer.Bounds.Height - mParameterTitleBar.Bounds.Height);
        mParameterContainer.Height = displayHeight;
        if (saveHeight)
        {
            mParameterHeight = displayHeight;
            StoreParameterHeight(mParameterHeight);
        }
        mWaveformBottomChanged.Invoke();
    }

    void BindWindowState()
    {
        if (mWindowStateBound)
            return;

        mWindow = this.Window();
        mWindowStateBound = true;
        s.Add(mWindow.GetObservable(Window.WindowStateProperty).Subscribe(state =>
        {
            var height = state == WindowState.Maximized ? EditorState.ParameterPanelHeightMaximized.Value : EditorState.ParameterPanelHeightNormal.Value;
            if (Math.Abs(mParameterHeight - height) < 0.1)
                return;

            mParameterHeight = height;
            Dispatcher.UIThread.Post(() => CorrectParameterHeight(false), DispatcherPriority.Background);
        }));
    }

    double GetStoredParameterHeight()
    {
        return EditorState.MainWindowMaximized ? EditorState.ParameterPanelHeightMaximized : EditorState.ParameterPanelHeightNormal;
    }

    void StoreParameterHeight(double height)
    {
        EditorState.ParameterPanelHeight.Value = height;
        if (mWindow != null && mWindow.WindowState == WindowState.Maximized)
        {
            EditorState.ParameterPanelHeightMaximized.Value = height;
        }
        else
        {
            EditorState.ParameterPanelHeightNormal.Value = height;
        }
    }

    void OnParameterTabBarStateChangeAsked(AutomationKey automation, ParameterButton.ButtonState state)
    {
        if (state == ParameterButton.ButtonState.Edit)
            ActiveAutomation = automation;
        else
            SetAutomationVisible(automation, state == ParameterButton.ButtonState.Visible);
    }

    const double TIME_AXIS_HEIGHT = 48;
    const double ROLL_WIDTH = 64;
    const double PARAMETER_TITLE_BAR_HEIGHT = 20;

    double mParameterHeight = 200;

    AutomationKey? mActiveAutomation;
    readonly List<AutomationKey> mVisibleAutomations = new();

    readonly ActionEvent mWaveformBottomChanged = new();
    readonly DisposableManager s = new();

    readonly Quantization mQuantization;
    readonly TickAxis mTickAxis;
    readonly PitchAxis mPitchAxis;

    readonly PianoScrollView mPianoScrollView;
    readonly TimelineView mPianoTimelineView;
    readonly PlayheadLayer mPlayheadLayer;
    readonly ParameterContainer mParameterContainer;
    readonly ParameterTitleBar mParameterTitleBar;
    readonly PianoRoll mPianoRoll;
    readonly ParameterTabBar mParameterTabBar;
    readonly DockPanel mParameterLayer;

    readonly Holder<IMidiPart> mPartHolder = new();

    readonly IDependency mDependency;
    bool mWindowStateBound = false;
    Window? mWindow;
}
