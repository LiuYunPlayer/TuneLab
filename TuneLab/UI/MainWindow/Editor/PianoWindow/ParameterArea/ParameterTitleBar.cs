using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;

using Point = Avalonia.Point;

namespace TuneLab.UI;

// 参数区标题栏：既是拖拽改高的把手（空白区拖动），也是合成参数回显轨的显隐工具条。
// 结构 = LayerPanel 两层：底层 MovableComponent 全幅拖拽把手；上层内容行无背景、空白区指针直落把手，
// 按钮全部复用 Toggle 控件（悬浮/按下色彩过渡由 Button 提供，与底部 tabbar 同源同手感），
// 「按在按钮 = 切换、按在空白 = 拖拽」的手势仲裁由控件树命中天然完成，无需手写。
// 回显轨是 voice 级扁平只读集合，不入分源的底部 tabbar，故显隐按钮收在这条标题栏里（右对齐）。
// 复用参数栏的小眼睛图标表显隐：眼睛点亮（config 色）= 可见，眼睛暗（灰白）= 隐藏；悬浮时图标/文字各提亮一档。
// 左对齐另置一个波形带显隐开关（波形条图标 + 文本，图标点亮=展开 / 暗=收起），与回显轨显隐相互独立。
internal class ParameterTitleBar : LayerPanel
{
    public event Action<double>? Moved;

    public interface IDependency
    {
        event Action? ReadbackVisibilityChanged;
        IHolder<IMidiPart> PartHolder { get; }
        // 回显轨声明按 AutomationKey 分源（voice / 各 effect），按源序排列（voice 在前、各 effect 按 index）。
        IReadOnlyOrderedMap<AutomationKey, AutomationConfigEntry> ReadbackConfigs { get; }
        bool IsReadbackVisible(AutomationKey key);
        void SetReadbackVisible(AutomationKey key, bool isVisible);
        // 波形带显隐（左对齐开关）：与回显轨显隐相互独立。
        IActionEvent WaveformVisibleChanged { get; }
        bool IsWaveformVisible { get; }
        void SetWaveformVisible(bool isVisible);
    }

    IMidiPart? Part => mDependency.PartHolder.Value;

    public ParameterTitleBar(IDependency dependency)
    {
        mDependency = dependency;
        Background = new Color(255, 51, 51, 64).ToBrush();

        // 底层拖拽把手：Moved 换算成标题栏在父容器内的目标 top（把手在标题栏内恒为 (0,0)，补上自身位置即可）。
        var dragHandle = new MovableComponent();
        dragHandle.Moved.Subscribe(p => Moved?.Invoke(p.Y + Bounds.Y));
        Children.Add(dragHandle);

        var content = new DockPanel() { LastChildFill = false };
        {
            mWaveformToggle = CreateToggle(GUI.Assets.Waveform, WaveformIconSize, "Waveform".Tr(TC.Document), Style.WHITE);
            mWaveformToggle.Margin = new Thickness(LeftMargin - ChipHitPadding, 0, 0, 0);
            mWaveformToggle.Switched.Subscribe(() => mDependency.SetWaveformVisible(mWaveformToggle.IsChecked));
            mWaveformToggle.Display(mDependency.IsWaveformVisible);
            content.AddDock(mWaveformToggle, Dock.Left);

            mChipsPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Thickness(0, 0, RightMargin - ChipHitPadding, 0) };
            content.AddDock(mChipsPanel, Dock.Right);
        }
        Children.Add(content);

