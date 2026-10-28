using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Extensions.Formats.TLP;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.I18N;
using TuneLab.Input;
using TuneLab.Utils;
using Color = Avalonia.Media.Color;

namespace TuneLab.UI;

// 一块参数面板：标题栏 + 容器（内含渲染器）+ 这块面板自己的显示状态——激活参数、可见参数、回显轨显隐。
// 各面板各管各的这三样；part、刻度轴、钢琴窗视图、工具、回显轨声明这些共享的东西由宿主（PianoWindow）提供。
// 渲染器与标题栏只经各自的 IDependency 读状态，故它们不知道自己是第几块面板。
// 高度记的是"想要的高度"（放不下时宿主按比例压缩显示，见 ParameterPanelLayout），收起与焦点也各块各记。
internal sealed class ParameterPanel : AutomationRenderer.IDependency, ParameterTitleBar.IDependency
{
    public interface IHost
    {
        // 只剩一块时不能关（右键菜单不出"关闭面板"）。
        bool CanClosePanel { get; }
        // 右键菜单的「隐藏参数面板」：整个参数区藏起来（同 Ctrl+P）。
        void HideParameterArea();
        IHolder<IMidiPart> PartHolder { get; }
        PianoScrollView PianoScrollView { get; }
        TickAxis TickAxis { get; }
        INotifiableProperty<PianoTool> PianoTool { get; }
        IReadOnlyOrderedMap<AutomationKey, AutomationConfigEntry> SynthesizedParameterConfigs { get; }
    }

    public event Action? ActiveAutomationChanged;
    public event Action? VisibleAutomationChanged;
    public event Action? SynthesizedParameterVisibilityChanged;
    // 交给宿主的：点了标题栏（聚焦它）、收起三角被点、点了「×」或右键选了关闭。
    public event Action? FocusRequested;
    public event Action? CollapseToggleAsked;
    public event Action? CloseAsked;

    public ParameterTitleBar TitleBar { get; }
    public ParameterContainer Container { get; }
    public AutomationRenderer AutomationRenderer => Container.AutomationRenderer;

    // 想要的高度（逻辑像素）与收起状态：显示高度由宿主按空间算（ParameterPanelLayout），不回写这里。
    public double DesiredHeight { get; set; }
    public bool IsCollapsed
    {
        get => mIsCollapsed;
        set
        {
            mIsCollapsed = value;
            RefreshHeader();
        }
    }
    public bool IsFocused
    {
        get => mIsFocused;
        set
        {
            mIsFocused = value;
            RefreshHeader();
        }
    }

    public ParameterPanel(IHost host, double titleBarHeight, double desiredHeight)
    {
        mHost = host;
        DesiredHeight = desiredHeight;
        Container = new ParameterContainer(this);
        TitleBar = new ParameterTitleBar(this) { Height = titleBarHeight };
        ActiveAutomation = DefaultAutomation;

        // 只有按标题栏才聚焦（见 ParameterTitleBar.Pressed）。在内容里画参数不切焦点：用户可以常驻几块面板、
        // 留一块主面板专门用底部标签栏切参数——画完别的面板，标签栏照旧作用于主面板。
        TitleBar.Pressed += () => FocusRequested?.Invoke();
        TitleBar.CollapseToggleAsked += () => CollapseToggleAsked?.Invoke();
        TitleBar.CloseAsked += () => CloseAsked?.Invoke();
        // 右键菜单不能挂 ContextRequested：标题栏底层的拖拽把手（自绘组件）把指针事件都标成已处理，Avalonia 就不再派发它。
        // 故直接收右键松开（已处理的也收）。
        TitleBar.AddHandler(InputElement.PointerReleasedEvent, OnTitleBarReleased, RoutingStrategies.Tunnel, handledEventsToo: true);

        // 表头显示激活参数的名字与色：激活变了、声源 / 效果器链 / 轨集合变了都要重取。
        ActiveAutomationChanged += RefreshHeader;
        VisibleAutomationChanged += RefreshHeader;
        host.PartHolder.Modified.Subscribe(RefreshHeader, s);
        host.PartHolder.When(p => p.SoundSource.Modified).Subscribe(RefreshHeader, s);
        host.PartHolder.When(p => p.AutomationConfigsModified).Subscribe(RefreshHeader, s);
        host.PartHolder.When(p => p.Effects.MembershipModified).Subscribe(RefreshHeader, s);
        RefreshHeader();
    }

