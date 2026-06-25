using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.I18N;
using TuneLab.SDK;
using TuneLab.Utils;

using Point = Avalonia.Point;

namespace TuneLab.UI;

// 参数区标题栏：既是拖拽改高的把手（空白区拖动），也是合成参数回显轨的显隐工具条。
// 回显轨是 voice 级扁平只读集合，不入分源的底部 tabbar，故显隐按钮收在这条标题栏里（右对齐）。
// 复用参数栏的小眼睛图标表显隐：眼睛点亮（config 色）= 可见，眼睛暗（灰白）= 隐藏。
// 左对齐另置一个波形带显隐开关（声波图标 + 文本，图标点亮=展开 / 暗=收起），与回显轨显隐相互独立。
// 手势共存：按在按钮上 = 切换该显隐（不拖拽）；按在空白区 = 沿用拖拽改高。
internal class ParameterTitleBar : MovableComponent
{
    public new event Action<double>? Moved;

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
        base.Moved.Subscribe(p => Moved?.Invoke(p.Y));

        mDependency.ReadbackVisibilityChanged += InvalidateVisual;
        mDependency.WaveformVisibleChanged.Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.Modified.Subscribe(InvalidateVisual, s);
        // 换声源 → 回显轨声明随之变（按钮组要立即重绘，否则要等鼠标移上来才刷新）。
        mDependency.PartHolder.When(p => p.SoundSource.Modified).Subscribe(InvalidateVisual, s);
        // 回显轨集合随参数 commit 显隐（context 驱动）→ 重绘按钮组。
        mDependency.PartHolder.When(p => p.AutomationConfigsModified).Subscribe(InvalidateVisual, s);
        // effect 增删/重排 + 各 effect 参数变（条件回显轨显隐、分组与显示名）→ 回显分组随之变，重绘。
        mDependency.PartHolder.When(p => p.Effects.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.When(p => p.Effects.WhenAny(e => e.Modified)).Subscribe(InvalidateVisual, s);
    }

    ~ParameterTitleBar()
    {
        s.DisposeAll();
        mDependency.ReadbackVisibilityChanged -= InvalidateVisual;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new Color(255, 51, 51, 64).ToBrush(), this.Rect());

        // 左对齐：波形带显隐开关。图标点亮（亮白）= 展开、暗（灰白）= 收起；文字恒亮（与右侧回显轨 chip 同口径）。
        {
            var toggle = WaveformToggle();
            var iconColor = mDependency.IsWaveformVisible ? Style.WHITE : EyeOffColor;
            var icon = GetWaveformIcon(iconColor);
            icon.Draw(context, new Rect(icon.Size), toggle.IconRect);
            context.DrawString(toggle.Text, new Point(toggle.TextX, Bounds.Height / 2), Style.LIGHT_WHITE.ToBrush(), FontSize, Alignment.LeftCenter);
        }

        var (labels, chips) = Layout();

        // 源标签 / 分隔符（淡色、不可点）：源标签略亮（灰底上要看清），分隔符更淡（对齐底部 tabbar 分隔线）。
        foreach (var label in labels)
            context.DrawString(label.Text, new Point(label.Rect.X, label.Rect.Y + label.Rect.Height / 2), label.Brush, FontSize, Alignment.LeftCenter);