        mDependency.ReadbackVisibilityChanged += SyncChipStates;
        mDependency.WaveformVisibleChanged.Subscribe(() => mWaveformToggle.Display(mDependency.IsWaveformVisible), s);
        mDependency.PartHolder.Modified.Subscribe(RebuildChips, s);
        // 换声源 → 回显轨声明随之变，按钮组要立即重建（否则显示的还是旧声源的轨）。
        mDependency.PartHolder.When(p => p.SoundSource.Modified).Subscribe(RebuildChips, s);
        // 回显轨集合随参数 commit 显隐（context 驱动）→ 重建按钮组。
        mDependency.PartHolder.When(p => p.AutomationConfigsModified).Subscribe(RebuildChips, s);
        // effect 增删/重排 + 各 effect 参数变（条件回显轨显隐、分组与显示名）→ 回显分组随之变。
        // 这两类事件在参数编辑期间很密集：RebuildChips 内有构成签名比对，构成未变时不动控件树。
        mDependency.PartHolder.When(p => p.Effects.Modified).Subscribe(RebuildChips, s);
        mDependency.PartHolder.When(p => p.Effects.WhenAny(e => e.Modified)).Subscribe(RebuildChips, s);

        RebuildChips();
    }

    ~ParameterTitleBar()
    {
        s.DisposeAll();
        mDependency.ReadbackVisibilityChanged -= SyncChipStates;
    }

    // 重建回显 chip 行（右对齐，按源分组、按声明序左→右排）：每组前置一个源标签（"Voice" / effect 的 Type），
    // 组内为各回显轨 chip。组间用 "|" 分隔（类似底部 tabbar）；voice 恒在最前且唯一 → 无源标签。
    // 元素序列同时充当重建签名：与上次一致（高频的 effect 数据变动大多如此）则不动控件树，只对齐显隐态。
    void RebuildChips()
    {
        var elems = BuildElems();
        if (elems.SequenceEqual(mElems))
        {
            SyncChipStates();
            return;
        }
        mElems = elems;

        mChipToggles.Clear();
        mChipsPanel.Children.Clear();

        for (int i = 0; i < elems.Count; i++)
        {
            var e = elems[i];
            // 视觉间距沿用旧口径：分隔符旁 DividerGap、标签后首 chip LabelGap、chip 间 ChipGap、首元素无前距。
            double gap = i == 0 ? 0
                : e.Kind == ElemKind.Divider || elems[i - 1].Kind == ElemKind.Divider ? DividerGap
                : elems[i - 1].Kind == ElemKind.Label ? LabelGap
                : ChipGap;
            // chip 命中区两侧各比可视内容宽 ChipHitPadding（折进控件宽度），相邻间距扣除该量保持视觉不变。
            if (e.Kind == ElemKind.Chip)
                gap -= ChipHitPadding;
            if (i > 0 && elems[i - 1].Kind == ElemKind.Chip)
                gap -= ChipHitPadding;

            if (e.Kind == ElemKind.Chip)
            {
                var toggle = CreateToggle(GUI.Assets.Eye, EyeWidth, e.Text, e.Color);
                toggle.Margin = new Thickness(gap, 0, 0, 0);
                var key = e.Key;
                toggle.Switched.Subscribe(() => mDependency.SetReadbackVisible(key, toggle.IsChecked));
                toggle.Display(mDependency.IsReadbackVisible(key));
                mChipToggles.Add(key, toggle);
                mChipsPanel.Children.Add(toggle);
            }
            else
            {
                // 源标签略亮（灰底上要看清）；分隔符更淡（对齐底部 tabbar 分隔线）。
                // 不吃指针：这些淡色文本上仍可按下拖拽改高（穿透到把手），与旧自绘版行为一致。
                mChipsPanel.Children.Add(new TextBlock()
                {
                    Text = e.Text,
                    FontSize = FontSize,
                    Foreground = e.Kind == ElemKind.Divider ? DividerColor : LabelColor,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Margin = new Thickness(gap, 0, 0, 0),
                    IsHitTestVisible = false,
                });
            }
        }
    }

    List<Elem> BuildElems()
    {
        var elems = new List<Elem>();
        var configs = mDependency.ReadbackConfigs;
        if (configs.Count == 0)
            return elems;

        var part = Part;
        int? curSource = null;
        foreach (var kvp in configs)
        {
            var key = kvp.Key;
            if (curSource != key.EffectIndex)
            {
                curSource = key.EffectIndex;
                if (elems.Count > 0)
                    elems.Add(new(ElemKind.Divider, default, DividerText, default));
                if (key.IsEffect)
                {
                    string labelText = part != null && key.EffectIndex < part.Effects.Count ? part.Effects[key.EffectIndex].Type : "Effect";
                    elems.Add(new(ElemKind.Label, default, labelText, default));
                }
            }
            elems.Add(new(ElemKind.Chip, key, kvp.Value.DisplayText, ColorUtils.ParseOrFallback(kvp.Value.Config.Color)));
        }
        return elems;
    }

    void SyncChipStates()
    {
        foreach (var (key, toggle) in mChipToggles)
            toggle.Display(mDependency.IsReadbackVisible(key));
    }

    // 图标 + 文字显隐开关的统一构造：命中区两侧比可视内容各宽 ChipHitPadding（内容经 Offset 内缩）、垂直吃满栏高。
    // 图标点亮色随勾选态（悬浮向白提亮 30%，纯白点亮态如波形开关则不变、由文字承担反馈），压暗态灰白（悬浮升不透明度）；
    // 文字恒亮 LIGHT_WHITE（开/关仅由图标染色区分）、悬浮提亮到纯白。
    Toggle CreateToggle(SvgIcon icon, double iconWidth, string text, Color litIconColor)
    {
        var hoverLit = litIconColor.Lerp(Style.WHITE, 0.3);
        var toggle = new Toggle() { Width = ChipHitPadding + iconWidth + EyeTextGap + MeasureTextWidth(text) + ChipHitPadding };
        toggle.AddContent(new()
        {
            Item = new IconItem() { Icon = icon, Alignment = Alignment.LeftCenter, Offset = new Point(ChipHitPadding, 0) },
            CheckedColorSet = new() { Color = litIconColor, HoveredColor = hoverLit, PressedColor = hoverLit },
            UncheckedColorSet = new() { Color = EyeOffColor, HoveredColor = HoverOffColor, PressedColor = HoverOffColor },
        });
        toggle.AddContent(new()
        {
            Item = new TextItem() { Text = text, FontSize = FontSize, Alignment = Alignment.LeftCenter, PivotAlignment = Alignment.LeftCenter, Offset = new Point(ChipHitPadding + iconWidth + EyeTextGap, 0) },
            ColorSet = new() { Color = Style.LIGHT_WHITE, HoveredColor = Style.WHITE, PressedColor = Style.WHITE },
        });
        return toggle;
    }

    static double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var formattedText = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, AppFont.Typeface, FontSize, Brushes.White);
        return formattedText.Width;
    }

    enum ElemKind { Label, Divider, Chip }
    // 布局元素（源标签 / 分隔符 / 回显轨 chip）：先铺序列再物化控件，值语义 = 重建签名。
    readonly record struct Elem(ElemKind Kind, AutomationKey Key, string Text, Color Color);

    const double FontSize = 12;
    const double EyeWidth = 12;
    const double EyeTextGap = 5;
    const double ChipGap = 16;
    const double LabelGap = 8;
    const double DividerGap = 10;
    const double RightMargin = 10;
    const double LeftMargin = 10;
    const double WaveformIconSize = 14;
    const double ChipHitPadding = 6;
    const string DividerText = "|";

    static readonly Color EyeOffColor = new(102, 255, 255, 255);
    // 暗态（隐藏）图标的悬浮提亮档：仍低于点亮态，保持状态可辨。
    static readonly Color HoverOffColor = new(180, 255, 255, 255);
    // 源标签略亮（灰底上要看清）；分隔符更淡，对齐底部 tabbar 的分隔线（LIGHT_WHITE @ 0.25）。
    static readonly IBrush LabelColor = Style.LIGHT_WHITE.Opacity(0.55).ToBrush();
    static readonly IBrush DividerColor = Style.LIGHT_WHITE.Opacity(0.25).ToBrush();

    List<Elem> mElems = new();
    readonly Toggle mWaveformToggle;
    readonly StackPanel mChipsPanel;
    readonly Dictionary<AutomationKey, Toggle> mChipToggles = new();
    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