    // 新加的面板照着焦点面板来：激活、可见、回显显隐、想要的高度都一样。
    public void CopyFrom(ParameterPanel other)
    {
        mVisibleAutomations.Clear();
        mVisibleAutomations.AddRange(other.mVisibleAutomations);
        mVisibleSynthesizedParameters.Clear();
        mVisibleSynthesizedParameters.UnionWith(other.mVisibleSynthesizedParameters);
        mActiveAutomation = other.mActiveAutomation;
        DesiredHeight = other.DesiredHeight;
        ActiveAutomationChanged?.Invoke();
        VisibleAutomationChanged?.Invoke();
        SynthesizedParameterVisibilityChanged?.Invoke();
    }

    // 存进工程文件（EditorInfo）。effect 参数按当前 part 的链补上实例身份（见 ParameterPanelLayoutStore）。
    public ParameterPanelInfo Capture(IReadOnlyList<ParameterPanelLayoutStore.EffectSlot> effects)
    {
        var info = new ParameterPanelInfo
        {
            Height = DesiredHeight,
            Collapsed = mIsCollapsed,
            Active = mActiveAutomation is { } active ? ParameterPanelLayoutStore.ToRef(active, effects) : null,
        };
        foreach (var key in mVisibleAutomations)
        {
            if (ParameterPanelLayoutStore.ToRef(key, effects) is { } r)
                info.Visible.Add(r);
        }
        foreach (var key in mVisibleSynthesizedParameters)
        {
            if (ParameterPanelLayoutStore.ToRef(key, effects) is { } r)
                info.VisibleSynthesized.Add(r);
        }
        return info;
    }

    // 从工程文件还原。认不回的参数就少这一条；激活参数认不回则回落默认（Volume）。
    public void Restore(ParameterPanelInfo info, IReadOnlyList<ParameterPanelLayoutStore.EffectSlot> effects)
    {
        mVisibleAutomations.Clear();
        foreach (var r in info.Visible)
        {
            if (ParameterPanelLayoutStore.Resolve(r, effects) is { } key && !mVisibleAutomations.Contains(key))
                mVisibleAutomations.Add(key);
        }
        mVisibleSynthesizedParameters.Clear();
        foreach (var r in info.VisibleSynthesized)
        {
            if (ParameterPanelLayoutStore.Resolve(r, effects) is { } key)
                mVisibleSynthesizedParameters.Add(key);
        }
        mActiveAutomation = info.Active is { } active ? ParameterPanelLayoutStore.Resolve(active, effects) ?? DefaultAutomation : DefaultAutomation;
        DesiredHeight = info.Height;
        mIsCollapsed = info.Collapsed;
        ActiveAutomationChanged?.Invoke();
        VisibleAutomationChanged?.Invoke();
        SynthesizedParameterVisibilityChanged?.Invoke();
        RefreshHeader();
    }

    // 关掉面板：退订一切共享事件源（它们比面板活得久，订阅在就一直收事件、也释放不掉）。
    public void Detach()
    {
        s.DisposeAll();
        AutomationRenderer.Detach();
        TitleBar.Detach();
    }

