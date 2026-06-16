using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.SDK;
using TuneLab.Utils;

using Point = Avalonia.Point;

namespace TuneLab.UI;

// 参数区标题栏：既是拖拽改高的把手（空白区拖动），也是合成参数回显轨的显隐工具条。
// 回显轨是 voice 级扁平只读集合，不入分源的底部 tabbar，故显隐按钮收在这条标题栏里（右对齐）。
// 复用参数栏的小眼睛图标表显隐：眼睛点亮（config 色）= 可见，眼睛暗（灰白）= 隐藏。
// 手势共存：按在按钮上 = 切换该回显轨显隐（不拖拽）；按在空白区 = 沿用拖拽改高。
internal class ParameterTitleBar : MovableComponent
{
    public new event Action<double>? Moved;

    public interface IDependency
    {
        event Action? ReadbackVisibilityChanged;
        IHolder<IMidiPart> PartHolder { get; }
        IReadOnlyOrderedMap<string, AutomationConfig> ReadbackConfigs { get; }
        bool IsReadbackVisible(string id);
        void SetReadbackVisible(string id, bool isVisible);
    }

    public ParameterTitleBar(IDependency dependency)
    {
        mDependency = dependency;
        base.Moved.Subscribe(p => Moved?.Invoke(p.Y));

        mDependency.ReadbackVisibilityChanged += InvalidateVisual;
        mDependency.PartHolder.Modified.Subscribe(InvalidateVisual, s);
        // 换声源 → 回显轨声明随之变（按钮组要立即重绘，否则要等鼠标移上来才刷新）。
        mDependency.PartHolder.When(p => p.Voice.Modified).Subscribe(InvalidateVisual, s);
        // 回显轨集合随参数 commit 显隐（context 驱动）→ 重绘按钮组。
        mDependency.PartHolder.When(p => p.AutomationConfigsModified).Subscribe(InvalidateVisual, s);
    }

    ~ParameterTitleBar()
    {
        s.DisposeAll();
        mDependency.ReadbackVisibilityChanged -= InvalidateVisual;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(new Color(255, 51, 51, 64).ToBrush(), this.Rect());

        foreach (var chip in LayoutChips())
        {
            bool visible = mDependency.IsReadbackVisible(chip.Id);

            // 小眼睛：可见时 config 色、隐藏时灰白（沿用参数栏按钮的眼睛语义）。
            var eyeColor = visible ? chip.Color : EyeOffColor;
            var eye = GetEyeImage(eyeColor);
            double eyeTop = chip.Rect.Y + (chip.Rect.Height - EyeHeight) / 2;
            eye.Draw(context, new Rect(eye.Size), new Rect(chip.Rect.X, eyeTop, EyeWidth, EyeHeight));

            var textColor = (visible ? Style.LIGHT_WHITE : Style.LIGHT_WHITE.Opacity(0.5)).ToBrush();
            context.DrawString(chip.Text, new Point(chip.Rect.X + EyeWidth + EyeTextGap, chip.Rect.Y + chip.Rect.Height / 2), textColor, FontSize, Alignment.LeftCenter);
        }
    }

    protected override void OnMouseDown(MouseDownEventArgs e)
    {
        if (e.MouseButtonType == MouseButtonType.PrimaryButton && TryHitChip(e.Position, out var id))
        {
            // 按钮命中：切换显隐、吞掉本次按下（不进入拖拽）。
            mPressOnChip = true;
            mDependency.SetReadbackVisible(id, !mDependency.IsReadbackVisible(id));
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

    bool TryHitChip(Point position, out string id)
    {
        foreach (var chip in LayoutChips())
        {
            if (chip.HitRect.Contains(position))
            {
                id = chip.Id;
                return true;
            }
        }
        id = string.Empty;
        return false;
    }

    // 计算按钮组布局（右对齐，按声明序左→右排）：眼睛 + 文本，命中区含文本宽度。布局廉价、按需即算。
    List<Chip> LayoutChips()
    {
        var result = new List<Chip>();

        var measured = new List<(string Id, string Text, Color Color, double Width)>();
        double total = 0;
        foreach (var kvp in mDependency.ReadbackConfigs)
        {
            var config = kvp.Value;
            string text = config.DisplayText ?? kvp.Key;
            double width = EyeWidth + EyeTextGap + MeasureTextWidth(text);
            measured.Add((kvp.Key, text, Color.Parse(config.Color), width));
            total += width;
        }
        if (measured.Count == 0)
            return result;

        total += ChipGap * (measured.Count - 1);

        double height = Bounds.Height;
        double x = Bounds.Width - RightMargin - total;
        foreach (var m in measured)
        {
            var rect = new Rect(x, 0, m.Width, height);
            result.Add(new Chip(m.Id, m.Text, m.Color, rect, rect.Inflate(new Thickness(ChipHitPadding, 0))));
            x += m.Width + ChipGap;
        }
        return result;
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

    readonly record struct Chip(string Id, string Text, Color Color, Rect Rect, Rect HitRect);

    const double FontSize = 12;
    const double EyeWidth = 12;
    const double EyeHeight = 10;
    const double EyeTextGap = 5;
    const double ChipGap = 16;
    const double RightMargin = 10;
    const double ChipHitPadding = 6;

    static readonly Color EyeOffColor = new(102, 255, 255, 255);

    bool mPressOnChip;
    readonly Dictionary<uint, IImage> mEyeCache = new();
    readonly IDependency mDependency;
    readonly DisposableManager s = new();
}
