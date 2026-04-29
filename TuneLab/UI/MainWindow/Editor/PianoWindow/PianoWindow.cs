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
using TuneLab.GUI.Components;
using TuneLab.Base.Utils;
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
    public IProvider<IMidiPart> PartProvider => mPartProvider;
    public IProvider<ITimeline> TimelineProvider => mPartProvider;
    public IMidiPart? Part
    {
        get => mPartProvider.Object;
        set => mPartProvider.Set(value);
    }
    public ParameterButton PitchButton => mParameterTabBar.PitchButton;
    public PianoScrollView PianoScrollView => mPianoScrollView;
    public AutomationRenderer AutomationRenderer => mParameterContainer.AutomationRenderer;
    public IQuantization Quantization => mQuantization;
    public IPlayhead Playhead => mDependency.Playhead;
    public INotifiableProperty<PianoTool> PianoTool => mDependency.PianoTool;
    public INotifiableProperty<PlayScrollTarget> PlayScrollTarget => mDependency.PlayScrollTarget;
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
            Part?.DeselectAllAutomationPoints();
            mActiveAutomation = value;
            if (mActiveAutomation != null)
            {
                SetAutomationVisible(mActiveAutomation, true);
            }

            ActiveAutomationChanged?.Invoke();
        }
    }
    public IReadOnlyList<string> VisibleAutomations => mVisibleAutomations;
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

        ActiveAutomation = ConstantDefine.PreCommonAutomationConfigs[0].Key;
    }

    ~PianoWindow()
    {
        s.DisposeAll();
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
            var height = state == WindowState.Maximized ? Settings.ParameterPanelHeightMaximized.Value : Settings.ParameterPanelHeightNormal.Value;
            if (Math.Abs(mParameterHeight - height) < 0.1)
                return;

            mParameterHeight = height;
            Dispatcher.UIThread.Post(() => CorrectParameterHeight(false), DispatcherPriority.Background);
        }));
    }

    double GetStoredParameterHeight()
    {
        return Settings.MainWindowMaximized ? Settings.ParameterPanelHeightMaximized : Settings.ParameterPanelHeightNormal;
    }

    void StoreParameterHeight(double height)
    {
        Settings.ParameterPanelHeight.Value = height;
        if (mWindow != null && mWindow.WindowState == WindowState.Maximized)
        {
            Settings.ParameterPanelHeightMaximized.Value = height;
        }
        else
        {
            Settings.ParameterPanelHeightNormal.Value = height;
        }
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
    const double PARAMETER_TITLE_BAR_HEIGHT = 20;

    double mParameterHeight = 200;

    string? mActiveAutomation;
    readonly List<string> mVisibleAutomations = new();

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

    readonly Owner<IMidiPart> mPartProvider = new();

    readonly IDependency mDependency;
    bool mWindowStateBound = false;
    Window? mWindow;
}