    void OnTitleBarReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Right)
            return;

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem().SetName("Hide Parameter Panel".Tr(TC.Menu)).SetAction(mHost.HideParameterArea));
        if (mHost.CanClosePanel)
            menu.Items.Add(new MenuItem().SetName("Close Panel".Tr(TC.Menu)).SetAction(() => CloseAsked?.Invoke()));
        menu.Open(TitleBar);
    }

    // 激活参数的显示名（标题栏上写的那个）：动作面的面板值域、编辑器状态用它让外部认出是哪一块。
    public string ActiveName => DescribeActive().Name;

    public void RefreshHeader()
    {
        var (name, color) = DescribeActive();
        TitleBar.ShowHeader(name, color, mIsFocused, mIsCollapsed);
    }

    // 激活参数的显示名与色：与底部标签栏同一来源（声源 / 各效果器的自动化声明，属性 lane 的钉选色）。
    (string Name, Color Color) DescribeActive()
    {
        var part = Part;
        if (part == null || ActiveAutomation is not { } key)
            return (string.Empty, Style.LIGHT_WHITE);

        switch (key.Source)
        {
            case AutomationSource.Voice:
                return Describe(part.SoundSource.AutomationConfigs, key.Id);
            case AutomationSource.Effect:
                return key.EffectIndex >= 0 && key.EffectIndex < part.Effects.Count
                    ? Describe(part.Effects[key.EffectIndex].AutomationConfigs, key.Id)
                    : (key.Id, Style.LIGHT_WHITE);
            case AutomationSource.NoteLane:
            case AutomationSource.PhonemeLane:
                var lanes = key.IsNoteLane ? part.NoteLaneConfigs : part.PhonemeLaneConfigs;
                foreach (var kvp in lanes)
                {
                    if (kvp.Key.Id == key.Id)
                        return (kvp.Key.DisplayText ?? kvp.Key.Id, ColorUtils.ParseOrFallback(kvp.Value.Color));
                }
                return (key.Id, Style.LIGHT_WHITE);
            default:
                return (key.Id, Style.LIGHT_WHITE);
        }
    }

    static (string Name, Color Color) Describe(IReadOnlyOrderedMap<TuneLab.SDK.PropertyKey, TuneLab.SDK.AutomationConfig> configs, string id)
    {
        foreach (var kvp in configs)
        {
            if (kvp.Key.Id == id)
                return (kvp.Key.DisplayText ?? kvp.Key.Id, ColorUtils.ParseOrFallback(kvp.Value.Color));
        }
        return (id, Style.LIGHT_WHITE);
    }

    IMidiPart? Part => mHost.PartHolder.Value;

    // 激活参数：记下的那条在当前 part 里不再有效（换了声源、插件没装……）时显示回落到 Volume，记录本身保留——
    // 它重新有效时自动恢复。
    public AutomationKey? ActiveAutomation
    {
        get
        {
            if (Part == null)
                return null;

            if (mActiveAutomation.HasValue && IsAutomationVisible(mActiveAutomation.Value))
                return mActiveAutomation.Value;

            return DefaultAutomation;
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
        {
            // 画布的可见互斥：一块面板里至多一个画布可见（作背景时画），打开一个就关掉别的——画布是一整块界面，
            // 两块叠起来哪块都看不清。激活也会把它设为可见，所以切回普通参数后作背景的是最近用过的那个画布。
            // 曲线 / 数值 lane 不受影响，照常与一个画布同时可见。
            if (IsCanvasLane(automation))
                mVisibleAutomations.RemoveAll(IsCanvasLane);
            mVisibleAutomations.Add(automation);
        }

        VisibleAutomationChanged?.Invoke();

        // 配对轨的单向联动：点亮可编辑轨（灭→亮的那一刻）⇒ 同 key 的回显轨一起亮。需求本身不对称——
        // 只看回显是独立的合法用法（对着模型输出观察），而查看自己画的曲线时几乎总要参照模型输出。
        // 故只带亮、不连带熄灭（否则会吞掉用户原本单独开着的回显），也不锁 chip（允许手动关掉）；
        // 一次性带亮而非持续绑定，与固定"不自动同步"同一种克制。
        if (isVisible && !wasVisible && SynthesizedParameterConfigs.ContainsKey(automation))
            SetSynthesizedParameterVisible(automation, true);
    }

    bool IsCanvasLane(AutomationKey key)
        => Part is { } part && (part.IsEffectiveNoteLane(key) || part.IsEffectivePhonemeLane(key)) && !part.IsNumberLane(key);

    // —— 合成参数回显轨显隐（只读轨集合；独立于可编辑轨的 Visible/Active 机制；显隐由本面板标题栏管控）——
    public bool IsSynthesizedParameterVisible(AutomationKey key) => mVisibleSynthesizedParameters.Contains(key);

    public void SetSynthesizedParameterVisible(AutomationKey key, bool isVisible)
    {
        bool changed = isVisible ? mVisibleSynthesizedParameters.Add(key) : mVisibleSynthesizedParameters.Remove(key);
        if (changed)
            SynthesizedParameterVisibilityChanged?.Invoke();
    }

    // —— 共享部分：转交宿主 ——
    public IHolder<IMidiPart> PartHolder => mHost.PartHolder;
    public PianoScrollView PianoScrollView => mHost.PianoScrollView;
    public TickAxis TickAxis => mHost.TickAxis;
    public INotifiableProperty<PianoTool> PianoTool => mHost.PianoTool;
    public IReadOnlyOrderedMap<AutomationKey, AutomationConfigEntry> SynthesizedParameterConfigs => mHost.SynthesizedParameterConfigs;

    static AutomationKey DefaultAutomation => AutomationKey.Voice(ConstantDefine.PreCommonAutomationConfigs[0].Key.Id);

    readonly IHost mHost;
    readonly DisposableManager s = new();
    bool mIsCollapsed;
    bool mIsFocused;
    AutomationKey? mActiveAutomation;
    readonly List<AutomationKey> mVisibleAutomations = new();
    // 回显轨显隐集合（默认隐藏，用户经标题栏 chip 点亮）。
    readonly HashSet<AutomationKey> mVisibleSynthesizedParameters = new();
}