        foreach (var chip in chips)
        {
            bool visible = mDependency.IsReadbackVisible(chip.Key);

            // 小眼睛：可见时 config 色、隐藏时灰白（沿用参数栏按钮的眼睛语义）。
            var eyeColor = visible ? chip.Color : EyeOffColor;
            var eye = GetEyeImage(eyeColor);
            double eyeTop = chip.Rect.Y + (chip.Rect.Height - EyeHeight) / 2;
            eye.Draw(context, new Rect(eye.Size), new Rect(chip.Rect.X, eyeTop, EyeWidth, EyeHeight));

            // 文字恒亮（开/关状态仅由小眼睛染色区分；隐藏时不再压暗文字）。
            var textColor = Style.LIGHT_WHITE.ToBrush();
            context.DrawString(chip.Text, new Point(chip.Rect.X + EyeWidth + EyeTextGap, chip.Rect.Y + chip.Rect.Height / 2), textColor, FontSize, Alignment.LeftCenter);
        }
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        if (e.MouseButtonType == MouseButtonType.PrimaryButton && WaveformToggle().HitRect.Contains(e.Position))
        {
            // 波形开关命中：切换波形带显隐、吞掉本次按下（不进入拖拽）。
            mPressOnChip = true;
            mDependency.SetWaveformVisible(!mDependency.IsWaveformVisible);
            return;
        }

        if (e.MouseButtonType == MouseButtonType.PrimaryButton && TryHitChip(e.Position, out var key))
        {
            // 按钮命中：切换显隐、吞掉本次按下（不进入拖拽）。
            mPressOnChip = true;
            mDependency.SetReadbackVisible(key, !mDependency.IsReadbackVisible(key));
            return;
        }

