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
using TuneLab.Utils;
using TuneLab.SDK;
using TuneLab.GUI.Components;
using TuneLab.Configs;
using TuneLab.Extensions.Effect;
using TuneLab.I18N;
using Avalonia.Threading;

namespace TuneLab.UI;

internal class PianoWindow : DockPanel, PianoRoll.IDependency, PianoScrollView.IDependency, TimelineView.IDependency, ParameterTabBar.IDependency, ParameterTitleBar.IDependency, AutomationRenderer.IDependency, PlayheadLayer.IDependency
{
    public event Action? ActiveAutomationChanged;
    public event Action? VisibleAutomationChanged;
    public event Action? SynthesizedParameterVisibilityChanged;
    public IActionEvent WaveformBottomChanged => mWaveformBottomChanged;
    public IActionEvent WaveformVisibleChanged => mWaveformVisibleChanged;
    public IActionEvent ParameterPanelVisibilityChanged => mParameterPanelVisibilityChanged;
    public bool IsWaveformVisible => EditorState.WaveformVisible.Value;
    public bool IsParameterPanelVisible => mParameterHeight > 0.5;
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

            return AutomationKey.Voice(ConstantDefine.PreCommonAutomationConfigs[0].Key.Id);
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
    public double WaveformBottom => mPianoScrollView.Bounds.Height - mParameterTitleBar.Height - mParameterContainer.Height;
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

                        mParameterTitleBar = new ParameterTitleBar(this) { Height = PARAMETER_TITLE_BAR_HEIGHT };
                        mParameterLayer.AddDock(mParameterTitleBar, Dock.Bottom);
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
        mParameterTitleBar.Moved += top =>
        {
            mParameterHeight = mParameterLayer.Bounds.Height - top - mParameterTitleBar.Bounds.Height;
            CorrectParameterHeight(true);
        };
        mParameterLayer.SizeChanged += (s, e) => { CorrectParameterHeight(false); };
        AttachedToVisualTree += (s, e) => BindWindowState();

        ClipToBounds = true;

        ActiveAutomation = AutomationKey.Voice(ConstantDefine.PreCommonAutomationConfigs[0].Key.Id);

