using System;
using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;

namespace TuneLab.UI;

// 一块参数面板：标题栏 + 容器（内含渲染器）+ 这块面板自己的显示状态——激活参数、可见参数、回显轨显隐。
// 各面板各管各的这三样；part、刻度轴、钢琴窗视图、工具、回显轨声明、波形显隐这些共享的东西由宿主（PianoWindow）提供。
// 渲染器与标题栏只经各自的 IDependency 读状态，故它们不知道自己是第几块面板。
internal sealed class ParameterPanel : AutomationRenderer.IDependency, ParameterTitleBar.IDependency
{
    public interface IHost
    {
        IHolder<IMidiPart> PartHolder { get; }
        PianoScrollView PianoScrollView { get; }
        TickAxis TickAxis { get; }
        INotifiableProperty<PianoTool> PianoTool { get; }
        IReadOnlyOrderedMap<AutomationKey, AutomationConfigEntry> SynthesizedParameterConfigs { get; }
        // 波形显隐是全局的独立开关，不属于任何面板；只是显示在面板标题栏上，故经宿主转交。
        IActionEvent WaveformVisibleChanged { get; }
        bool IsWaveformVisible { get; }
        void SetWaveformVisible(bool isVisible);
    }

    public event Action? ActiveAutomationChanged;
    public event Action? VisibleAutomationChanged;
    public event Action? SynthesizedParameterVisibilityChanged;

    public ParameterTitleBar TitleBar { get; }
    public ParameterContainer Container { get; }
    public AutomationRenderer AutomationRenderer => Container.AutomationRenderer;

    public ParameterPanel(IHost host, double titleBarHeight)
    {
        mHost = host;
        Container = new ParameterContainer(this);
        TitleBar = new ParameterTitleBar(this) { Height = titleBarHeight };
        ActiveAutomation = DefaultAutomation;
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
            mVisibleAutomations.Add(automation);

        VisibleAutomationChanged?.Invoke();

        // 配对轨的单向联动：点亮可编辑轨（灭→亮的那一刻）⇒ 同 key 的回显轨一起亮。需求本身不对称——
        // 只看回显是独立的合法用法（对着模型输出观察），而查看自己画的曲线时几乎总要参照模型输出。
        // 故只带亮、不连带熄灭（否则会吞掉用户原本单独开着的回显），也不锁 chip（允许手动关掉）；
        // 一次性带亮而非持续绑定，与固定"不自动同步"同一种克制。
        if (isVisible && !wasVisible && SynthesizedParameterConfigs.ContainsKey(automation))
            SetSynthesizedParameterVisible(automation, true);
    }

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
    public IActionEvent WaveformVisibleChanged => mHost.WaveformVisibleChanged;
    public bool IsWaveformVisible => mHost.IsWaveformVisible;
    public void SetWaveformVisible(bool isVisible) => mHost.SetWaveformVisible(isVisible);

    static AutomationKey DefaultAutomation => AutomationKey.Voice(ConstantDefine.PreCommonAutomationConfigs[0].Key.Id);

    readonly IHost mHost;
    AutomationKey? mActiveAutomation;
    readonly List<AutomationKey> mVisibleAutomations = new();
    // 回显轨显隐集合（默认隐藏，用户经标题栏 chip 点亮）。
    readonly HashSet<AutomationKey> mVisibleSynthesizedParameters = new();
}
