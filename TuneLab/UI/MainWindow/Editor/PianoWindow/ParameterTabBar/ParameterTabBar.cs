using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Data;
using TuneLab.I18N;
using TuneLab.Utils;
using TuneLab.SDK;
using Color = Avalonia.Media.Color;
using Size = Avalonia.Size;
using Rect = Avalonia.Rect;

namespace TuneLab.UI;

internal class ParameterTabBar : Panel
{
    public event Action<AutomationKey, ParameterButton.ButtonState>? StateChangeAsked;
    public ParameterButton PitchButton => mPitchButton;

    public interface IDependency
    {
        event Action? ActiveAutomationChanged;
        event Action? VisibleAutomationChanged;
        IHolder<IMidiPart> PartHolder { get; }
        AutomationKey? ActiveAutomation { get; }
        bool IsAutomationVisible(AutomationKey automation);
        // 参数区藏起来（Ctrl+P）时没有哪个参数在显示：激活的那个也不点亮，看着像"都没开"——点一个就出来。
        IActionEvent ParameterPanelVisibilityChanged { get; }
        bool IsParameterPanelVisible { get; }
        // 「+」：在焦点面板下方加一块照着它设置的新面板（参数区藏着时只是把它显示出来）。
        void AddParameterPanel();
        // 右键「+」：关掉焦点面板（熟手的快捷方式；直观入口是各面板标题栏最右的「×」）。只剩一块时不关。
        void CloseFocusedParameterPanel();
        // 波形带显隐：全局的独立开关，不属于哪块面板。
        IActionEvent WaveformVisibleChanged { get; }
        bool IsWaveformVisible { get; }
        void SetWaveformVisible(bool isVisible);
    }