        RegisterActions();
    }

    ~PianoWindow()
    {
        s.DisposeAll();
    }

    public bool IsAutomationVisible(AutomationKey automation)
    {
        if (Part == null)
            return false;

        if (!Part.IsEffectiveAutomation(automation) && !Part.IsEffectivePiecewiseAutomation(automation)
            && !Part.IsEffectiveNoteLane(automation) && !Part.IsEffectivePhonemeLane(automation))
            return false;

        return mVisibleAutomations.Contains(automation);
    }

    public void SetAutomationVisible(AutomationKey automation, bool isVisible)
    {
        bool wasVisible = mVisibleAutomations.Contains(automation);
        mVisibleAutomations.Remove(automation);

        if (isVisible)
            mVisibleAutomations.Add(automation);

        VisibleAutomationChanged?.Invoke();

        // 配对轨的单向联动：点亮可编辑轨（灭→亮的那一刻）⇒ 同 key 的回显轨一起亮。需求本身不对称——
        // 只看回显是独立的合法用法（对着模型输出观察），而查看自己画的曲线时几乎总要参照模型输出。
        // 故只带亮、不连带熄灭（否则会吞掉用户原本单独开着的回显），也不锁 chip（允许手动关掉）；
        // 一次性带亮而非持续绑定，与固定"不自动同步"同一种克制。
        if (isVisible && !wasVisible && SynthesizedParameterConfigs.ContainsKey(automation))
            SetSynthesizedParameterVisible(automation, true);
    }

    // —— 合成参数回显轨显隐（只读轨集合，按 AutomationKey 分源合并 voice + 各 effect；独立于可编辑轨的
    //    Visible/Active 机制；显隐由参数区标题栏管控）——
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

    public bool IsSynthesizedParameterVisible(AutomationKey key) => mVisibleSynthesizedParameters.Contains(key);

    public void SetSynthesizedParameterVisible(AutomationKey key, bool isVisible)
    {
        bool changed = isVisible ? mVisibleSynthesizedParameters.Add(key) : mVisibleSynthesizedParameters.Remove(key);
        if (changed)
            SynthesizedParameterVisibilityChanged?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.IsHandledByTextBox())
            return;

        if (mParameterContainer.AutomationRenderer.IsOperating)
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
        Keymap.Register(new() { Id = "note.octaveUp", DisplayName = () => "Octave Up".Tr(TC.Menu), Kind = ActionKind.ProjectEdit, Unavailable = PianoScrollView.TransposeUnavailable, Execute = () => PianoScrollView.OctaveUp() }, KeyScope.PianoWindow, new(Key.Up, KeyModifiers.Shift));
        Keymap.Register(new() { Id = "note.octaveDown", DisplayName = () => "Octave Down".Tr(TC.Menu), Kind = ActionKind.ProjectEdit, Unavailable = PianoScrollView.TransposeUnavailable, Execute = () => PianoScrollView.OctaveDown() }, KeyScope.PianoWindow, new(Key.Down, KeyModifiers.Shift));
        Keymap.Register(new() { Id = "note.transposeUp", DisplayName = () => "Semitone Up".Tr(TC.Menu), Kind = ActionKind.ProjectEdit, Unavailable = PianoScrollView.TransposeUnavailable, Execute = () => PianoScrollView.ChangeKey(+1) }, KeyScope.PianoWindow, new(Key.Up));
        Keymap.Register(new() { Id = "note.transposeDown", DisplayName = () => "Semitone Down".Tr(TC.Menu), Kind = ActionKind.ProjectEdit, Unavailable = PianoScrollView.TransposeUnavailable, Execute = () => PianoScrollView.ChangeKey(-1) }, KeyScope.PianoWindow, new(Key.Down));

        RegisterParameterPanelActions();
    }

    // 参数面板里「有哪些轨」这四条：动作面上唯一的一批【带选择器参数】的动作（见 TuneLab.Input.ActionParameter）。
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
                Execute = token => SetSynthesizedParameterVisible(ParseTrackToken(token), true),
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
                Execute = token => SetSynthesizedParameterVisible(ParseTrackToken(token), false),
            },
        });

        // ── 参数面板钉选（把一个有界数值的 note / phoneme 属性物化成一条 lane）：显示名沿用界面两处右键
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
    }

    const string TrackParameterDescription =
        "Which synthesized parameter track (read-only engine output), written \"source:<id>\" for one the part's sound source declares, "
        + "or \"effect<n>:<id>\" for the n-th effect in the chain (0-based, the same position as in part.effects()). The label works too.";

    // pin 与 unpin 的值域是两个不同的集合（能钉的 / 已钉的），故说明也分开写：共用一句就必然对其中一条说反。
    const string PinPropertyDescription =
        "Which property to pin to the parameter panel, written \"note:<id>\" or \"phoneme:<id>\" (only bounded numeric properties can be pinned, so the values are exactly the ones that can). "
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
            values.Add(new ActionArgument(TrackToken(kvp.Key), TrackLabel(part, kvp.Key, kvp.Value), IsSynthesizedParameterVisible(kvp.Key) ? "shown" : "hidden"));
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
                ? "this part's sound source declares no properties that can be pinned (only bounded numeric ones can)"
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
    public bool CanRunEditCommand => !mParameterContainer.AutomationRenderer.IsOperating && PianoScrollView.OperationState == PianoScrollView.State.None;

    public void CopySelection() { if (CanRunEditCommand) PianoScrollView.Copy(); }
    public void CutSelection() { if (CanRunEditCommand) PianoScrollView.Cut(); }
    public void PasteSelection() { if (CanRunEditCommand) PianoScrollView.Paste(); }

    public void DeleteSelection()
    {
        if (!CanRunEditCommand)
            return;
        var automationRenderer = mParameterContainer.AutomationRenderer;
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
        var automationRenderer = mParameterContainer.AutomationRenderer;
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

    // 一键折叠/恢复参数面板内容高度（标题栏仍保留，与拖到最低等价）。
    // 收起前把当前非零高度写入 EditorState，恢复时再读回；折叠态本身不落盘为 0，避免覆盖原高度。
    public void ToggleParameterPanel()
    {
        if (mParameterHeight > 0.5)
        {
            StoreParameterHeight(mParameterHeight);
            mParameterHeight = 0;
            CorrectParameterHeight(false);
            return;
        }

        var restore = GetStoredParameterHeight();
        if (restore < 1)
            restore = EditorState.Defaults.ParameterPanelHeight;
        mParameterHeight = restore;
        CorrectParameterHeight(false);
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
        mParameterPanelVisibilityChanged.Invoke();
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
    // 钢琴键列宽：ParameterTabBar 引用它对齐左侧预留区（面板开关正对键列居中）。
    public const double ROLL_WIDTH = 64;
    // 抬高以容纳回显轨显隐 chip（色块 + 文本）；空白区仍可拖拽改高。
    const double PARAMETER_TITLE_BAR_HEIGHT = 24;

    double mParameterHeight = 200;

    AutomationKey? mActiveAutomation;
    readonly List<AutomationKey> mVisibleAutomations = new();
    // 回显轨显隐集合（按轨 id；默认隐藏，用户经标题栏 chip 点亮）。
    readonly HashSet<AutomationKey> mVisibleSynthesizedParameters = new();
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
        double inset = mParameterTitleBar.Height + mParameterContainer.Height;
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
