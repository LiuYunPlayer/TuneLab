using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using TuneLab.Input;
using TuneLab.Data;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Utils;
using TuneLab.SDK;
using TuneLab.GUI.Components;
using TuneLab.Configs;
using TuneLab.Extensions.Effect;
using TuneLab.I18N;
using Avalonia.Threading;

namespace TuneLab.UI;

internal class PianoWindow : DockPanel, PianoRoll.IDependency, PianoScrollView.IDependency, TimelineView.IDependency, ParameterTabBar.IDependency, ParameterPanel.IHost, PlayheadLayer.IDependency
{
    // 底部标签栏与外部读到的「激活 / 可见」都是焦点面板的。
    public event Action? ActiveAutomationChanged;
    public event Action? VisibleAutomationChanged;
    public IActionEvent WaveformBottomChanged => mWaveformBottomChanged;
    public IActionEvent WaveformVisibleChanged => mWaveformVisibleChanged;
    public IActionEvent ParameterPanelVisibilityChanged => mParameterPanelVisibilityChanged;
    public bool IsWaveformVisible => EditorState.WaveformVisible.Value;
    // 参数区是否显示着（Ctrl+P 藏起来的就是不显示；各面板自己收起与否不算）。
    public bool IsParameterPanelVisible => !mAreaHidden;
    // 报给外部的面板列表（`editor status`），自上而下。
    public IReadOnlyList<TuneLab.Commands.ParameterPanelStatus> ParameterPanelStatuses
        => mPanels.Select(p => new TuneLab.Commands.ParameterPanelStatus(p.ActiveName, p.IsFocused, p.IsCollapsed)).ToList();
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
    public IQuantization Quantization => mQuantization;
    public IPlayhead Playhead => mDependency.Playhead;
    public INotifiableProperty<PianoTool> PianoTool => mDependency.PianoTool;
    public INotifiableProperty<PlayScrollTarget> PlayScrollTarget => mDependency.PlayScrollTarget;
    public AutomationKey? ActiveAutomation
    {
        get => FocusedPanel.ActiveAutomation;
        set => FocusedPanel.ActiveAutomation = value;
    }
    public IReadOnlyList<AutomationKey> VisibleAutomations => FocusedPanel.VisibleAutomations;
    // 波形带贴在面板堆顶上：= 视图高 − 整个面板堆的高度。
    public double WaveformBottom => mPianoScrollView.Bounds.Height - StackHeight;
    public interface IDependency
    {
        IPlayhead Playhead { get; }
        INotifiableProperty<PianoTool> PianoTool { get; }
        INotifiableProperty<PlayScrollTarget> PlayScrollTarget { get; }
    }

    public PianoWindow(IDependency dependency)
    {
        mDependency = dependency;

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
                        var first = CreatePanel(GetStoredParameterHeight());
                        first.IsFocused = true;
                        mPanels.Add(first);
                        RebuildParameterLayer();
                    }
                    pianoScrollViewPanel.Children.Add(mParameterLayer);

                    // 滚动条置顶层：纵向绑音高轴、贴右边；横向绑时间轴（无界，设 ContentExtentProvider = 内容末尾口径）。
                    // 横向条落在"波形上方"（= note 区下沿）而非窗口最底——由布局 Margin 定位（见 UpdateHorizontalBarMargin）。
                    mVerticalScrollBar = new(mPitchAxis, Orientation.Vertical);
                    mHorizontalScrollBar = new(mTickAxis, Orientation.Horizontal) { ContentExtentProvider = GetContentEndX };
                    pianoScrollViewPanel.Children.Add(mVerticalScrollBar);
                    pianoScrollViewPanel.Children.Add(mHorizontalScrollBar);

                    // 靠近边缘才显示：view 层职责。铺满 pianoScrollViewPanel 但只手柄可命中、其余穿透。
                    mVerticalReveal = new(mVerticalScrollBar, pianoScrollViewPanel, Orientation.Vertical);
                    mHorizontalReveal = new(mHorizontalScrollBar, pianoScrollViewPanel, Orientation.Horizontal);

                    // 横向条底边落在波形上方：以 Margin 定位（布局职责），随参数区高/波形显隐更新。
                    UpdateHorizontalBarMargin();
                    mWaveformBottomChanged.Subscribe(UpdateHorizontalBarMargin, s);
                    mWaveformVisibleChanged.Subscribe(UpdateHorizontalBarMargin, s);
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
        mPartHolder.Modified.Subscribe(ApplyPendingParameterLayout, s);
        mParameterLayer.SizeChanged += (s, e) => { ApplyLayout(); };

        ClipToBounds = true;

