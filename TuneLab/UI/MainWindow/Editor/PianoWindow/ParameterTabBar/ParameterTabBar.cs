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
using TuneLab.Extensions.Effect;
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

        // 左侧让出钢琴键列宽，留给波形开关与「+」（避免轨多时居中流式布局铺过来重叠）；右侧只让出「显示哪些参数」按钮那一格
        //（按钮宽 + 两侧边距），多留给轨按钮——轨按钮因此相对剩下的区域居中、比窗口正中略偏右。
        mAutomationLayout = new() { Orientation = Orientation.Horizontal, HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Center, Margin = new Thickness(PianoWindow.ROLL_WIDTH, 9, RightReserve, 9) };
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

        // 最右侧（只占一格，见 RightReserve）：「显示哪些参数」——点开的是与空白处右键同一张勾选列表（见 OpenParameterMenu）。
        // 看得见的入口：用户不必去猜右键；右键空白处留作熟手的快捷方式。
        mParameterMenuButton = new TuneLab.GUI.Components.Button() { Width = 24, Height = 24 }
            .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, ColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
            .AddContent(new() { Item = new IconItem() { Icon = Assets.Checklist }, ColorSet = new() { Color = Colors.White } });
        mParameterMenuButton.SetupToolTip("Parameters in Tab Bar".Tr(TC.Menu));
        mParameterMenuButton.Clicked += () => OpenParameterMenu(mParameterMenuButton);
        Children.Add(mParameterMenuButton);

        // 标签栏空白处右键：标签栏上显示哪些参数（隐藏常驻的 / 钉可钉的；按音源记、跨所有面板生效，所以挂在标签栏而不是
        // 某块面板上，见 OnContextRequested）。标签按钮自己的右键是开关可见（小眼睛），不在这里弹菜单。
        ContextRequested += OnContextRequested;
        ParameterHiding.Changed.Subscribe(OnAutomationConfigsChanged, s);

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
        // 「显示哪些参数」在右侧那一格里居中（离右缘 = RightButtonMargin）。
        double right = finalSize.Width - RightButtonMargin - mParameterMenuButton.Width;
        mParameterMenuButton.Arrange(new Rect(right, (finalSize.Height - mParameterMenuButton.Height) / 2, mParameterMenuButton.Width, mParameterMenuButton.Height));
        return finalSize;
    }

    // 标签栏上显示哪些参数：右键直接弹出一张勾选列表，打勾 = 在标签栏上，点一下切换、菜单不关（可连续勾选）。只认空白处
    //（标签栏本身或按钮行的空隙）：按钮上的右键冒泡上来时不弹。勾选的含义统一，背后是两类存储（见 ParameterHiding）：
    // · 原本常驻的——音源的自动化轨、各效果器的自动化轨、时间轴画布：取消勾选 = 隐藏（ParameterHiding.SetHidden）。
    //   有数据的在行尾标一个强调色小圆点：藏起来它照样参与合成，别让人忘了它还在起作用。
    // · 原本不显示的——可钉属性：勾上 = 钉选（ParameterPinning.SetPinned，同侧栏属性右键、同命令面的 parameter.pin / unpinProperty）。
    //   列表 = 此刻能钉的（各 note 声明的并集，与 parameter.pinProperty 同一份口径）+ 已钉但引擎不再声明的孤儿（取自钉选存储，
    //   标签栏里连按钮都没有，只能在这里清掉）。
    // 按参数属于谁分组，组前一条写着组名的分隔线（MenuSeparators.Labeled）：声源名 / 音符 / 音素 / 各效果器名——与标签栏的分组、顺序一致。
    // 画布还是数值对用户不重要；音符、音素组里常驻的时间轴画布在前，可钉属性（按声明序）随后，孤儿钉选垫底。
    void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, this) && !ReferenceEquals(e.Source, mAutomationLayout))
            return;
        if (OpenParameterMenu(null))
            e.Handled = true;
    }

    // anchor = null：在指针处弹（右键空白处）；否则贴着这个按钮向上弹（标签栏在参数区底下，往上才不出窗口）。返回是否弹出了。
    bool OpenParameterMenu(Control? anchor)
    {
        var part = Part;
        if (part == null)
            return false;

        var groups = new List<(string Name, List<MenuItem> Items)>();

        MenuItem HideableItem(AutomationKey key, string name)
        {
            return CheckItem(name, () => !ParameterHiding.IsHidden(part, key), () => ParameterHiding.SetHidden(part, key, !ParameterHiding.IsHidden(part, key)),
                hasData: HasData(part, key));
        }

        var voice = new List<MenuItem>();
        foreach (var kvp in part.SoundSource.AutomationConfigs)
            voice.Add(HideableItem(AutomationKey.Voice(kvp.Key.Id), kvp.Key.DisplayText ?? kvp.Key.Id));
        groups.Add((string.IsNullOrEmpty(part.SoundSource.Name) ? part.SoundSource.Type : part.SoundSource.Name, voice));

        var note = new List<MenuItem>();
        var phoneme = new List<MenuItem>();
        foreach (var kvp in part.NoteLaneConfigs)
        {
            if (kvp.Value.Kind == LaneKind.TimelineCanvas)
                note.Add(HideableItem(AutomationKey.NoteLane(kvp.Key.Id), kvp.Key.DisplayText ?? kvp.Key.Id));
        }
        foreach (var kvp in part.PhonemeLaneConfigs)
        {
            if (kvp.Value.Kind == LaneKind.TimelineCanvas)
                phoneme.Add(HideableItem(AutomationKey.PhonemeLane(kvp.Key.Id), kvp.Key.DisplayText ?? kvp.Key.Id));
        }

        var listed = new HashSet<(ParameterPinKind, string)>();
        void AddPinnable(ParameterPinKind kind, string id, string name)
        {
            if (listed.Add((kind, id)))
                (kind == ParameterPinKind.PhonemeProperty ? phoneme : note).Add(CheckItem(name,
                    () => ParameterPinning.IsPinned(part.SoundSource, kind, id),
                    () => ParameterPinning.SetPinned(part, kind, id, !ParameterPinning.IsPinned(part.SoundSource, kind, id))));
        }
        foreach (var (kind, key) in part.PinnableProperties())
            AddPinnable(kind, key.Id, key.DisplayText ?? key.Id);
        foreach (var kind in Enum.GetValues<ParameterPinKind>())
        {
            foreach (var id in ParameterPinning.GetPinned(part.SoundSource, kind).Keys)
                AddPinnable(kind, id, id);
        }
        groups.Add(("Note".Tr(TC.Property), note));
        groups.Add(("Phoneme".Tr(TC.Property), phoneme));

        for (int i = 0; i < part.Effects.Count; i++)
        {
            var effect = new List<MenuItem>();
            foreach (var kvp in part.Effects[i].AutomationConfigs)
                effect.Add(HideableItem(AutomationKey.Effect(i, kvp.Key.Id), kvp.Key.DisplayText ?? kvp.Key.Id));
            groups.Add((EffectManager.GetDisplayName(part.Effects[i].Type), effect));
        }

        var menu = new ContextMenu();
        foreach (var (name, items) in groups)
        {
            if (items.Count == 0)
                continue;
            menu.Items.Add(MenuSeparators.Labeled(name));
            foreach (var item in items)
                menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            return false;
        if (anchor != null)
        {
            menu.PlacementTarget = anchor;
            menu.Placement = PlacementMode.TopEdgeAlignedRight;
        }
        menu.Open(anchor ?? this);
        return true;
    }

    // 点一项不关菜单、可以连续勾选，所以按存储此刻的状态切换（不用打开菜单那一刻的），勾选态也按它回填——
    // 推迟到这次点击处理完再填：菜单项自己在点击时也会翻转勾选态，两者先后不定，以最后回填的为准。
    // hasData：行尾右对齐一个强调色小圆点（各行的点排成一列，好扫）。
    static MenuItem CheckItem(string label, Func<bool> isChecked, Action toggle, bool hasData = false)
    {
        MenuItem? item = null;
        item = new MenuItem() { ToggleType = MenuItemToggleType.CheckBox, IsChecked = isChecked(), StaysOpenOnClick = true }.SetName(label).SetAction(() =>
        {
            toggle();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => item!.IsChecked = isChecked());
        });
        if (hasData)
        {
            var dot = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = DataDotSize,
                Height = DataDotSize,
                Fill = Style.HIGH_LIGHT.ToBrush(),
                Margin = new Thickness(16, 0, 0, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                UseLayoutRounding = true,
            };
            DockPanel.SetDock(dot, Dock.Right);
            var header = new DockPanel { LastChildFill = true };
            header.Children.Add(dot);
            header.Children.Add(new TextBlock { Text = label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            item.Header = header;
        }
        return item;
    }

    const double DataDotSize = 6;

    // 这条参数此刻有没有用户写的数据（隐藏了也照样参与合成）：自动化轨 = 有点 / 基线改过，分段轨 = 有段，
    // 时间轴画布 = 有哪个 note / 钉死的音素写过它。
    static bool HasData(IMidiPart part, AutomationKey key)
    {
        switch (key.Source)
        {
            case AutomationSource.Voice:
                return part.SoundSource.AutomationConfigs.TryGetValue(key.Id, out var voiceConfig)
                    && HasAutomationData(part.Automations, part.PiecewiseAutomations, key.Id, voiceConfig);
            case AutomationSource.Effect:
                if (key.EffectIndex < 0 || key.EffectIndex >= part.Effects.Count)
                    return false;
                var effect = part.Effects[key.EffectIndex];
                return effect.AutomationConfigs.TryGetValue(key.Id, out var effectConfig)
                    && HasAutomationData(effect.Automations, effect.PiecewiseAutomations, key.Id, effectConfig);
            case AutomationSource.NoteLane:
                foreach (var note in part.Notes)
                {
                    if (part.ResolveNoteLane(note, key.Id) is { } resolved && !LanePath.Read(note.Properties, resolved.Steps, PropertyValue.Null).IsNull())
                        return true;
                }
                return false;
            case AutomationSource.PhonemeLane:
                foreach (var note in part.Notes)
                {
                    for (int i = 0; i < note.Phonemes.Count; i++)
                    {
                        if (note.Phonemes[i].HasProperties && part.ResolvePhonemeLane(note, i, key.Id) is { } resolved
                            && !LanePath.Read(note.Phonemes[i].Properties, resolved.Steps, PropertyValue.Null).IsNull())
                            return true;
                    }
                }
                return false;
            default:
                return false;
        }
    }

    static bool HasAutomationData(IReadOnlyDataObjectMap<string, IAutomation> automations, IReadOnlyDataObjectMap<string, IPiecewiseAutomation> piecewise, string id, AutomationConfig config)
    {
        if (config.IsPiecewise)
            return piecewise.TryGetValue(id, out var segments) && segments.AnchorGroups.Count > 0;
        return automations.TryGetValue(id, out var automation) && (automation.Points.Count > 0 || automation.DefaultValue.Value != config.DefaultValue);
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
        AddLaneButtons(Part.NoteLaneConfigs, AutomationKey.NoteLane, ref firstGroup);
        AddLaneButtons(Part.PhonemeLaneConfigs, AutomationKey.PhonemeLane, ref firstGroup);
        for (int i = 0; i < Part.Effects.Count; i++)
            AddAutomationGroup(Part.Effects[i].AutomationConfigs, i, ref firstGroup);
    }

    // 属性 lane 按钮：音符一组、音素一组，按参数属于谁分组（同右键菜单），组间插分隔符。
    // 轨色：钉选的是钉选时分配的，时间轴画布取声明（未指定的由宿主分配）；名字沿用属性声明的 DisplayText。
    void AddLaneButtons(IReadOnlyOrderedMap<PropertyKey, LaneEntry> lanes, Func<string, AutomationKey> makeKey, ref bool firstGroup)
    {
        bool separated = false;
        foreach (var kvp in lanes)
        {
            var key = makeKey(kvp.Key.Id);
            if (ParameterHiding.IsHidden(Part!, key))
                continue;
            if (!separated)
            {
                if (!firstGroup)
                    mAutomationLayout.Children.Add(CreateSeparator());
                firstGroup = false;
                separated = true;
            }
            if (!mCacheParameterButtons.TryGetValue(key, out var button))
            {
                button = new ParameterButton(ColorUtils.ParseOrFallback(kvp.Value.Color), kvp.Key.DisplayText ?? kvp.Key.Id) { Margin = new(6, 0) };
                var captured = key;
                button.StateChangeAsked += (state) => StateChangeAsked?.Invoke(captured, state);
                mCacheParameterButtons.Add(key, button);
            }
            else
            {
                // 解钉后重钉可能分配到不同轨色（也可能切语言换 DisplayText）：缓存按钮就地对齐。
                button.Color = ColorUtils.ParseOrFallback(kvp.Value.Color);
                button.Text = kvp.Key.DisplayText ?? kvp.Key.Id;
            }
            // 标签名是最短不重名的（嵌套字段只显示末段 + 序号），悬停给完整路径。
            if (kvp.Value.Label is { } label && LanePath.FullLabel(label) is var full && full != (kvp.Key.DisplayText ?? kvp.Key.Id))
                button.SetupToolTip(full);
            mAutomationButtons.Add(key, button);
            mAutomationLayout.Children.Add(button);
        }
    }

    // 一个来源（voice / 某 effect）一组（连续轨与分段轨同在此 map、按声明序），组间插分隔符。按钮形态与 kind 无关（读 config 色/名）。
    // 从标签栏隐藏的不出按钮；一组全藏了，连分隔符也不留。
    void AddAutomationGroup(IReadOnlyOrderedMap<PropertyKey, AutomationConfig> configs, int effectIndex, ref bool firstGroup)
    {
        var shown = new List<(AutomationKey Key, PropertyKey Name, AutomationConfig Config)>();
        foreach (var kvp in configs)
        {
            var key = effectIndex < 0 ? AutomationKey.Voice(kvp.Key.Id) : AutomationKey.Effect(effectIndex, kvp.Key.Id);
            if (!ParameterHiding.IsHidden(Part!, key))
                shown.Add((key, kvp.Key, kvp.Value));
        }
        if (shown.Count == 0)
            return;

        if (!firstGroup)
            mAutomationLayout.Children.Add(CreateSeparator());
        firstGroup = false;

        foreach (var (key, name, config) in shown)
        {
            if (!mCacheParameterButtons.TryGetValue(key, out var button))
            {
                button = new ParameterButton(ColorUtils.ParseOrFallback(config.Color), name.DisplayText ?? name.Id) { Margin = new(6, 0) };
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

        // 激活的是画布时，背景里的画布不画（两块界面叠起来看不清，见 AutomationRenderer），它们的小眼睛也就不点亮——
        // 画面上确实没有它；可见记录本身不动，换回别的激活参数即复原。
        var part = Part;
        bool IsCanvasLane(AutomationKey key)
            => (part.IsEffectiveNoteLane(key) || part.IsEffectivePhonemeLane(key)) && !part.IsNumberLane(key);
        bool activeIsCanvas = activeAutomation is { } active && IsCanvasLane(active);

        void SyncKey(AutomationKey key)
        {
            if (activeAutomation == key)
                SetState(key, ParameterButton.ButtonState.Edit);
            else if (activeIsCanvas && IsCanvasLane(key))
                SetState(key, ParameterButton.ButtonState.Off);
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
    readonly TuneLab.GUI.Components.Button mParameterMenuButton;
    const double LeftButtonGap = 4;
    // 右侧只让出一格：按钮宽 24 + 两侧边距（边距同左侧那组按钮离左缘的距离 6）。
    const double RightButtonMargin = 6;
    const double RightReserve = 24 + RightButtonMargin * 2;
    readonly ParameterButton mPitchButton;
    readonly OrderedMap<AutomationKey, ParameterButton> mAutomationButtons = new();
    readonly Map<AutomationKey, ParameterButton> mCacheParameterButtons = new();

    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