    public ParameterTabBar(IDependency dependency)
    {
        mDependency = dependency;

        Background = Back.ToBrush();

        mPitchButton = new ParameterButton(Color.Parse(ConstantDefine.PitchColor), ConstantDefine.PitchName);
        mPitchButton.State = ParameterButton.ButtonState.Visible;
        mPitchButton.StateChangeAsked += (state) => { mPitchButton.State = state; };

        // 左右各让出钢琴键列宽：左侧留给波形开关与「+」（避免轨多时居中流式布局铺过来重叠），右侧对称、保持轨按钮相对窗口居中。
        mAutomationLayout = new() { Orientation = Orientation.Horizontal, HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(PianoWindow.ROLL_WIDTH, 9) };
        Children.Add(mAutomationLayout);

        // 左侧两个按钮的配色照工具栏：开关（同钢琴窗工具按钮）开 = 高亮圆角底 + 白图标、关 = 无底 + 半透明图标；
        // 「+」是常用操作，图标纯白。悬停都只加一层淡底。
        var hoverBack = Colors.White.Opacity(0.05);

        // 最左侧：波形带显隐开关。钉在 tabbar——它和「+」都作用于整个参数区，不属于哪块面板。
        mWaveformToggle = new Toggle() { Width = 24, Height = 24 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { Color = Style.HIGH_LIGHT }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.Waveform }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });
        mWaveformToggle.SetupToolTip("Waveform".Tr(TC.Document));
        mWaveformToggle.Switched.Subscribe(() => mDependency.SetWaveformVisible(mWaveformToggle.IsChecked));
        mDependency.WaveformVisibleChanged.Subscribe(() => mWaveformToggle.Display(mDependency.IsWaveformVisible), s);
        mWaveformToggle.Display(mDependency.IsWaveformVisible);
        Children.Add(mWaveformToggle);

        // 波形开关右边：「+」加一块参数面板（照着焦点面板，插在它下方）。同样钉在 tabbar、不随面板改高移动。
        mAddPanelButton = new TuneLab.GUI.Components.Button() { Width = 24, Height = 24 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.Plus }, ColorSet = new() { Color = Colors.White } });
        mAddPanelButton.SetupToolTip("Add Parameter Panel".Tr(TC.Menu));
        // 自绘按钮的 Clicked 不分左右键（右键松开也算点击），故先在隧道阶段记下这次松开的是哪个键，点击时再分流：
        // 左键加面板、右键关焦点面板。（隧道先于按钮自身的松开处理，记下的总是这一次的。）
        bool rightReleased = false;
        mAddPanelButton.AddHandler(PointerReleasedEvent, (_, e) => rightReleased = e.InitialPressMouseButton == Avalonia.Input.MouseButton.Right,
            Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        mAddPanelButton.Clicked += () =>
        {
            if (rightReleased)
                mDependency.CloseFocusedParameterPanel();
            else
                mDependency.AddParameterPanel();
        };
        Children.Add(mAddPanelButton);

        mDependency.PartHolder.Modified.Subscribe(OnPartChanged, s);
        mDependency.PartHolder.When(p => p.SoundSource.Modified).Subscribe(OnAutomationConfigsChanged, s);
        mDependency.PartHolder.When(p => p.Effects.MembershipModified).Subscribe(OnAutomationConfigsChanged, s);
        // 条件自动化轨随参数 commit 显隐 → 重建轨按钮（仅轨集合实变时触发）。
        mDependency.PartHolder.When(p => p.AutomationConfigsModified).Subscribe(OnAutomationConfigsChanged, s);
        mDependency.ActiveAutomationChanged += SyncAutomationButtonsState;
        mDependency.VisibleAutomationChanged += SyncAutomationButtonsState;
        mDependency.ParameterPanelVisibilityChanged.Subscribe(SyncAutomationButtonsState, s);

        OnPartChanged();
    }

    ~ParameterTabBar()
    {
        s.DisposeAll();
        mDependency.ActiveAutomationChanged -= SyncAutomationButtonsState;
        mDependency.VisibleAutomationChanged -= SyncAutomationButtonsState;
    }

    public void SetState(AutomationKey automation, ParameterButton.ButtonState state)
    {
        if (mAutomationButtons.ContainsKey(automation))
            mAutomationButtons[automation].State = state;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        mAutomationLayout.Measure(availableSize);
        return base.MeasureOverride(availableSize);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        mAutomationLayout.Arrange(new Rect(finalSize));
        // 波形开关与「+」并排、整体在左侧预留区内水平居中（正对钢琴键列）、垂直居中，位置不随轨按钮集合变化。
        double left = (PianoWindow.ROLL_WIDTH - (mWaveformToggle.Width + LeftButtonGap + mAddPanelButton.Width)) / 2;
        mWaveformToggle.Arrange(new Rect(left, (finalSize.Height - mWaveformToggle.Height) / 2, mWaveformToggle.Width, mWaveformToggle.Height));
        mAddPanelButton.Arrange(new Rect(left + mWaveformToggle.Width + LeftButtonGap, (finalSize.Height - mAddPanelButton.Height) / 2, mAddPanelButton.Width, mAddPanelButton.Height));
        return finalSize;
    }

    // voice 与各 effect 的自动化轨统一汇入这一栏。voice 一组，每个 effect 一组，组间插竖向分隔符让用户看出分组。
    // 轨用 AutomationKey 标识（来源 + plain id），避免与真实 id 撞名。
    void ResetAutomationButtons()
    {
        mAutomationButtons.Clear();
        mAutomationLayout.Children.Clear();

        if (Part == null)
            return;

        bool firstGroup = true;
        AddAutomationGroup(Part.SoundSource.AutomationConfigs, -1, ref firstGroup);
        AddLaneButtons(Part.NoteLaneConfigs, AutomationKey.NoteLane);
        AddLaneButtons(Part.PhonemeLaneConfigs, AutomationKey.PhonemeLane);
        for (int i = 0; i < Part.Effects.Count; i++)
            AddAutomationGroup(Part.Effects[i].AutomationConfigs, i, ref firstGroup);
    }

    // 属性 lane 按钮（note 组在前、phoneme 组随后）：与 voice 轨同源（钉选的声源属性），故并入 voice 组尾部、不插分隔符。
    // 轨色：钉选的是钉选时分配的，时间轴画布取声明（未指定的由宿主分配）；名字沿用属性声明的 DisplayText。
    void AddLaneButtons(IReadOnlyOrderedMap<PropertyKey, LaneEntry> lanes, Func<string, AutomationKey> makeKey)
    {
        foreach (var kvp in lanes)
        {
            var key = makeKey(kvp.Key.Id);
            if (!mCacheParameterButtons.TryGetValue(key, out var button))
            {
                button = new ParameterButton(ColorUtils.ParseOrFallback(kvp.Value.Color), kvp.Key.DisplayText ?? kvp.Key.Id) { Margin = new(6, 0) };
                var captured = key;
                button.StateChangeAsked += (state) => StateChangeAsked?.Invoke(captured, state);
                // lane 按钮右键可就地解钉——与侧栏属性行右键对称；也是「引擎不再声明该属性」时死 tab 的唯一移除口
                //（此时侧栏没有对应控件可右键）。只在创建时挂一次（缓存按钮跨重建复用）。
                var capturedButton = button;
                button.ContextRequested += (_, e) =>
                {
                    var part = Part;
                    if (part == null)
                        return;
                    // 时间轴画布是插件声明的常驻参数、不是钉上来的，没有"移除"可言。
                    var lanes = captured.IsPhonemeLane ? part.PhonemeLaneConfigs : part.NoteLaneConfigs;
                    if (lanes.TryGetValue(captured.Id, out var lane) && lane.Kind == LaneKind.TimelineCanvas)
                        return;
                    var scope = captured.IsPhonemeLane ? ParameterPinKind.PhonemeProperty : ParameterPinKind.NoteProperty;
                    var menu = new ContextMenu();
                    // 解钉收口在 ParameterPinning.SetPinned（同侧栏属性右键、同命令面的 parameter.unpinProperty）。
                    menu.Items.Add(new MenuItem().SetName("Remove from Parameter Panel".Tr(TC.Menu))
                        .SetAction(() => ParameterPinning.SetPinned(part, scope, captured.Id, false)));
                    menu.Open(capturedButton);
                    e.Handled = true;
                };
                mCacheParameterButtons.Add(key, button);
            }
            else
            {
                // 解钉后重钉可能分配到不同轨色（也可能切语言换 DisplayText）：缓存按钮就地对齐。
                button.Color = ColorUtils.ParseOrFallback(kvp.Value.Color);
                button.Text = kvp.Key.DisplayText ?? kvp.Key.Id;
            }
            mAutomationButtons.Add(key, button);
            mAutomationLayout.Children.Add(button);
        }
    }

    // 一个来源（voice / 某 effect）一组（连续轨与分段轨同在此 map、按声明序），组间插分隔符。按钮形态与 kind 无关（读 config 色/名）。
    void AddAutomationGroup(IReadOnlyOrderedMap<PropertyKey, AutomationConfig> configs, int effectIndex, ref bool firstGroup)
    {
        if (configs.Count == 0)
            return;

        if (!firstGroup)
            mAutomationLayout.Children.Add(CreateSeparator());
        firstGroup = false;

        foreach (var kvp in configs)
        {
            var config = kvp.Value;
            var key = effectIndex < 0 ? AutomationKey.Voice(kvp.Key.Id) : AutomationKey.Effect(effectIndex, kvp.Key.Id);
            if (!mCacheParameterButtons.TryGetValue(key, out var button))
            {
                button = new ParameterButton(ColorUtils.ParseOrFallback(config.Color), kvp.Key.DisplayText ?? kvp.Key.Id) { Margin = new(6, 0) };
                var captured = key;
                button.StateChangeAsked += (state) => StateChangeAsked?.Invoke(captured, state);
                mCacheParameterButtons.Add(key, button);
            }
            mAutomationButtons.Add(key, button);
            mAutomationLayout.Children.Add(button);
        }
    }

    static Control CreateSeparator()
    {
        return new Border()
        {
            Width = 1,
            Height = 16,
            Margin = new Thickness(8, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Background = Style.LIGHT_WHITE.Opacity(0.25).ToBrush(),
        };
    }

    void SyncAutomationButtonsState()
    {
        if (Part == null)
            return;

        // 参数区藏着时不点亮激活的那个（画面上确实没有参数在显示）；激活记录本身不动，显示出来即复原。
        var activeAutomation = mDependency.IsParameterPanelVisible ? mDependency.ActiveAutomation : null;

        void SyncKey(AutomationKey key)
        {
            if (activeAutomation == key)
                SetState(key, ParameterButton.ButtonState.Edit);
            else
                SetState(key, mDependency.IsAutomationVisible(key) ? ParameterButton.ButtonState.Visible : ParameterButton.ButtonState.Off);
        }

        void Sync(IReadOnlyOrderedMap<PropertyKey, AutomationConfig> configs, int effectIndex)
        {
            foreach (var kvp in configs)
                SyncKey(effectIndex < 0 ? AutomationKey.Voice(kvp.Key.Id) : AutomationKey.Effect(effectIndex, kvp.Key.Id));
        }

        Sync(Part.SoundSource.AutomationConfigs, -1);
        foreach (var kvp in Part.NoteLaneConfigs)
            SyncKey(AutomationKey.NoteLane(kvp.Key.Id));
        foreach (var kvp in Part.PhonemeLaneConfigs)
            SyncKey(AutomationKey.PhonemeLane(kvp.Key.Id));
        for (int i = 0; i < Part.Effects.Count; i++)
            Sync(Part.Effects[i].AutomationConfigs, i);
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
    IMidiPart? Part => mDependency.PartHolder.Value;

    Color Back => Style.INTERFACE;

    readonly WrapPanel mAutomationLayout;
    readonly Toggle mWaveformToggle;
    readonly TuneLab.GUI.Components.Button mAddPanelButton;
    const double LeftButtonGap = 4;
    readonly ParameterButton mPitchButton;
    readonly OrderedMap<AutomationKey, ParameterButton> mAutomationButtons = new();
    readonly Map<AutomationKey, ParameterButton> mCacheParameterButtons = new();

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