        RegisterActions();
    }

    ~PianoWindow()
    {
        s.DisposeAll();
    }

    public bool IsAutomationVisible(AutomationKey automation) => FocusedPanel.IsAutomationVisible(automation);

    // 合成参数回显轨声明（只读轨集合，按 AutomationKey 分源合并 voice + 各 effect）：各面板共用，显隐由各面板自己管。
    // 每次按源现合并（轨集合随参数 commit 涌现、规模小、不在热路径）：voice 声明 → AutomationKey.Voice，
    // 各 effect 声明 → AutomationKey.Effect(index)。
    public IReadOnlyOrderedMap<AutomationKey, AutomationConfigEntry> SynthesizedParameterConfigs
    {
        get
        {
            if (Part == null)
                return sEmptySynthesizedParameterConfigs;

            var result = new OrderedMap<AutomationKey, AutomationConfigEntry>();
            foreach (var kvp in Part.SoundSource.SynthesizedParameterConfigs)
                result.Add(AutomationKey.Voice(kvp.Key.Id), new AutomationConfigEntry(kvp.Key, kvp.Value));
            for (int i = 0; i < Part.Effects.Count; i++)
            {
                foreach (var kvp in Part.Effects[i].SynthesizedParameterConfigs)
                    result.Add(AutomationKey.Effect(i, kvp.Key.Id), new AutomationConfigEntry(kvp.Key, kvp.Value));
            }
            return result;
        }
    }

    public void SetWaveformVisible(bool isVisible)
    {
        if (EditorState.WaveformVisible.Value == isVisible)
            return;

        EditorState.WaveformVisible.Value = isVisible;
        mWaveformVisibleChanged.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.IsHandledByTextBox())
            return;

        if (AnyPanelOperating)
            return;

        // 命令级快捷键仅在无进行中操作（拖动/缩放等）时分发；进行中的操作态修饰键由各 Operation 自行处理、不经此。
        if (PianoScrollView.OperationState != PianoScrollView.State.None)
            return;

        e.Handled = Keymap.TryHandle(KeyScope.PianoWindow, e);
    }

    // PianoWindow 作用域的内置动作（钢琴窗专属：移调 / 八度）。剪贴板类动词（复制/剪切/粘贴/删除/全选）
    // 是与编排区共享的通用动作，注册在 Editor 域、由 Editor 按聚焦面路由到下列 *Selection 方法，不在此登记。
    void RegisterActions()
    {
        // 域 = 功能身份（note 音符级操作），非分发作用域（虽在 PianoWindow 分发）。见 docs/keybinding-system.md §1.1。
        // 四条都改音符音高、进撤销栈 → ProjectEdit；可用性判据（有没有 part、选中了没有）与 ChangeKey
        // 从前的内部守卫同一份，见 PianoScrollView.TransposeUnavailable。
        Keymap.Register(Transpose("note.octaveUp", () => "Octave Up".Tr(TC.Menu), +12), KeyScope.PianoWindow, new(Key.Up, KeyModifiers.Shift));
        Keymap.Register(Transpose("note.octaveDown", () => "Octave Down".Tr(TC.Menu), -12), KeyScope.PianoWindow, new(Key.Down, KeyModifiers.Shift));
        Keymap.Register(Transpose("note.transposeUp", () => "Semitone Up".Tr(TC.Menu), +1), KeyScope.PianoWindow, new(Key.Up));
        Keymap.Register(Transpose("note.transposeDown", () => "Semitone Down".Tr(TC.Menu), -1), KeyScope.PianoWindow, new(Key.Down));

        RegisterParameterPanelActions();
        RegisterPanelStackActions();
    }

    // 一条移调动作。带一个**可选修饰符参数**（动作面上第一个，见 TuneLab.Input.ActionParameter.Optional）：
    // 移调要不要把音符底下的音高线与自动化曲线一起搬走。
    //
    // 【为什么是可选而不是逐值两条动作】这四条都带手势，而键盘按下去无从携带参数——手势与右键菜单要的
    // 就是"按我在设置里选的来"。故缺省路径读 Settings.ParameterSyncMode，命令面要确定性行为时显式给值。
    // 值域是固定二值，但它是修饰符、不是"作用对象"，逐值开会把四条翻成八条（判据见 ActionParameter）。
    EditorAction Transpose(string id, Func<string> displayName, int offset) => new()
    {
        Id = id,
        DisplayName = displayName,
        Kind = ActionKind.ProjectEdit,   // 改音符音高、进撤销栈
        // 可用性判据（有没有开 part、选中了没有）与 ChangeKey 从前的内部守卫同一份。
        Unavailable = PianoScrollView.TransposeUnavailable,
        Execute = () => PianoScrollView.ChangeKey(offset, Settings.ParameterSyncMode),
        Parameter = new()
        {
            Name = "parameters",
            Description = "Whether the pitch line and automation curves under the notes are transposed along with them.",
            // State 报出此刻哪一个是缺省，调用方因此不必先去读设置才知道不给值会发生什么。
            Values = () =>
            [
                new ActionArgument("sync", "Transpose the parameters too", Settings.ParameterSyncMode ? "current default" : null),
                new ActionArgument("keep", "Move the notes only, leave the curves", Settings.ParameterSyncMode ? null : "current default"),
            ],
            Execute = value => PianoScrollView.ChangeKey(offset, value == "sync"),
            Optional = true,
            DefaultBehavior = "follows the user's \"Parameter Sync Mode\" setting (Settings > Editing), which is how the keyboard shortcut and the context menu behave",
        },
    };

    // 参数面板里「有哪些轨」这六条：动作面上唯一的一批【带选择器参数】的动作（见 TuneLab.Input.ActionParameter）。
    // 成员随 part 的声源与效果器链、随属性声明面而变（动态集）——逐成员开 id 会爆，而且那些 id 明天就不在了，
    // 「一经发布不改」无从保证。故一个动词 + 一个闭集里的成员：加一条轨是加一个**值**，不是加一条动作。
    //
    // 四条都是**终态且幂等**（点亮已亮的轨、钉已钉的属性都什么都不做），外部因此不必先读一次状态才敢按；
    // 此刻的实情由 `action list` 的值域（每个值带 shown / hidden / pinned…）与 `action run` 的回报给出。
    // 都不进 Keymap：一条绑定只有手势没有参数，v1 刻意不做「绑定携带参数」（docs/keybinding-system.md §10）。
    void RegisterParameterPanelActions()
    {
        // ── 合成参数轨（引擎产物、只读）的显隐：与标题栏那排 chip 是同一件事（同一个
        // SetSynthesizedParameterVisible）。改的是编辑器自身的显示状态（内存集合，不随工程保存、
        // 撤销栈里没有它）→ AppState、不过闸门。
        ActionRegistry.Register(new()
        {
            Id = "parameter.showSynthesizedTrack",
            DisplayName = () => "Show Synthesized Parameter Track".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Unavailable = SynthesizedTrackUnavailable,
            Parameter = new()
            {
                Name = "track",
                Description = TrackParameterDescription,
                Values = SynthesizedTrackValues,
                Execute = token => FocusedPanel.SetSynthesizedParameterVisible(ParseTrackToken(token), true),
            },
        });
        ActionRegistry.Register(new()
        {
            Id = "parameter.hideSynthesizedTrack",
            DisplayName = () => "Hide Synthesized Parameter Track".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Unavailable = SynthesizedTrackUnavailable,
            Parameter = new()
            {
                Name = "track",
                Description = TrackParameterDescription,
                Values = SynthesizedTrackValues,
                Execute = token => FocusedPanel.SetSynthesizedParameterVisible(ParseTrackToken(token), false),
            },
        });

        // ── 参数面板钉选（把一个有界数值或侧栏画布的 note / phoneme 属性物化成一条 lane）：显示名沿用界面两处右键
        // 菜单的措辞（复用其翻译），执行也是同一个 ParameterPinning.SetPinned。
        // 【为什么仍是 AppState】它写钉选存储（Configs/ParameterPins.json），但那是**界面自身状态的持久化**
        // ——同 view.toggleWaveform 写 EditorState.json，不是 Destructive 说的"往磁盘写文件"（那一档指的是
        // 写用户的文档或任意路径的文件）。分档判据没变：用户一眼看得见（参数栏多/少一条 tab）、自己就能改回来。
        ActionRegistry.Register(new()
        {
            Id = "parameter.pinProperty",
            DisplayName = () => "Edit in Parameter Panel".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Unavailable = () => PinUnavailable(pinned: true),
            Parameter = new()
            {
                Name = "property",
                Description = PinPropertyDescription,
                Values = () => PinnableValues(),
                Execute = token => SetPinned(token, true),
            },
        });
        ActionRegistry.Register(new()
        {
            Id = "parameter.unpinProperty",
            DisplayName = () => "Remove from Parameter Panel".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Unavailable = () => PinUnavailable(pinned: false),
            Parameter = new()
            {
                Name = "property",
                Description = UnpinPropertyDescription,
                Values = () => PinnedValues(),
                Execute = token => SetPinned(token, false),
            },
        });

        // ── 标签栏上常驻参数（音源 / 效果器的自动化轨、时间轴画布）的隐藏 / 显示：与标签栏空白处右键那张勾选列表里
        // 这几类的勾是同一件事（同一个 ParameterHiding.SetHidden）。可钉属性不在这对里——它们走 pin / unpinProperty。
        // AppState 的理由同钉选：写的是界面偏好（Configs/ParameterHidden.json），一眼看得见、自己就能改回来。
        ActionRegistry.Register(new()
        {
            Id = "parameter.hideFromTabBar",
            DisplayName = () => "Hide from Tab Bar".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Unavailable = HideableUnavailable,
            Parameter = new()
            {
                Name = "parameter",
                Description = HideableParameterDescription,
                Values = HideableValues,
                Execute = token => SetHiddenFromTabBar(token, true),
            },
        });
        ActionRegistry.Register(new()
        {
            Id = "parameter.showInTabBar",
            DisplayName = () => "Show in Tab Bar".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Unavailable = HideableUnavailable,
            Parameter = new()
            {
                Name = "parameter",
                Description = HideableParameterDescription,
                Values = HideableValues,
                Execute = token => SetHiddenFromTabBar(token, false),
            },
        });
    }

    const string HideableParameterDescription =
        "Which parameter, written \"source:<id>\" for an automation track of the part's sound source, \"effect<n>:<id>\" for one of the n-th effect in the chain (0-based, the same position as in part.effects()), "
        + "or \"note:<path>\" / \"phoneme:<path>\" for a timeline canvas. Each value says whether it is shown or hidden right now; hiding only takes it off the tab bar, its data still takes part in synthesis. "
        + "Pinnable properties are not in this list: they are added and removed with parameter.pinProperty / unpinProperty. The label works too.";

    string? HideableUnavailable()
        => Part == null ? "no part is open in the piano roll, so there is no parameter tab bar" : null;

    // 值域 = 标签栏上能常驻的全集（含已藏的：终态动作幂等），序同标签栏（音源轨、时间轴画布、各效果器轨）。
    IReadOnlyList<ActionArgument> HideableValues()
    {
        var part = Part;
        var values = new List<ActionArgument>();
        if (part == null)
            return values;

        void Add(AutomationKey key, string token, string label)
            => values.Add(new ActionArgument(token, label, ParameterHiding.IsHidden(part, key) ? "hidden" : "shown"));

        foreach (var kvp in part.SoundSource.AutomationConfigs)
            Add(AutomationKey.Voice(kvp.Key.Id), "source:" + kvp.Key.Id, (kvp.Key.DisplayText ?? kvp.Key.Id) + " (sound source)");
        foreach (var kvp in part.NoteLaneConfigs)
        {
            if (kvp.Value.Kind == LaneKind.TimelineCanvas)
                Add(AutomationKey.NoteLane(kvp.Key.Id), "note:" + kvp.Key.Id, (kvp.Key.DisplayText ?? kvp.Key.Id) + " (timeline canvas)");
        }
        foreach (var kvp in part.PhonemeLaneConfigs)
        {
            if (kvp.Value.Kind == LaneKind.TimelineCanvas)
                Add(AutomationKey.PhonemeLane(kvp.Key.Id), "phoneme:" + kvp.Key.Id, (kvp.Key.DisplayText ?? kvp.Key.Id) + " (phoneme timeline canvas)");
        }
        for (int i = 0; i < part.Effects.Count; i++)
        {
            var type = part.Effects[i].Type;
            var source = string.IsNullOrEmpty(type) ? "(empty)" : EffectManager.GetDisplayName(type);
            foreach (var kvp in part.Effects[i].AutomationConfigs)
                Add(AutomationKey.Effect(i, kvp.Key.Id), "effect" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + kvp.Key.Id, (kvp.Key.DisplayText ?? kvp.Key.Id) + " (" + source + ")");
        }
        return values;
    }

    // token 只按第一个冒号切（路径与 id 里带冒号也无碍）；source / effect<n> 的拼法同合成参数轨（ParseTrackToken）。
    void SetHiddenFromTabBar(string token, bool hidden)
    {
        var part = Part;
        if (part == null)
            return;

        int colon = token.IndexOf(':');
        var source = token[..colon];
        var id = token[(colon + 1)..];
        var key = source switch
        {
            "note" => AutomationKey.NoteLane(id),
            "phoneme" => AutomationKey.PhonemeLane(id),
            _ => ParseTrackToken(token),
        };
        ParameterHiding.SetHidden(part, key, hidden);
    }

    // 参数面板堆本身（几块、哪块是焦点、各自收起没有、整个区显示没有）的动作。与标题栏 / 标签栏那几个按钮是同一件事。
    // 改的都是编辑器自身的显示状态（不进撤销、不算工程已修改）→ AppState、不过闸门。都是终态且幂等
    //（收起已收起的、聚焦已是焦点的什么都不做），只有「加一块」本就不是幂等的。
    // 「哪一块」是选择器参数：值 = 自上而下的 1-based 序号，标签 = 它此刻的激活参数名。关、收起、展开的参数可选，
    // 不给就作用于焦点面板（右键「+」关的也是它）；聚焦必须说是哪块。
    void RegisterPanelStackActions()
    {
        ActionRegistry.Register(new()
        {
            Id = "view.showParameterPanel",
            DisplayName = () => "Show Parameter Panel".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Execute = () => SetParameterAreaHidden(false),
        });
        ActionRegistry.Register(new()
        {
            Id = "view.hideParameterPanel",
            DisplayName = () => "Hide Parameter Panel".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Execute = () => SetParameterAreaHidden(true),
        });
        // 照着焦点面板加一块、插在它下方并获得焦点（同标签栏的「+」）。参数区藏着时先显示出来——
        // 与「+」不同（藏着时点「+」只是唤醒）：动作说的是"加一块"，就该真加一块。
        ActionRegistry.Register(new()
        {
            Id = "view.addParameterPanel",
            DisplayName = () => "Add Parameter Panel".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Execute = () =>
            {
                SetParameterAreaHidden(false);
                InsertPanelAfterFocused();
            },
        });
        ActionRegistry.Register(new()
        {
            Id = "view.closeParameterPanel",
            DisplayName = () => "Close Panel".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Unavailable = () => CanClosePanel ? null : "only one parameter panel is open, and the last one cannot be closed",
            Execute = () => ClosePanel(FocusedPanel),
            Parameter = PanelParameter(panel => ClosePanel(panel), optional: true, "closes the focused panel"),
        });
        ActionRegistry.Register(new()
        {
            Id = "view.collapseParameterPanel",
            DisplayName = () => "Collapse Panel".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Execute = () => SetPanelCollapsed(FocusedPanel, true),
            Parameter = PanelParameter(panel => SetPanelCollapsed(panel, true), optional: true, "collapses the focused panel"),
        });
        ActionRegistry.Register(new()
        {
            Id = "view.expandParameterPanel",
            DisplayName = () => "Expand Panel".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Execute = () => SetPanelCollapsed(FocusedPanel, false),
            Parameter = PanelParameter(panel => SetPanelCollapsed(panel, false), optional: true, "expands the focused panel"),
        });
        ActionRegistry.Register(new()
        {
            Id = "view.focusParameterPanel",
            DisplayName = () => "Focus Panel".Tr(TC.Menu),
            Kind = ActionKind.AppState,
            Parameter = PanelParameter(panel => SetFocusedPanel(panel), optional: false, null),
        });
    }

    ActionParameter PanelParameter(Action<ParameterPanel> execute, bool optional, string? defaultBehavior) => new()
    {
        Name = "panel",
        Description = "Which parameter panel, numbered from the top starting at 1. The label is the parameter it is showing right now (the name on its title bar); the label works too, but two panels can show the same parameter, so the number is the unambiguous way.",
        Values = PanelValues,
        Execute = token => execute(mPanels[int.Parse(token) - 1]),
        Optional = optional,
        DefaultBehavior = defaultBehavior,
    };

    IReadOnlyList<ActionArgument> PanelValues()
    {
        var values = new List<ActionArgument>();
        for (int i = 0; i < mPanels.Count; i++)
        {
            var panel = mPanels[i];
            var state = string.Join(", ", new[] { panel.IsFocused ? "focused" : null, panel.IsCollapsed ? "collapsed" : null }.OfType<string>());
            values.Add(new ActionArgument((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), panel.ActiveName, state.Length == 0 ? null : state));
        }
        return values;
    }

    void SetPanelCollapsed(ParameterPanel panel, bool collapsed)
    {
        if (panel.IsCollapsed == collapsed)
            return;

        panel.IsCollapsed = collapsed;
        ApplyLayout();
    }

    const string TrackParameterDescription =
        "Which synthesized parameter track (read-only engine output), written \"source:<id>\" for one the part's sound source declares, "
        + "or \"effect<n>:<id>\" for the n-th effect in the chain (0-based, the same position as in part.effects()). The label works too.";

    // pin 与 unpin 的值域是两个不同的集合（能钉的 / 已钉的），故说明也分开写：共用一句就必然对其中一条说反。
    const string PinPropertyDescription =
        "Which property to pin to the parameter panel, written \"note:<id>\" or \"phoneme:<id>\" (only bounded numeric properties and side-panel canvases can be pinned, so the values are exactly the ones that can; timeline canvases are always in the panel and need no pinning). "
        + "Each value says whether it is already pinned; pinning one that already is does nothing. The label works too.";

    const string UnpinPropertyDescription =
        "Which pinned property to remove from the parameter panel, written \"note:<id>\" or \"phoneme:<id>\". "
        + "The values are the ones pinned right now for this part's sound source — including any whose engine no longer declares it (there is no tab left for those, so this is the only way to clear them). The label works too.";

    string? SynthesizedTrackUnavailable()
    {
        if (Part == null)
            return "no part is open in the piano roll, so there is no parameter panel to show tracks in";
        if (SynthesizedParameterConfigs.Count == 0)
            return "this part's sound source and effects declare no synthesized parameter tracks";
        return null;
    }

    // 值域 = 标题栏那排 chip 的全集（含已亮的：终态动作幂等），序与界面一致（voice 在前、各 effect 按链序）。
    IReadOnlyList<ActionArgument> SynthesizedTrackValues()
    {
        var part = Part;
        var values = new List<ActionArgument>();
        foreach (var kvp in SynthesizedParameterConfigs)
            values.Add(new ActionArgument(TrackToken(kvp.Key), TrackLabel(part, kvp.Key, kvp.Value), FocusedPanel.IsSynthesizedParameterVisible(kvp.Key) ? "shown" : "hidden"));
        return values;
    }

    // token 与它的解析成对写在一起（一处拼法）。id 里若真带冒号也无碍：只按第一个冒号切。
    static string TrackToken(AutomationKey key) => (key.IsEffect ? "effect" + key.EffectIndex : "source") + ":" + key.Id;

    static AutomationKey ParseTrackToken(string token)
    {
        int colon = token.IndexOf(':');
        var source = token[..colon];
        var id = token[(colon + 1)..];
        return source.StartsWith("effect", StringComparison.Ordinal)
            ? AutomationKey.Effect(int.Parse(source["effect".Length..]), id)
            : AutomationKey.Voice(id);
    }

    // 人看的名字：轨名 + 它属于哪个源（同一个轨名可能在声源与多个 effect 里各出现一次）。
    // 源名与"添加效果"菜单、侧栏 effect 块、标题栏源标签同一口径（manifest 声明名，缺失才回退 type id）。
    static string TrackLabel(IMidiPart? part, AutomationKey key, AutomationConfigEntry entry)
    {
        var name = entry.Key.DisplayText ?? entry.Key.Id;
        if (!key.IsEffect)
            return name + " (sound source)";

        string source = "effect";
        if (part != null && key.EffectIndex < part.Effects.Count)
        {
            var type = part.Effects[key.EffectIndex].Type;
            source = string.IsNullOrEmpty(type) ? "(empty)" : EffectManager.GetDisplayName(type);
        }
        return name + " (" + source + ")";
    }

    // pinned=true 问的是"现在能不能钉"（要有可钉的声明），false 问的是"能不能解钉"（要有已钉的）。
    string? PinUnavailable(bool pinned)
    {
        if (Part == null)
            return "no part is open in the piano roll, so there is no parameter panel to pin to";
        if (pinned)
            return Part.PinnableProperties().Count == 0
                ? "this part's sound source declares no properties that can be pinned (only bounded numeric ones and side-panel canvases can)"
                : null;
        return PinnedValues().Count == 0 ? "nothing is pinned to the parameter panel for this part's sound source" : null;
    }

    // 可钉的全集（含已钉的：终态幂等），口径与 lane 物化同一份（IMidiPart.PinnableProperties）。
    IReadOnlyList<ActionArgument> PinnableValues()
    {
        var part = Part;
        var values = new List<ActionArgument>();
        if (part == null)
            return values;

        foreach (var (kind, key) in part.PinnableProperties())
        {
            values.Add(new ActionArgument(PinToken(kind, key.Id), PinLabel(kind, key.DisplayText ?? key.Id),
                ParameterPinning.IsPinned(part.SoundSource, kind, key.Id) ? "pinned" : "not pinned"));
        }
        return values;
    }

    // 已钉的全集，取自钉选存储而不是物化出来的 lane 集合：引擎不再声明的钉选（参数栏里连 tab 都没有的
    // 孤儿）也必须够得着解钉，否则外部就出现了一处清不掉的残留。名字能从 lane 拿就拿，拿不到用 id。
    IReadOnlyList<ActionArgument> PinnedValues()
    {
        var part = Part;
        var values = new List<ActionArgument>();
        if (part == null)
            return values;

        foreach (var kind in Enum.GetValues<ParameterPinKind>())
        {
            var lanes = kind == ParameterPinKind.PhonemeProperty ? part.PhonemeLaneConfigs : part.NoteLaneConfigs;
            foreach (var id in ParameterPinning.GetPinned(part.SoundSource, kind).Keys)
            {
                var name = id;
                foreach (var kvp in lanes)
                {
                    if (kvp.Key.Id == id)
                    {
                        name = kvp.Key.DisplayText ?? kvp.Key.Id;
                        break;
                    }
                }
                values.Add(new ActionArgument(PinToken(kind, id), PinLabel(kind, name), "pinned"));
            }
        }
        return values;
    }

    static string PinToken(ParameterPinKind kind, string id)
        => (kind == ParameterPinKind.PhonemeProperty ? "phoneme" : "note") + ":" + id;

    static string PinLabel(ParameterPinKind kind, string name)
        => name + (kind == ParameterPinKind.PhonemeProperty ? " (phoneme property)" : " (note property)");

    void SetPinned(string token, bool pinned)
    {
        var part = Part;
        if (part == null)
            return;

        int colon = token.IndexOf(':');
        var kind = token[..colon] == "phoneme" ? ParameterPinKind.PhonemeProperty : ParameterPinKind.NoteProperty;
        ParameterPinning.SetPinned(part, kind, token[(colon + 1)..], pinned);
    }

    // 剪贴板类命令仅在无进行中操作（拖动/缩放等）时生效——原本由 OnKeyDown 前置守卫，改由 Editor 路由后在此自守。
    // 公开是给 Editor 的动作可用性判据用（EditSurfaceUnavailable）：外部触发时"正拖着东西所以不收命令"
    // 必须能说出来，不能静默吞掉。
    public bool CanRunEditCommand => !AnyPanelOperating && PianoScrollView.OperationState == PianoScrollView.State.None;

    public void CopySelection() { if (CanRunEditCommand) PianoScrollView.Copy(); }
    public void CutSelection() { if (CanRunEditCommand) PianoScrollView.Cut(); }
    public void PasteSelection() { if (CanRunEditCommand) PianoScrollView.Paste(); }

    public void DeleteSelection()
    {
        if (!CanRunEditCommand)
            return;
        var automationRenderer = HoveredOrFocusedPanel.AutomationRenderer;
        // Anchor 工具且悬停 automation 时删锚点，否则删所选对象。
        if (PianoTool.Value == UI.PianoTool.Anchor && automationRenderer.IsHover)
            automationRenderer.DeleteSelectedAnchors();
        else
            PianoScrollView.Delete();
    }

    public void SelectAllInPiano()
    {
        if (!CanRunEditCommand)
            return;
        var automationRenderer = HoveredOrFocusedPanel.AutomationRenderer;
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
                    RefreshParameterPanels();
                    Part?.Pitch.SelectAllAnchors();
                }
                break;
            default:
                break;
        }
    }

    // Ctrl+P：把**整个参数区**藏起来 / 显示出来（藏起来时连标题栏也不占地方）。各面板的高度与收起状态不动，显示即原样。
    public void ToggleParameterPanel() => SetParameterAreaHidden(!mAreaHidden);

    // 标题栏右键的「隐藏参数面板」。
    public void HideParameterArea() => SetParameterAreaHidden(true);

    // 藏着时的唤醒入口除了 Ctrl+P，还有底部标签栏的参数按钮与「+」（藏着时它们都不点亮，看着就是"没开"）。
    // 显示出来时区里没有展开着的内容（全收起了、或都拖到了底），就展开焦点面板，免得显示了也看不到东西。
    void SetParameterAreaHidden(bool hidden)
    {
        if (mAreaHidden == hidden)
            return;

        mAreaHidden = hidden;
        if (!hidden && !mPanels.Any(p => !p.IsCollapsed && p.DesiredHeight > 0.5))
        {
            var focused = FocusedPanel;
            focused.IsCollapsed = false;
            if (focused.DesiredHeight < 1)
                focused.DesiredHeight = GetStoredParameterHeight();
        }
        mParameterLayer.IsVisible = !hidden;
        ApplyLayout();
        mParameterPanelVisibilityChanged.Invoke();
    }

    // —— 参数面板布局进出工程文件（EditorInfo）——

    // 存：自上而下的各面板 + 焦点下标。effect 参数按钢琴窗此刻开着的 part 补上实例身份。
    public (List<ParameterPanelInfo> Panels, int Focused) CaptureParameterLayout()
    {
        var effects = ParameterPanelLayoutStore.EffectSlotsOf(Part);
        return (mPanels.Select(p => p.Capture(effects)).ToList(), mFocusedIndex);
    }

    // 还原（打开工程、崩溃恢复、新建）。panels 为 null 或空 = 默认单面板（旧工程、.tlp、新工程）。
    // effect 参数要对着一个 part 认回，此刻钢琴窗还没开 part（崩溃恢复是先装数据、后开 part）就先记下，开了再还原。
    public void RestoreParameterLayout(IReadOnlyList<ParameterPanelInfo>? panels, int focused)
    {
        mPendingParameterLayout = null;
        if (panels is { Count: > 0 } && Part == null)
        {
            mPendingParameterLayout = (panels, focused);
            return;
        }
        ApplyParameterLayout(panels, focused);
    }

    void ApplyPendingParameterLayout()
    {
        if (mPendingParameterLayout is not { } pending || Part == null)
            return;

        mPendingParameterLayout = null;
        ApplyParameterLayout(pending.Panels, pending.Focused);
    }

    void ApplyParameterLayout(IReadOnlyList<ParameterPanelInfo>? panels, int focused)
    {
        foreach (var panel in mPanels)
            panel.Detach();
        mPanels.Clear();
        // 先把焦点下标放到第一块上：Restore 会发激活 / 可见变化，转发时要读焦点面板（先加进列表再 Restore 同理）。
        mFocusedIndex = 0;

        var effects = ParameterPanelLayoutStore.EffectSlotsOf(Part);
        if (panels is { Count: > 0 })
        {
            foreach (var info in panels)
            {
                var panel = CreatePanel(info.Height);
                mPanels.Add(panel);
                panel.Restore(info, effects);
            }
        }
        else
        {
            mPanels.Add(CreatePanel(GetStoredParameterHeight()));
        }

        RebuildParameterLayer();
        SetFocusedPanel(mPanels[Math.Clamp(focused, 0, mPanels.Count - 1)], force: true);
    }

    // —— 参数面板堆 ——

    public bool CanClosePanel => mPanels.Count > 1;

    // 标签栏的「+」：在焦点面板下方插一块照着它设置的新面板，新面板获得焦点。参数区藏着时只是把它显示出来。
    public void AddParameterPanel()
    {
        if (mAreaHidden)
        {
            SetParameterAreaHidden(false);
            return;
        }

        InsertPanelAfterFocused();
    }

    void InsertPanelAfterFocused()
    {
        var focused = FocusedPanel;
        var panel = CreatePanel(focused.DesiredHeight);
        panel.CopyFrom(focused);
        mPanels.Insert(mFocusedIndex + 1, panel);
        RebuildParameterLayer();
        SetFocusedPanel(panel);
    }

    // 标签栏右键「+」。
    public void CloseFocusedParameterPanel() => ClosePanel(FocusedPanel);

    // 只剩一块时不关。关掉的是焦点面板就把焦点交给它上面那块（没有上面的就是新的第一块）。
    void ClosePanel(ParameterPanel panel)
    {
        int index = mPanels.IndexOf(panel);
        if (index < 0 || mPanels.Count <= 1)
            return;

        var focused = FocusedPanel;
        mPanels.RemoveAt(index);
        panel.Detach();
        RebuildParameterLayer();
        SetFocusedPanel(panel == focused ? mPanels[Math.Max(0, index - 1)] : focused, force: panel == focused);
    }

    ParameterPanel CreatePanel(double desiredHeight)
    {
        var panel = new ParameterPanel(this, PARAMETER_TITLE_BAR_HEIGHT, desiredHeight);
        // 底部标签栏只跟焦点面板：别的面板的激活 / 可见变化不转发。
        panel.ActiveAutomationChanged += () => { if (panel == FocusedPanel) ActiveAutomationChanged?.Invoke(); };
        panel.VisibleAutomationChanged += () => { if (panel == FocusedPanel) VisibleAutomationChanged?.Invoke(); };
        panel.FocusRequested += () => SetFocusedPanel(panel);
        panel.CloseAsked += () => ClosePanel(panel);
        // 收起 / 展开不切焦点（同只有点标题栏才聚焦的约定）。
        panel.CollapseToggleAsked += () => SetPanelCollapsed(panel, !panel.IsCollapsed);
        panel.TitleBar.Moved += top => OnPanelDragged(panel, top);
        return panel;
    }

    void SetFocusedPanel(ParameterPanel panel, bool force = false)
    {
        int index = mPanels.IndexOf(panel);
        if (index < 0 || (index == mFocusedIndex && !force))
            return;

        mFocusedIndex = index;
        foreach (var p in mPanels)
            p.IsFocused = p == panel;
        // 标签栏改跟新的焦点面板：按它的激活 / 可见重新同步。
        ActiveAutomationChanged?.Invoke();
        VisibleAutomationChanged?.Invoke();
    }

    // 拖某块面板的标题栏：只改这一块的高度，其余面板不变，差额由钢琴窗吸收。
    // 先把当前各面板显示的高度记成想要的高度——压缩状态下拖动，松手后不会有别的面板跳回原高（所见即所得）。
    void OnPanelDragged(ParameterPanel panel, double top)
    {
        foreach (var p in mPanels)
        {
            if (!p.IsCollapsed)
                p.DesiredHeight = p.Container.Height;
        }

        double delta = panel.TitleBar.Bounds.Y - top;   // 往上拖为正
        if (panel.IsCollapsed)
        {
            if (delta <= 0)
                return;
            panel.IsCollapsed = false;
            panel.DesiredHeight = 0;
        }

        double others = mPanels.Where(p => p != panel).Sum(p => p.Container.Height);
        panel.DesiredHeight = (panel.DesiredHeight + delta).Limit(0, Math.Max(0, ContentAvailable - others));
        StoreParameterHeight(panel.DesiredHeight);
        ApplyLayout();
    }

    // 面板堆自上而下排进参数层（DockPanel 自底向上贴，故倒序加）。
    void RebuildParameterLayer()
    {
        mParameterLayer.Children.Clear();
        for (int i = mPanels.Count - 1; i >= 0; i--)
        {
            mParameterLayer.AddDock(mPanels[i].Container, Dock.Bottom);
            mParameterLayer.AddDock(mPanels[i].TitleBar, Dock.Bottom);
            // 只剩一块时不能关，「×」也不出。
            mPanels[i].TitleBar.IsCloseButtonVisible = mPanels.Count > 1;
        }
        mParameterLayer.MinHeight = mPanels.Count * PARAMETER_TITLE_BAR_HEIGHT;
        ApplyLayout();
    }

    double ContentAvailable => Math.Max(0, mParameterLayer.Bounds.Height - mPanels.Count * PARAMETER_TITLE_BAR_HEIGHT);

    // 各面板的显示高度 = 按可用空间分配想要的高度（ParameterPanelLayout）；只设显示，不回写想要的高度。
    void ApplyLayout()
    {
        var heights = ParameterPanelLayout.Solve(ContentAvailable,
            mPanels.Select(p => new ParameterPanelLayout.Panel(p.DesiredHeight, p.IsCollapsed)).ToList(), MIN_PANEL_HEIGHT);
        for (int i = 0; i < mPanels.Count; i++)
            mPanels[i].Container.Height = heights[i];
        mWaveformBottomChanged.Invoke();
    }

    // 参数区藏着时不占地方：波形带贴到钢琴窗底。
    double StackHeight => mAreaHidden ? 0 : mPanels.Sum(p => p.TitleBar.Height + p.Container.Height);

    // 钢琴窗里取消了自动化点选中之类：每块面板都要重画、刷新锚点数值框。
    public void RefreshParameterPanels()
    {
        foreach (var panel in mPanels)
        {
            panel.AutomationRenderer.InvalidateVisual();
            panel.AutomationRenderer.RefreshAnchorValueInput();
        }
    }

    // 本机记的参数栏高度：只作新面板堆（新工程、不带布局的工程）里第一块面板的默认高度。
    double GetStoredParameterHeight()
    {
        double height = EditorState.MainWindowMaximized ? EditorState.ParameterPanelHeightMaximized : EditorState.ParameterPanelHeightNormal;
        return height < 1 ? EditorState.Defaults.ParameterPanelHeight : height;
    }

    void StoreParameterHeight(double height)
    {
        EditorState.ParameterPanelHeight.Value = height;
        if (TopLevel.GetTopLevel(this) is Window { WindowState: WindowState.Maximized })
        {
            EditorState.ParameterPanelHeightMaximized.Value = height;
        }
        else
        {
            EditorState.ParameterPanelHeightNormal.Value = height;
        }
    }

    // 在焦点面板里编辑这条参数（侧栏时间轴画布那一行的编辑按钮）：同点它的页签——参数区藏着就先显示出来，
    // 它被从标签栏隐藏了也先显示回去（按这个按钮就是要看它）。
    public void EditInFocusedPanel(AutomationKey automation)
    {
        SetParameterAreaHidden(false);
        if (Part is { } part)
            ParameterHiding.SetHidden(part, automation, false);
        FocusedPanel.ActiveAutomation = automation;
    }

    void OnParameterTabBarStateChangeAsked(AutomationKey automation, ParameterButton.ButtonState state)
    {
        if (state == ParameterButton.ButtonState.Edit)
        {
            // 参数区藏着时点参数按钮 = 想看它：先显示出来。
            SetParameterAreaHidden(false);
            FocusedPanel.ActiveAutomation = automation;
        }
        else
            FocusedPanel.SetAutomationVisible(automation, state == ParameterButton.ButtonState.Visible);
    }

    const double TIME_AXIS_HEIGHT = 48;
    // 钢琴键列宽：ParameterTabBar 引用它对齐左侧预留区（面板开关正对键列居中）。
    public const double ROLL_WIDTH = 64;
    // 抬高以容纳回显轨显隐 chip（色块 + 文本）；空白区仍可拖拽改高。
    const double PARAMETER_TITLE_BAR_HEIGHT = 24;

    // 压缩时每块展开着的面板至少保留的内容高度：再矮，刻度轴上下两端的数值标签就叠在一起了。
    const double MIN_PANEL_HEIGHT = 60;

    // 面板堆（自上而下）与焦点：底部标签栏、动作面、编辑命令作用于焦点面板。
    readonly List<ParameterPanel> mPanels = new();
    int mFocusedIndex;
    bool mAreaHidden;
    // 等 part 打开再还原的布局（见 RestoreParameterLayout）。
    (IReadOnlyList<ParameterPanelInfo> Panels, int Focused)? mPendingParameterLayout;
    ParameterPanel FocusedPanel => mPanels[mFocusedIndex];
    // 删除 / 全选锚点作用于指针悬停的那块，没有悬停的就是焦点面板。
    ParameterPanel HoveredOrFocusedPanel => mPanels.FirstOrDefault(p => p.AutomationRenderer.IsHover) ?? FocusedPanel;
    bool AnyPanelOperating => mPanels.Any(p => p.AutomationRenderer.IsOperating);

    static readonly OrderedMap<AutomationKey, AutomationConfigEntry> sEmptySynthesizedParameterConfigs = new();

    readonly ActionEvent mWaveformBottomChanged = new();
    readonly ActionEvent mWaveformVisibleChanged = new();
    readonly ActionEvent mParameterPanelVisibilityChanged = new();
    readonly DisposableManager s = new();

    readonly Quantization mQuantization;
    readonly TickAxis mTickAxis;
    readonly PitchAxis mPitchAxis;

    // 横向滚动条的内容末尾像素长度：当前 part 的末尾 tick × 每 tick 像素（手柄滑到底 = 视口远边正好落在
    // part 末尾；无限拖仍可继续拖过末尾、手柄钳在边缘）。
    double GetContentEndX()
    {
        var part = mPartHolder.Value;
        if (part == null)
            return 0;

        return part.EndPos() * mTickAxis.PixelsPerTick;
    }

    // 横向条落在"波形上方"（note 区下沿）：底边留白 = 参数区高 + 波形高（波形可见时），用 Margin 定位。
    void UpdateHorizontalBarMargin()
    {
        double inset = StackHeight;
        if (IsWaveformVisible)
            inset += PianoScrollView.WAVEFORM_HEIGHT;
        mHorizontalScrollBar.Margin = new Thickness(0, 0, 0, inset);
    }

    readonly PianoScrollView mPianoScrollView;
    readonly TimelineView mPianoTimelineView;
    readonly PlayheadLayer mPlayheadLayer;
    readonly ScrollBar mVerticalScrollBar;
    readonly ScrollBar mHorizontalScrollBar;
    readonly EdgeProximityReveal mVerticalReveal;
    readonly EdgeProximityReveal mHorizontalReveal;
    readonly PianoRoll mPianoRoll;
    readonly ParameterTabBar mParameterTabBar;
    readonly DockPanel mParameterLayer;

    readonly Holder<IMidiPart> mPartHolder = new();

    readonly IDependency mDependency;
}