        mPressOnChip = false;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseMoveEventArgs e)
    {
        if (mPressOnChip)
            return;

        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseUpEventArgs e)
    {
        if (mPressOnChip)
        {
            mPressOnChip = false;
            return;
        }

        base.OnMouseUp(e);
    }

    bool TryHitChip(Point position, out AutomationKey key)
    {
        var (_, chips) = Layout();
        foreach (var chip in chips)
        {
            if (chip.HitRect.Contains(position))
            {
                key = chip.Key;
                return true;
            }
        }
        key = default;
        return false;
    }

    // 计算布局（右对齐，按源分组、按声明序左→右排）：每组前置一个源标签（"Voice" / effect 的 Type），
    // 组内为各回显轨 chip（眼睛 + 文本，命中区含文本宽度）。组间留更大间距、标签到首 chip 留小间距。
    // 布局廉价、按需即算（回显轨集合规模小、不在热路径）。
    (List<LabelItem> Labels, List<Chip> Chips) Layout()
    {
        var labels = new List<LabelItem>();
        var chips = new List<Chip>();

        var configs = mDependency.ReadbackConfigs;
        if (configs.Count == 0)
            return (labels, chips);

        var part = Part;

        // 先按源分组铺出元素序列（chip... | label chip... | label chip...）连同各自宽度，再按总宽右对齐定位。
        // 组间用 "|" 分隔（类似底部 tabbar）；voice 恒在最前且唯一 → 无源标签，各 effect 组前置 Type 名标签。
        var elems = new List<Elem>();
        int? curSource = null;
        foreach (var kvp in configs)
        {
            var key = kvp.Key;
            if (curSource != key.EffectIndex)
            {
                curSource = key.EffectIndex;
                if (elems.Count > 0)
                    elems.Add(Elem.Divider(MeasureTextWidth(DividerText)));
                if (key.IsEffect)
                {
                    string labelText = part != null && key.EffectIndex < part.Effects.Count ? part.Effects[key.EffectIndex].Type : "Effect";
                    elems.Add(Elem.Label(labelText, MeasureTextWidth(labelText)));
                }
            }
            var config = kvp.Value.Config;
            string text = kvp.Value.DisplayText;
            double width = EyeWidth + EyeTextGap + MeasureTextWidth(text);
            elems.Add(Elem.Chip(key, text, Color.Parse(config.Color), width));
        }

        // 各元素前距：分隔符及其相邻元素留 DividerGap；标签后首 chip 留 LabelGap；chip 间 ChipGap；首元素无前距。
        double total = 0;
        for (int i = 0; i < elems.Count; i++)
        {
            double gap = i == 0 ? 0
                : elems[i].IsDivider || elems[i - 1].IsDivider ? DividerGap
                : elems[i - 1].IsLabel ? LabelGap
                : ChipGap;
            elems[i].GapBefore = gap;
            total += gap + elems[i].Width;
        }

        double height = Bounds.Height;
        double x = Bounds.Width - RightMargin - total;
        foreach (var e in elems)
        {
            x += e.GapBefore;
            var rect = new Rect(x, 0, e.Width, height);
            // 源标签与分隔符同为淡色不可点文本，进 labels（各带自己的色）；chip 进 chips（带命中区）。
            if (e.IsLabel || e.IsDivider)
                labels.Add(new LabelItem(e.Text, rect, e.IsDivider ? DividerColor : LabelColor));
            else
                chips.Add(new Chip(e.Key, e.Text, e.Color, rect, rect.Inflate(new Thickness(ChipHitPadding, 0))));
            x += e.Width;
        }
        return (labels, chips);
    }

    // 左对齐波形开关的图标/文本/命中区（声波图标 + "Waveform" 文本，命中区含文本宽度并左右内缩）。
    (Rect IconRect, double TextX, string Text, Rect HitRect) WaveformToggle()
    {
        string text = "Waveform".Tr(TC.Document);
        double iconTop = (Bounds.Height - WaveformIconSize) / 2;
        var iconRect = new Rect(LeftMargin, iconTop, WaveformIconSize, WaveformIconSize);
        double textX = LeftMargin + WaveformIconSize + EyeTextGap;
        double width = WaveformIconSize + EyeTextGap + MeasureTextWidth(text);
        var hitRect = new Rect(LeftMargin, 0, width, Bounds.Height).Inflate(new Thickness(ChipHitPadding, 0));
        return (iconRect, textX, text, hitRect);
    }

    IImage GetWaveformIcon(Color color)
    {
        uint key = color.ToUInt32();
        if (!mWaveformIconCache.TryGetValue(key, out var image))
        {
            image = GUI.Assets.Audio.GetImage(color);
            mWaveformIconCache[key] = image;
        }
        return image;
    }

    IImage GetEyeImage(Color color)
    {
        uint key = color.ToUInt32();
        if (!mEyeCache.TryGetValue(key, out var image))
        {
            image = GUI.Assets.Eye.GetImage(color);
            mEyeCache[key] = image;
        }
        return image;
    }

    static double MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var formattedText = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, FontSize, Brushes.White);
        return formattedText.Width;
    }

    readonly record struct Chip(AutomationKey Key, string Text, Color Color, Rect Rect, Rect HitRect);
    readonly record struct LabelItem(string Text, Rect Rect, IBrush Brush);

    // 布局元素（源标签 / 分隔符 / 回显轨 chip）：先铺序列、计前距，再右对齐定位。
    sealed class Elem
    {
        public bool IsLabel;     // 源标签（effect Type 名），淡色不可点
        public bool IsDivider;   // 组间 "|" 分隔，淡色不可点
        public AutomationKey Key;
        public string Text = string.Empty;
        public Color Color;
        public double Width;
        public double GapBefore;

        public static Elem Label(string text, double width) => new() { IsLabel = true, Text = text, Width = width };
        public static Elem Divider(double width) => new() { IsDivider = true, Text = DividerText, Width = width };
        public static Elem Chip(AutomationKey key, string text, Color color, double width)
            => new() { Key = key, Text = text, Color = color, Width = width };
    }

    const double FontSize = 12;
    const double EyeWidth = 12;
    const double EyeHeight = 10;
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
    // 源标签略亮（灰底上要看清）；分隔符更淡，对齐底部 tabbar 的分隔线（LIGHT_WHITE @ 0.25）。
    static readonly IBrush LabelColor = Style.LIGHT_WHITE.Opacity(0.55).ToBrush();
    static readonly IBrush DividerColor = Style.LIGHT_WHITE.Opacity(0.25).ToBrush();

    bool mPressOnChip;
    readonly Dictionary<uint, IImage> mEyeCache = new();
    readonly Dictionary<uint, IImage> mWaveformIconCache = new();
    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
