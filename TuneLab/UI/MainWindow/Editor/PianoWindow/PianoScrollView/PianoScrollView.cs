using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using DynamicData;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Input;
using TuneLab.Data;
using TuneLab.SDK;
using TuneLab.Extensions.Voices;
using TuneLab.Extensions.Instruments;
using TuneLab.Utils;
using TuneLab.I18N;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TuneLab.Configs;
using System.IO;

using Point = Avalonia.Point;

namespace TuneLab.UI;

internal partial class PianoScrollView : View, IPianoScrollView
{
    public interface IDependency
    {
        INotifiableProperty<PianoTool> PianoTool { get; }
        IPlayhead Playhead { get; }
        TickAxis TickAxis { get; }
        PitchAxis PitchAxis { get; }
        IQuantization Quantization { get; }
        IHolder<IMidiPart> PartHolder { get; }
        ParameterButton PitchButton { get; }
        AutomationRenderer AutomationRenderer { get; }
        double WaveformBottom { get; }
        IActionEvent WaveformBottomChanged { get; }
        bool IsWaveformVisible { get; }
        IActionEvent WaveformVisibleChanged { get; }
    }

    // 能否粘贴只看剪贴板里有没有东西（与当前工具无关）——粘贴语义 = 粘贴上次复制的全部内容。
    public bool CanPaste => !mNoteClipboard.IsEmpty() || !mVibratoClipboard.IsEmpty() || !mParameterClipboard.IsEmpty;
    public State OperationState => mState;

    public PianoScrollView(IDependency dependency)
    {
        mDependency = dependency;

        mMiddleDragOperation = new(this);
        mNoteSelectOperation = new(this);
        mPitchDrawOperation = new(this);
        mPitchClearOperation = new(this);
        mPitchLockOperation = new(this);
        mNoteMoveOperation = new(this); 
        mNoteStartResizeOperation = new(this);
        mNoteEndResizeOperation = new(this);
        mVibratoSelectOperation = new(this);
        mVibratoStartResizeOperation = new(this);
        mVibratoEndResizeOperation = new(this);
        mVibratoAmplitudeOperation = new(this);
        mVibratoFrequencyOperation = new(this);
        mVibratoPhaseOperation = new(this);
        mVibratoAttackOperation = new(this);
        mVibratoReleaseOperation = new(this);
        mVibratoMoveOperation = new(this);
        mWaveformPhonemeResizeOperation = new(this);
        mSelectionOperation = new(this);
        mAnchorSelectOperation = new(this);
        mAnchorDeleteOperation = new(this);
        mAnchorMoveOperation = new(this);

        mDependency.PartHolder.Modified.Subscribe(Update, s);
        mDependency.PartHolder.Modified.Subscribe(ClearRegionSelection, s);   // 切 part：上一 part 的范围选区作废
        mDependency.PartHolder.When(p => p.Modified).Subscribe(Update, s);
        // 侧栏属性拖动（Gain→波形振幅、音素时长/权重→音素带，及任意属性）经数据层 merge notify：拖动期只发 canIgnore
        // 中间态，结果态（默认脸 p.Modified，即上一条订 Update 的那个）要松手才发，故上一条拖动中不触发。这里另订全量脸
        // （AsEverytime，中间态也触发）只做重绘——使波形/音素带在拖动每帧按新值跟手刷新，不必等提交。重建交互热区仍留给
        // 结果态的 Update：这类拖动都发生在侧栏，钢琴窗热区中途无需更新。音素几何变化本就沿链传到 p.Modified，故此条亦覆盖之。
        mDependency.PartHolder.When(p => p.Modified.AsEverytime()).Subscribe(_ => InvalidateVisual(), s);
        mDependency.PartHolder.When(p => p.SynthesisStatusChanged).Subscribe(OnSynthesisStatusChanged, s);
        mDependency.PartHolder.When(p => p.Notes.SelectionChanged).Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.When(p => p.Vibratos.WhenAny(vibrato => vibrato.SelectionChanged)).Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.When(p => p.Pitch.Modified).Subscribe(InvalidateVisual, s); 
        mDependency.PartHolder.When(p => p.Track.Project.Tracks.WhenAny(track => track.AsRefer.Modified)).Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.When(p => p.Track.Project.Tracks.WhenAny(track => track.Color.Modified)).Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.When(p => p.TempoManager.Modified).Subscribe(InvalidateVisual, s);
        mDependency.PartHolder.When(p => p.TimeSignatureManager.Modified).Subscribe(InvalidateVisual, s);
        mDependency.WaveformBottomChanged.Subscribe(InvalidateVisual, s);
        mDependency.WaveformVisibleChanged.Subscribe(Update, s);
        mDependency.PianoTool.Modified.Subscribe(InvalidateVisual, s);
        TickAxis.AxisChanged += Update;
        PitchAxis.AxisChanged += Update;
        Quantization.QuantizationChanged += InvalidateVisual;
        PitchButton.StateChanged += InvalidateVisual;

        mLyricInput = new TextInput()
        {
            Padding = new Thickness(8, LyricInputVerticalPadding),
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderThickness = new(0),
            FontSize = LyricInputFontSize,
            CaretBrush = Brushes.Black,
            IsVisible = false
        };
        mLyricInput.EndInput.Subscribe(OnLyricInputComplete);
        Children.Add(mLyricInput);

        // 音素输入框以「音素文字」为锚：同字号、文字水平居中、矩形中心 = 音素文字中心（见 PhonemeInputRect），
        // 弹出瞬间框内文字与底下画的符号同位置不跳。矮壳（垂直零 padding，靠 VerticalContentAlignment 居中）。
        mPhonemeInput = new TextInput()
        {
            Padding = new Thickness(2, 0),
            // TextInput 基类默认 HorizontalContentAlignment=Left：TextPresenter 只有内容宽、贴左，
            // TextAlignment.Center 会失效——须 Stretch 让 presenter 占满框宽，居中才作用于整框。
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            TextAlignment = TextAlignment.Center,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderThickness = new(0),
            FontSize = LyricInputFontSize,
            CaretBrush = Brushes.Black,
            IsVisible = false
        };
        mPhonemeInput.EndInput.Subscribe(OnPhonemeInputComplete);
        Children.Add(mPhonemeInput);

        ClipToBounds = true;

        TickAxis.AxisChanged += InvalidateArrange;
        PitchAxis.AxisChanged += InvalidateArrange;

        Settings.BackgroundImagePath.Modified.Subscribe(LoadBackgroundImage, s);
        Settings.BackgroundImageOpacity.Modified.Subscribe(InvalidateVisual, s);
        LoadBackgroundImage();

        // voice 立绘随当前 part 及其音源（换引擎 / 换声库）变化重解析；动图播放随挂载 / 卸载视觉树起停（不可见不空转）。
        mDependency.PartHolder.Modified.Subscribe(LoadPortrait, s);
        mDependency.PartHolder.When(p => p.SoundSource.Modified).Subscribe(LoadPortrait, s);
        AttachedToVisualTree += (_, _) => UpdateImagePlayback();
        DetachedFromVisualTree += (_, _) => UpdateImagePlayback();
        LoadPortrait();
    }

    ~PianoScrollView()
    {
        s.DisposeAll();
        TickAxis.AxisChanged -= Update;
        PitchAxis.AxisChanged -= Update;
        Quantization.QuantizationChanged -= InvalidateVisual;
        PitchButton.StateChanged -= InvalidateVisual;
    }

    void OnSynthesisStatusChanged()
    {
        if (Part == null)
            return;

        InvalidateVisual();
    }

    // —— 合成状态条 —— //
    const double SynthesisStripTop = 0;
    const double SynthesisStripHeight = 4;       // 视觉细带
    const double SynthesisStripRadius = 2;
    const double SynthesisHoverPadding = 4;      // hover/右键命中区 = 细带 + 这点余量 ≈ 8px；别太大，免得挡住顶部画 note
    const double SynthesisHoverDelaySeconds = 0.3; // hover 弹文案的延时：鼠标路过不闪、停够才弹
    const double TopShadowHeight = 18;           // 顶部向下渐隐的暗影高度（常驻，标尺与内容的层次）

    // 顶部渐变阴影：常驻，与状态条无关——让上方 TimelineView 标尺与下方音符内容有层次。
    void DrawTopShadow(DrawingContext context)
    {
        var shadowBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Colors.Black.Opacity(0.3), 0),
                new GradientStop(Colors.Black.Opacity(0), 1),
            }
        };
        context.FillRectangle(shadowBrush, new Rect(0, 0, Bounds.Width, TopShadowHeight));
    }

    // —— 合成状态条 hover 延时：鼠标进命中区起表，停够 SynthesisHoverDelaySeconds 才算“就绪”可弹；划走即取消。 —— //
    bool mSynthesisHovering;
    bool mSynthesisHoverReady;
    DispatcherTimer? mSynthesisHoverTimer;

    void UpdateSynthesisHover(Point position)
    {
        bool inZone = position.Y >= SynthesisStripTop && position.Y <= SynthesisStripTop + SynthesisStripHeight + SynthesisHoverPadding;
        if (inZone)
        {
            if (mSynthesisHovering)
                return;

            mSynthesisHovering = true;
            mSynthesisHoverReady = false;
            mSynthesisHoverTimer ??= CreateSynthesisHoverTimer();
            mSynthesisHoverTimer.Stop();
            mSynthesisHoverTimer.Start();
        }
        else if (mSynthesisHovering)
        {
            mSynthesisHovering = false;
            mSynthesisHoverReady = false;
            mSynthesisHoverTimer?.Stop();
            InvalidateVisual();
        }
    }

    DispatcherTimer CreateSynthesisHoverTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SynthesisHoverDelaySeconds) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            mSynthesisHoverReady = true;
            InvalidateVisual();
        };
        return timer;
    }
    const double SynthesisShimmerPeriod = 1.25;  // 流光一趟的秒数

    DispatcherTimer? mSynthesisShimmerTimer;
    readonly Stopwatch mSynthesisShimmerClock = new();

    void EnsureSynthesisShimmer()
    {
        mSynthesisShimmerTimer ??= CreateSynthesisShimmerTimer();
        if (!mSynthesisShimmerTimer.IsEnabled)
        {
            mSynthesisShimmerClock.Restart();
            mSynthesisShimmerTimer.Start();
        }
    }

    DispatcherTimer CreateSynthesisShimmerTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => InvalidateVisual();
        return timer;
    }

    void StopSynthesisShimmer()
    {
        if (mSynthesisShimmerTimer is { IsEnabled: true })
            mSynthesisShimmerTimer.Stop();
        mSynthesisShimmerClock.Reset();
    }

    // hover 命中某段 → 在细带下方弹出文案 pill（阶段/进度/错误全文）；pill 锚在段首、不跟随鼠标、渐显渐隐。
    // 失败不再画 ⚠——红条本身已是足够的失败信号；要拿走文字用右键「复制」（见 TrySynthesisStripCopy）。
    void DrawSynthesisStatusOverlay(DrawingContext context, IReadOnlyList<SynthesisStatusSegment> segments, ITempoManager tempoManager)
    {
        // 本帧是否该显示 pill：就绪 + 鼠标在命中区 + 命中到有文案的段。命中时记下内容（渐隐期间仍用这份）。
        bool show = false;
        if (IsHover && mSynthesisHoverReady)
        {
            var mouse = MousePosition;
            if (mouse.Y >= SynthesisStripTop && mouse.Y <= SynthesisStripTop + SynthesisStripHeight + SynthesisHoverPadding)
            {
                foreach (var seg in segments)
                {
                    double left = TickAxis.Tick2X(tempoManager.GetTick(seg.StartTime));
                    double right = TickAxis.Tick2X(tempoManager.GetTick(seg.EndTime));
                    if (mouse.X < left || mouse.X > right)
                        continue;

                    string text = SynthesisStatusText(seg);
                    if (string.IsNullOrEmpty(text))
                        break;

                    // 正文（报错/进度）恒在；仅尾部提示在“右键复制 ↔ Copied”间原地切换，文案框不变形、不消失。
                    bool copyable = seg.Status == SynthesisSegmentStatus.Failed || !string.IsNullOrEmpty(seg.Message);
                    string? hint = null;
                    if (copyable)
                    {
                        bool showCopied = !double.IsNaN(mSynthesisCopiedSegmentStart)
                            && Math.Abs(mSynthesisCopiedSegmentStart - seg.StartTime) < 1e-6
                            && mSynthesisCopyClock.Elapsed.TotalSeconds < SynthesisCopyFeedbackSeconds;
                        hint = showCopied ? "Copied".Tr(this) : "Right-click to copy".Tr(this);
                    }

                    mTooltipText = text;       // 锚在段首；记下内容供渐隐期间继续绘制
                    mTooltipHint = hint;
                    mTooltipAnchorX = left;
                    show = true;
                    break;
                }
            }
        }

        AdvanceTooltipFade(show ? 1 : 0);
        if (mTooltipOpacity > 0.01 && !string.IsNullOrEmpty(mTooltipText))
            DrawSynthesisTooltip(context, mTooltipAnchorX, mTooltipText!, mTooltipHint, mTooltipOpacity);
    }

    // —— pill 渐显渐隐：每帧把不透明度朝目标(0/1)推进，过渡期用 16ms 定时器持续重绘。 —— //
    const double TooltipFadeSeconds = 0.12;
    double mTooltipOpacity;
    string? mTooltipText;
    string? mTooltipHint;
    double mTooltipAnchorX;
    DispatcherTimer? mTooltipFadeTimer;
    readonly Stopwatch mTooltipFadeClock = new();

    void AdvanceTooltipFade(double target)
    {
        if (mTooltipOpacity == target)
        {
            if (mTooltipFadeTimer is { IsEnabled: true })
                mTooltipFadeTimer.Stop();
            mTooltipFadeClock.Reset();
            return;
        }

        double dt = mTooltipFadeClock.IsRunning ? mTooltipFadeClock.Elapsed.TotalSeconds : 0;
        mTooltipFadeClock.Restart();
        double step = TooltipFadeSeconds <= 0 ? 1 : dt / TooltipFadeSeconds;
        mTooltipOpacity = target > mTooltipOpacity
            ? Math.Min(target, mTooltipOpacity + step)
            : Math.Max(target, mTooltipOpacity - step);

        mTooltipFadeTimer ??= CreateTooltipFadeTimer();
        if (!mTooltipFadeTimer.IsEnabled)
            mTooltipFadeTimer.Start();
    }

    DispatcherTimer CreateTooltipFadeTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) => InvalidateVisual();
        return timer;
    }

    string SynthesisStatusText(SynthesisStatusSegment seg)
    {
        switch (seg.Status)
        {
            case SynthesisSegmentStatus.Pending:
                return "Pending".Tr(this);
            case SynthesisSegmentStatus.Synthesized:
                return "Synthesized".Tr(this);
            case SynthesisSegmentStatus.Failed:
                return string.IsNullOrEmpty(seg.Message) ? "Synthesis failed".Tr(this) : seg.Message;
            case SynthesisSegmentStatus.Synthesizing:
                string head = seg.Progress > 0 ? $"{(int)Math.Round(seg.Progress.Limit(0, 1) * 100)}%" : "Synthesizing".Tr(this);
                return string.IsNullOrEmpty(seg.Message) ? head : head + " · " + seg.Message;
            default:
                return string.Empty;
        }
    }

    // 段首锚定的文案 pill：主文案（白）+ 可选暗色提示（如“右键复制”，LIGHT_WHITE 弱化）；opacity 驱动渐显渐隐。
    void DrawSynthesisTooltip(DrawingContext context, double anchorX, string text, string? hint, double opacity)
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var face = AppFont.Typeface;
        var fMain = new FormattedText(text, culture, FlowDirection.LeftToRight, face, 12, null);
        FormattedText? fHint = string.IsNullOrEmpty(hint) ? null : new FormattedText(hint, culture, FlowDirection.LeftToRight, face, 12, null);

        const double padH = 8, padV = 4, gap = 10;
        double textW = fMain.Width + (fHint != null ? gap + fHint.Width : 0);
        double w = textW + padH * 2;
        double h = Math.Max(fMain.Height, fHint?.Height ?? 0) + padV * 2;
        double x = Math.Clamp(anchorX, 0, Math.Max(0, Bounds.Width - w));
        double y = SynthesisStripTop + SynthesisStripHeight + 4;
        var rect = new Rect(x, y, w, h);
        context.DrawRectangle(Style.DARK.Opacity(0.92 * opacity).ToBrush(), new Pen(Style.LINE.Opacity(opacity).ToBrush(), 1), new RoundedRect(rect, 6));

        double cy = y + h / 2;
        context.DrawString(text, new Point(x + padH, cy), Colors.White.Opacity(opacity).ToBrush(), 12, Alignment.LeftCenter);
        if (fHint != null)
            context.DrawString(hint!, new Point(x + padH + fMain.Width + gap, cy), Style.LIGHT_WHITE.Opacity(opacity).ToBrush(), 12, Alignment.LeftCenter);
    }

    // —— 右键复制成功回显 —— //
    const double SynthesisCopyFeedbackSeconds = 1.5;
    readonly Stopwatch mSynthesisCopyClock = new();
    double mSynthesisCopiedSegmentStart = double.NaN;
    DispatcherTimer? mSynthesisCopyTimer;

    // 右键复制成功后调用：在该段 pill 上短暂显示“Copied”，到点自动撤掉。
    void ShowSynthesisCopyFeedback(double segmentStartTime)
    {
        mSynthesisCopiedSegmentStart = segmentStartTime;
        mSynthesisCopyClock.Restart();
        if (mSynthesisCopyTimer == null)
        {
            mSynthesisCopyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SynthesisCopyFeedbackSeconds) };
            mSynthesisCopyTimer.Tick += (_, _) =>
            {
                mSynthesisCopyTimer!.Stop();
                mSynthesisCopiedSegmentStart = double.NaN;
                InvalidateVisual();
            };
        }
        mSynthesisCopyTimer.Stop();
        mSynthesisCopyTimer.Start();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        context.FillRectangle(WhiteKeyColor.ToBrush(), this.Rect());

        IBrush blackKeyBrush = BlackKeyColor.ToBrush();
        int minBlack = (int)Math.Floor(PitchAxis.MinVisiblePitch);
        int maxBlack = (int)Math.Ceiling(PitchAxis.MaxVisiblePitch);
        for (int i = minBlack; i < maxBlack; i++)
        {
            if (MusicTheory.IsBlack(i))
            {
                double top = PitchAxis.Pitch2Y(i + 1);
                double bottom = PitchAxis.Pitch2Y(i);
                context.FillRectangle(blackKeyBrush, new Rect(0, top, Bounds.Width, bottom - top));
            }
            else if (MusicTheory.IsEorB(i))
            {
                double top = PitchAxis.Pitch2Y(i + 1) - 0.5f;
                context.FillRectangle(blackKeyBrush, new Rect(0, top, Bounds.Width, 1));
            }
        }

        if (Part == null)
            return;

        var timeSignatureManager = Part.TimeSignatureManager;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;

        var startMeter = timeSignatureManager.GetMeterStatus(minVisibleTick);
        var endMeter = timeSignatureManager.GetMeterStatus(maxVisibleTick);

        int startIndex = startMeter.TimeSignatureIndex;
        int endIndex = endMeter.TimeSignatureIndex;

        var timeSignatures = timeSignatureManager.TimeSignatures;

        // draw bar（小节线抽稀：像素连续淡出 [12,24]，与相邻量化网格同套；缩小时按拍号段落分段省略，段首恒画。
        // 与时间线标尺各走一套——标尺是线+号一体的离散档淡出，此处是无号网格线，故用 ForEachGridLine）
        IBrush lineBrush = LineColor.ToBrush();
        BarGridLayout.ForEachGridLine(timeSignatureManager, TickAxis, (in BarGridLayout.BarLine line) =>
        {
            double x = TickAxis.Tick2X(line.Tick);
            var brush = line.Opacity >= 1 ? lineBrush : LineColor.Opacity(line.Opacity).ToBrush();
            context.FillRectangle(brush, new Rect(x, 0, 1, Bounds.Height));
        });

        for (int i = startIndex; i <= endIndex; i++)
        {
            int nextTimeSignatureBarIndex = i + 1 == timeSignatures.Count ? (int)Math.Ceiling(endMeter.BarIndex) : timeSignatures[i + 1].BarIndex;
            int thisTimeSignatureBarIndex = Math.Max(timeSignatures[i].BarIndex, (int)Math.Floor(startMeter.BarIndex));

            // draw beat
            double pixelsPerBeat = timeSignatures[i].TicksPerBeat() * TickAxis.PixelsPerTick;
            double beatOpacity = MathUtility.LineValue(6, 0, 12, 1, pixelsPerBeat).Limit(0, 1);
            if (beatOpacity == 0)
                continue;

            IPen beatLinePen = new Pen(LineColor.Opacity(beatOpacity).ToUInt32(), LineWidth, new DashStyle(new double[] { PitchAxis.KeyHeight * 0.4 / LineWidth, PitchAxis.KeyHeight * 0.6 / LineWidth }, 0));
            for (int barIndex = thisTimeSignatureBarIndex; barIndex < nextTimeSignatureBarIndex; barIndex++)
            {
                for (int beatIndex = 1; beatIndex < timeSignatures[i].Numerator; beatIndex++)
                {
                    double xBeatIndex = TickAxis.Tick2X(timeSignatures[i].GetTickByBarAndBeat(barIndex, beatIndex));
                    double x = xBeatIndex + LineWidth / 2;
                    context.DrawLine(beatLinePen, new Point(x, PitchAxis.Pitch2Y(maxBlack - 0.3)), new Point(x, Bounds.Height));
                }
            }

            // draw quantization
            int quantizationBase = (int)Quantization.Base;
            int ticksPerBase = timeSignatures[i].TicksPerBeat() / quantizationBase;
            double pixelsPerBase = ticksPerBase * TickAxis.PixelsPerTick;
            double baseOpacity = MathUtility.LineValue(MIN_GRID_GAP, 0, MIN_REALITY_GRID_GAP, 1, pixelsPerBase).Limit(0, 1);
            if (baseOpacity == 0)
                continue;

            IPen baseLinePen = new Pen(LineColor.Opacity(baseOpacity).ToUInt32(), LineWidth, new DashStyle(new double[] { PitchAxis.KeyHeight * 0.2 / LineWidth, PitchAxis.KeyHeight * 0.8 / LineWidth }, 0));
            for (int barIndex = thisTimeSignatureBarIndex; barIndex < nextTimeSignatureBarIndex; barIndex++)
            {
                for (int beatIndex = 0; beatIndex < timeSignatures[i].Numerator; beatIndex++)
                {
                    double beatPos = timeSignatures[i].GetTickByBarAndBeat(barIndex, beatIndex);
                    for (int baseIndex = 1; baseIndex < quantizationBase; baseIndex++)
                    {
                        double xBase = TickAxis.Tick2X(beatPos + baseIndex * ticksPerBase);
                        double x = xBase + LineWidth / 2;
                        context.DrawLine(baseLinePen, new Point(x, PitchAxis.Pitch2Y(maxBlack - 0.4)), new Point(x, Bounds.Height));
                    }
                }
            }

            int quantizationDivision = (int)Quantization.Division;
            int noteDivision = Math.Max(quantizationDivision * 4, timeSignatures[i].Denominator);
            int beatDivision = noteDivision / timeSignatures[i].Denominator;
            double thisTimeSignaturePos = timeSignatures[i].GetTickByBarIndex(thisTimeSignatureBarIndex);
            for (int cellsPerBase = 2; cellsPerBase <= beatDivision; cellsPerBase *= 2)
            {
                int ticksPerCell = ticksPerBase / cellsPerBase;
                double pixelsPerCell = ticksPerCell * TickAxis.PixelsPerTick;
                double cellOpacity = MathUtility.LineValue(MIN_GRID_GAP, 0, MIN_REALITY_GRID_GAP, 1, pixelsPerCell).Limit(0, 1);
                if (cellOpacity == 0)
                    break;

                IPen cellLinePen = new Pen(LineColor.Opacity(cellOpacity).ToUInt32(), LineWidth, new DashStyle(new double[] { PitchAxis.KeyHeight * 0.2 / LineWidth, PitchAxis.KeyHeight * 0.8 / LineWidth }, 0));
                int cellCount = (nextTimeSignatureBarIndex - thisTimeSignatureBarIndex) * timeSignatures[i].Numerator * quantizationBase * cellsPerBase / 2;
                for (int cellIndex = 0; cellIndex < cellCount; cellIndex++)
                {
                    double cellPos = thisTimeSignaturePos + (cellIndex * 2 + 1) * ticksPerCell;
                    double xCell = TickAxis.Tick2X(cellPos);
                    double x = xCell + LineWidth / 2;
                    context.DrawLine(cellLinePen, new Point(x, PitchAxis.Pitch2Y(maxBlack - 0.4)), new Point(x, Bounds.Height));
                }
            }
        }
        
        // draw decorative image（立绘优先）：当前声库有立绘则画立绘，否则画全局背景图——二者互斥、共用 ImagePlayer（静态 / 动图）。
        // 二者同样靠右贴住、按高度填满钢琴窗等比缩放，并同套 BackgroundImageOpacity 不透明度；画在网格之后、音符之前（音符盖其上仍清晰）。
        var decoFrame = mPortrait?.CurrentFrame ?? mBackground?.CurrentFrame;
        if (decoFrame != null && decoFrame.Size.Height > 0)
        {
            var imageSize = decoFrame.Size;
            imageSize *= Bounds.Height / imageSize.Height;
            using var _ = context.PushOpacity(Settings.BackgroundImageOpacity);
            context.DrawImage(decoFrame, new Rect(Bounds.Width - imageSize.Width, 0, imageSize.Width, imageSize.Height));
        }

        // draw refer note
        if (Part.Track != null && Part.Track.Project != null)
            foreach (var track in Part.Track.Project.Tracks)
            {
                if (track == Part.Track) continue;
                if (!track.AsRefer.Value) continue;
                IBrush referBrush = track.GetColor().Opacity(0.5).ToBrush();
                foreach (var part in track.Parts)
                {
                    if (part.EndPos() < minVisibleTick) continue;
                    if (part.StartPos() > maxVisibleTick) continue;
                    if (!(part is MidiPart midiPart)) continue;
                    foreach(var note in midiPart.Notes)
                    {
                        if (note.GlobalEndPos() < minVisibleTick)
                            continue;

                        if (note.GlobalStartPos() > maxVisibleTick)
                            break;

                        var rect = this.ReferNoteRect(note);
                        context.FillRectangle(referBrush, rect);
                    }
                }
            }
        
        // draw note
        // 圆角随缩放降级：ScaleLevel≥-4 满圆角(4)，缩到 ≤-8 收敛为纯直角——缩小时大量窄 note
        // 收成齐整方块、读作干净的旋律轮廓，而非糊成一团的圆角药丸。旧常用缩放区(≥-4)观感不变。
        double round = 4 * MathUtility.LineValue(-8, 0, -4, 1, TickAxis.ScaleLevel).Limit(0, 1);
        IBrush noteBrush = Style.ITEM.ToBrush();
        IBrush selectedNoteBrush = Style.HIGH_LIGHT.ToBrush();
        IBrush lyricBrush = Colors.White.Opacity(0.7).ToBrush();
        IBrush pronunciationBrush = Style.LIGHT_WHITE.ToBrush();
        IBrush overlapCoverBrush = Colors.Black.Opacity(0.45).ToBrush();
        foreach (var note in Part.Notes)
        {
            if (note.GlobalEndPos() < minVisibleTick)
                continue;

            if (note.GlobalStartPos() > maxVisibleTick)
                break;

            var rect = this.NoteRect(note);
            //context.FillRectangle(getPartColor(Part.Track,note.IsSelected).ToBrush(), rect, (float)round);
            context.FillRectangle(note.IsSelected ? selectedNoteBrush : noteBrush, rect, (float)round);

            // 去重叠暗色盖住：被「后盖前」砍掉的尾段 [有效结束, 画出末] 画暗，亮色才是真正发声段——用户即知此段被重叠覆盖。
            // 仅 voice（单声部去重叠口径）才画；instrument 保留重叠多声部，重叠段真发声、不该提示被盖。
            if (Part.SoundSource.Kind == SourceKind.Voice)
            {
                double coverLeftX = TickAxis.Tick2X(note.GlobalEffectiveEndPos());
                if (coverLeftX < rect.Right - 0.5)
                {
                    double left = Math.Max(coverLeftX, rect.Left);
                    context.DrawRectangle(overlapCoverBrush, null, new RoundedRect(new Rect(left, rect.Top, rect.Right - left, rect.Height), 0, round, round, 0));
                }
            }

            rect = rect.Adjusted(8, -28, -8, 0);
            if (rect.Width <= 0)
                continue;

            var clip = context.PushClip(rect);
            if (Part.SoundSource.Kind == SourceKind.Voice)
            {
                // voice：显示歌词 + 最终发音（音素来源）。
                context.DrawString(note.Lyric.Value, rect, lyricBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter, new(0, 14));
                var pronunciation = note.FinalPronunciation();
                if (!string.IsNullOrEmpty(pronunciation))
                {
                    context.DrawString(pronunciation, rect, pronunciationBrush, 12, Alignment.LeftTop, Alignment.LeftCenter, new(0, 14));
                }
            }
            else
            {
                // instrument：无歌词，显示音名（如 "A4"）。
                context.DrawString(MusicTheory.PitchName(note.Pitch.Value), rect, lyricBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter, new(0, 14));
            }
            clip.Dispose();
        }

        // draw pitch
        double pitchOpacity = MathUtility.LineValue(-6.7, 0, -4.3, 1, TickAxis.ScaleLevel).Limit(0, 1);

        // pitch/lock/anchor 工具下的暗遮罩“恒在”（强调可编辑音高），与缩放/pitchOpacity 无关。
        bool pitchEditTool = mDependency.PianoTool.Value is PianoTool.Pitch or PianoTool.Lock or PianoTool.Anchor;

        if (pitchOpacity == 0)
        {
            // 缩放太小：省略 pitch 曲线绘制，但遮罩仍要画（原先被这处跳转连遮罩一起省略了——bug）。
            if (pitchEditTool)
                context.FillRectangle(Colors.Black.Opacity(0.25).ToBrush(), this.Rect());
            goto FinishDrawPitch;
        }

        Color pitchColor = Colors.White.Opacity(pitchOpacity * (mDependency.PianoTool.Value == PianoTool.Note ? 0.3 : 1));

        DrawSynthesizedPitch(context, pitchColor);

        if (pitchEditTool)
            context.FillRectangle(Colors.Black.Opacity(0.25).ToBrush(), this.Rect());

        DrawVibratos(context);

        if (mDependency.PianoTool.Value is PianoTool.Note or PianoTool.Pitch or PianoTool.Lock or PianoTool.Vibrato)
        {
            // 颤音覆盖区、未绘制 pitch 处：在音符基线上叠加偏差画虚线波——颤音落笔即现波、不依赖合成完成；
            // 切到 note 工具也能在"画了颤音的位置"看到预期音高。
            DrawPitch(context, 0, Bounds.Width, Part.GetVibratoFallbackPitch, pitchColor.Opacity(0.7), 1, VibratoPreviewDashStyle);
        }
        DrawPitch(context, 0, Bounds.Width, Part.GetFinalPitch, pitchColor, mDependency.PianoTool.Value == PianoTool.Note ? 1 : 2);
    FinishDrawPitch:

        // draw select
        if (mNoteSelectOperation.IsOperating)
        {
            var rect = mNoteSelectOperation.SelectionRect();
            context.DrawRectangle(SelectionColor.Opacity(0.25).ToBrush(), new Pen(SelectionColor.ToUInt32()), rect);
        }

        if (mVibratoSelectOperation.IsOperating)
        {
            var rect = mVibratoSelectOperation.SelectionRect();
            context.DrawRectangle(SelectionColor.Opacity(0.25).ToBrush(), new Pen(SelectionColor.ToUInt32()), rect);
        }

        if (mAnchorSelectOperation.IsOperating)
        {
            var rect = mAnchorSelectOperation.SelectionRect();
            context.DrawRectangle(SelectionColor.Opacity(0.25).ToBrush(), new Pen(SelectionColor.ToUInt32()), rect);
        }

        double start = TickAxis.Tick2X(Part.StartPos());
        if (start > 0)
        {
            context.FillRectangle(Colors.Black.Opacity(0.3).ToBrush(), this.Rect().Adjusted(0, 0, start - Bounds.Width, 0));
        }

        double end = TickAxis.Tick2X(Part.EndPos());
        if (end < Bounds.Width)
        {
            context.FillRectangle(Colors.Black.Opacity(0.3).ToBrush(), this.Rect().Adjusted(end, 0, 0, 0));
        }

        // draw region selection（DAW 式范围选区：白罩 + 左右两条纯白虚线竖边）。
        // 白是 hue-neutral；虚线 + 白色刻意区别于"框选对象"的 HIGH_LIGHT 实色框。只画到参数区顶（WaveformBottom=参数层上沿），
        // 参数区那段由 AutomationRenderer 自己画——避免两层在参数区叠画（半透明叠加发暗）。不画上下横边 → 跨区拼成一条连续竖带、无横切。
        if (mSelection.IsAcitve)
        {
            double left = TickAxis.Tick2X(mSelection.Start);
            double right = TickAxis.Tick2X(mSelection.End);
            double bottom = Math.Max(0, WaveformBottom);
            context.FillRectangle(RegionSelectionColor.Opacity(0.12).ToBrush(), new Rect(left, -2, right - left, bottom + 2));
            var regionPen = new Pen(RegionSelectionColor.ToUInt32(), 1) { DashStyle = DashStyle.Dash };
            context.DrawLine(regionPen, new Point(left, -2), new Point(left, bottom));
            context.DrawLine(regionPen, new Point(right, -2), new Point(right, bottom));
        }

        DrawWaveform(context);

        // 合成状态条 + 顶部阴影（放在 OnRender 末尾 → 最上层，盖过 pitch/选区/波形等）：
        // 状态时间线贴音符区顶沿一条细带（灰=待合成 橙=合成中 绿=已合成 红=失败）。合成中段流光需逐帧重绘——
        // 按是否存在“合成中”段起停定时器；文案不常驻，hover 才弹 pill；右键复制（见 TrySynthesisStripCopy）。
        var synthesisStatus = Part.GetSynthesisStatus();
        bool anySynthesizing = false;
        for (int i = 0; i < synthesisStatus.Count; i++)
        {
            if (synthesisStatus[i].Status == SynthesisSegmentStatus.Synthesizing)
            {
                anySynthesizing = true;
                break;
            }
        }

        double shimmerPhase = -1;
        if (anySynthesizing)
        {
            EnsureSynthesisShimmer();
            shimmerPhase = (mSynthesisShimmerClock.Elapsed.TotalSeconds / SynthesisShimmerPeriod) % 1.0;
        }
        else
        {
            StopSynthesisShimmer();
        }

        // 顶部渐变阴影（常驻，与有无状态条无关）：让上方标尺(TimelineView)与下方音符内容拉开层次；状态条（若有）再浮其上。
        DrawTopShadow(context);

        if (synthesisStatus.Count > 0)
        {
            var tempoManager = Part.TempoManager;
            // 状态条恒显（像 note 一样不随缩放隐去）；稳定性靠 SynthesisStatusStrip 内部像素对齐保证。
            SynthesisStatusStrip.Draw(context, synthesisStatus, tempoManager, TickAxis, SynthesisStripTop, SynthesisStripHeight, SynthesisStripRadius, shimmerPhase);
            DrawSynthesisStatusOverlay(context, synthesisStatus, tempoManager);
        }
    }

    void DrawSynthesizedPitch(DrawingContext context, Color pitchColor)
    {
        if (Part == null)
            return;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;

        var tempoManager = Part.TempoManager;

        {
            foreach (var pitch in Part.SynthesizedPitch)
            {
                if (pitch.IsEmpty())
                    continue;

                double startTime = pitch[0].X;
                double endTime = pitch[pitch.Count - 1].X;
                double startTick = tempoManager.GetTick(startTime);
                double endTick = tempoManager.GetTick(endTime);
                if (endTick < minVisibleTick)
                    continue;

                if (startTick > maxVisibleTick)
                    break;

                int startX = (int)Math.Floor(TickAxis.Tick2X(Math.Max(startTick, minVisibleTick)));
                int endX = (int)Math.Ceiling(TickAxis.Tick2X(Math.Min(endTick, maxVisibleTick)));
                int n = endX - startX + 1;
                double[] times = new double[n];
                for (int i = 0; i < n; i++)
                {
                    times[i] = tempoManager.GetTime(TickAxis.X2Tick(i + startX));
                }

                var ys = pitch.LinearInterpolation(times);

                var points = new System.Collections.Generic.LinkedList<Point>();
                for (int i = 0; i < n; i++)
                {
                    points.AddLast(new Point(i + startX, PitchAxis.Pitch2Y(ys[i] + 0.5)));
                }

                context.DrawCurve(points, pitchColor, 1);
            }
        }
    }

    // 颤音"立即展示"兜底波/悬浮预览波的虚线样式（未绘制 pitch 时区别于实线最终曲线）。
    static readonly DashStyle VibratoPreviewDashStyle = new(new double[] { 4, 4 }, 0);

    void DrawPitch(DrawingContext context, double left, double right, Func<IReadOnlyList<double>, double[]> getPitch, Color pitchColor, double thickness, DashStyle? dashStyle = null)
    {
        if (Part == null)
            return;

        double pos = Part.Pos.Value;
        double[] ticks = new double[(int)(right - left) + 1];
        for (int i = 0; i < ticks.Length; i++)
        {
            ticks[i] = TickAxis.X2Tick(left + i) - pos;
        }
        var pitchValues = getPitch(ticks);
        List<List<Point>> pitchLines = new();
        List<Point> pitchLine = new();
        for (int i = 0; i < ticks.Length; i++)
        {
            var pitchValue = pitchValues[i];
            if (double.IsNaN(pitchValue))
            {
                if (pitchLine.Count == 0)
                    continue;

                pitchLines.Add(pitchLine);
                pitchLine = new();
                continue;
            }

            pitchLine.Add(new Point(left + i, PitchAxis.Pitch2Y(pitchValue + 0.5)));
        }
        if (pitchLine.Count != 0)
            pitchLines.Add(pitchLine);

        var start = TickAxis.X2Tick(left) - pos;
        var end = TickAxis.X2Tick(right) - pos;
        foreach (var pitchPoints in pitchLines)
        {
            context.DrawCurve(pitchPoints, pitchColor, thickness, false, dashStyle);
        }
    }

    // 颤音工具悬浮添加预览：鼠标悬浮在某音符本体的无颤音位置上时，返回待建颤音参数
    // （Pos = 鼠标量化 tick、Dur = 到下一个颤音起点或音符结尾中较早者）；否则 null。落实与预览渲染共用。
    VibratoInfo? GetVibratoAddPreview(Point position)
    {
        if (Part == null)
            return null;

        // 命中检测按去重叠口径（后盖前）：被下一音符盖掉的尾段不发声，不算悬浮命中，颤音也不该铺进去。
        double mouseTick = TickAxis.X2Tick(position.X) - Part.Pos.Value;
        INote? hovered = null;
        foreach (var note in Part.Notes)
        {
            if (this.NoteRect(note).Contains(position) && mouseTick < note.EffectiveEndPos())
            {
                hovered = note;
                break;
            }
        }
        if (hovered == null)
            return null;

        // 预览区间 = 鼠标所在的空白段：左界抬到左侧最近颤音右缘（防起点量化取整落回其内部），
        // 右界截到右侧最近颤音起点与音符有效末（去重叠后）中较早者。鼠标点落在已有颤音内则无预览。
        double end = hovered.EffectiveEndPos();
        double lo = hovered.StartPos();
        foreach (var vibrato in Part.Vibratos)
        {
            if (vibrato.StartPos() <= mouseTick && mouseTick < vibrato.EndPos())
                return null;

            if (vibrato.EndPos() <= mouseTick)
                lo = Math.Max(lo, vibrato.EndPos());
            else
                end = Math.Min(end, vibrato.StartPos());
        }
        if (end <= lo)
            return null;

        double hi = end - QuantizedCellTicks();
        double pos = GetQuantizedTick(TickAxis.X2Tick(position.X)) - Part.Pos.Value;
        pos = hi <= lo ? lo : Math.Clamp(pos, lo, hi);
        double dur = end - pos;
        if (dur <= 0)
            return null;

        return new VibratoInfo() { Pos = pos, Dur = dur, Amplitude = 0.5, Frequency = 6, Phase = 0, Attack = 0.2, Release = 0.2 };
    }

    void DrawVibratos(DrawingContext context)
    {
        if (mDependency.PianoTool.Value != PianoTool.Vibrato)
            return;

        if (Part == null)
            return;

        double minVisibleTick = TickAxis.MinVisibleTick;
        double maxVisibleTick = TickAxis.MaxVisibleTick;

        IBrush vibratoBrush = Colors.Black.Opacity(0.25).ToBrush();
        IPen vibratoSelectedPen = new Pen(Colors.White.ToUInt32(), 1);

        foreach (var vibrato in Part.Vibratos)
        {
            if (vibrato.GlobalEndPos() < minVisibleTick)
                continue;

            if (vibrato.GlobalStartPos() > maxVisibleTick)
                break;

            double x = TickAxis.Tick2X(vibrato.GlobalStartPos());
            double width = TickAxis.PixelsPerTick * vibrato.Dur;
            context.DrawRectangle(vibratoBrush, vibrato.IsSelected ? vibratoSelectedPen : null, new Rect(x, 0, width, Bounds.Height));
        }
        IBrush frequencyBrush = Colors.White.ToBrush();
        IBrush phaseBrush = Colors.White.ToBrush();
        IPen frequencyPen = new Pen(frequencyBrush, 1);
        IPen phasePen = new Pen(phaseBrush, 1);
        IBrush arBrush = Colors.White.ToBrush();
        IPen arPen = new Pen(arBrush, 1);
        IBrush textBrush = Brushes.White;
        var raycastItem = ItemAt(MousePosition);
        IVibratoItem? hoverVibratoItem = mOperatingVibratoItem;
        if (hoverVibratoItem == null && raycastItem is IVibratoItem vibratoItem) hoverVibratoItem = vibratoItem;
        if (hoverVibratoItem != null)
        {
            var hoverVibrato = hoverVibratoItem.Vibrato;

            var frequencyPosition = hoverVibratoItem.FrequencyPosition();
            if (!double.IsNaN(frequencyPosition.Y))
            {
                context.DrawEllipse(hoverVibratoItem is VibratoFrequencyItem || mVibratoFrequencyOperation.IsOperating ? frequencyBrush : null, frequencyPen, frequencyPosition, 6, 6);
                context.DrawString("Frequency".Tr(this) + ": " + hoverVibrato.Frequency.Value.ToString("F2"), frequencyPosition - new Point(0, 18), textBrush, 12, Alignment.Center, new Typeface(Assets.SegoeUI));
            }

            var phasePosition = hoverVibratoItem.PhasePosition();
            if (!double.IsNaN(phasePosition.Y))
            {
                context.DrawEllipse(hoverVibratoItem is VibratoPhaseItem || mVibratoPhaseOperation.IsOperating ? phaseBrush : null, phasePen, phasePosition, 6, 6);
                context.DrawString("Phase".Tr(this) + ": " + hoverVibrato.Phase.Value.ToString(" +0.00;-0.00"), phasePosition + new Point(0, 18), textBrush, 12, Alignment.Center, new Typeface(Assets.SegoeUI));
            }

            var attackPosition = hoverVibratoItem.AttackPosition();
            if (!double.IsNaN(attackPosition.Y))
            {
                context.DrawGeometry(arBrush, null, new PolylineGeometry([ 
                    attackPosition + new Point(-4, 0), 
                    attackPosition + new Point(0, -12), 
                    attackPosition + new Point(0, 12), 
                ], true));
            }

            var releasePosition = hoverVibratoItem.ReleasePosition();
            if (!double.IsNaN(releasePosition.Y))
            {
                context.DrawGeometry(arBrush, null, new PolylineGeometry([
                    releasePosition + new Point(0, -12),
                    releasePosition + new Point(4, 0),
                    releasePosition + new Point(0, 12),
                ], true));
            }
        }

        // 悬浮添加预览：未操作、未命中已有颤音时，在音符上画预览框 + 虚线预览波（点击即落实）。
        if (mState == State.None && mOperatingVibratoItem == null && raycastItem is not IVibratoItem)
        {
            var preview = GetVibratoAddPreview(MousePosition);
            if (preview != null)
            {
                var info = preview;
                double px = TickAxis.Tick2X(Part.Pos.Value + info.Pos);
                double pw = TickAxis.PixelsPerTick * info.Dur;
                // 用颤音本身的黑半透明打底（与已落实颤音同款）+ 白虚线框（"预览/待添加"提示）——区别于范围选区的白罩白虚线，避免撞样式。
                context.DrawRectangle(Colors.Black.Opacity(0.25).ToBrush(), new Pen(Colors.White.Opacity(0.5).ToUInt32(), 1, VibratoPreviewDashStyle), new Rect(px, 0, pw, Bounds.Height));
                DrawPitch(context, px, px + pw, ticks => Part.GetVibratoAddPreviewPitch(ticks, info), Colors.White.Opacity(0.7), 1, VibratoPreviewDashStyle);
            }
        }
    }

    void DrawWaveform(DrawingContext context)
    {
        if (Part == null)
            return;

        if (!mDependency.IsWaveformVisible)
            return;

        double height = WAVEFORM_HEIGHT;
        context.FillRectangle(Colors.Black.Opacity(0.5).ToBrush(), new(0, WaveformTop, Bounds.Width, WAVEFORM_HEIGHT));
        var tempoManager = Part.TempoManager;
        var viewStartTime = tempoManager.GetTime(TickAxis.X2Tick(0));
        var viewEndTime = tempoManager.GetTime(TickAxis.X2Tick(Bounds.Width));

        // 各已完成音频段：逐段与可视区间求交、各自绘制波形（段间空洞留白、不画静音线）。
        foreach (var segment in Part.SynthesizedSegments)
            DrawAudioWaveform(segment.Audio, segment.Waveform);
        void DrawAudioWaveform(MonoAudio audio, Waveform waveform)
        {
            if (audio.Samples is null)
                return;

            double startTime = audio.StartTime;
            double endTime = audio.StartTime + (audio.Samples.Length == 0 ? 0 : (double)audio.Samples.Length / audio.SampleRate);
            if (startTime > viewEndTime || endTime < viewStartTime)
                return;

            double minTime = Math.Max(viewStartTime, startTime);
            double maxTime = Math.Min(viewEndTime, endTime);
            double minX = TickAxis.Tick2X(tempoManager.GetTick(minTime));
            double maxX = TickAxis.Tick2X(tempoManager.GetTick(maxTime));
            var xs = new List<double>();
            var positions = new List<double>();
            double gap = 1;
            double xp = minX - gap;
            do
            {
                xp += gap;
                xs.Add(xp);
                double time = tempoManager.GetTime(TickAxis.X2Tick(xp));
                positions.Add((time - audio.StartTime) * audio.SampleRate);
            }
            while (xp < maxX);

            if (positions.Count < 2)
                return;

            float level = (float)MusicTheory.dB2Level(Part.Gain.Value);
            float r = (float)height / 2;
            float top = (float)WaveformTop;
            float toY(float value) => (1 - value * level) * r + top;

            var values = waveform.GetValues(positions);
            var peaks = waveform.GetPeaks(positions, values);

            double pos = Part.Pos.Value;
            var ticks = new double[xs.Count];
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] = TickAxis.X2Tick(xs[i]) - pos;
            }
            var volumes = Part.GetFinalAutomationValues(ticks, ConstantDefine.VolumeID);
            for (int i = 0; i < volumes.Length; i++)
            {
                volumes[i] = MidiPart.Volume2Level(volumes[i]);
            }
            for (int i = 0; i < values.Length; i++)
            {
                values[i] *= (float)volumes[i];
            }
            for (int i = 0; i < peaks.Length; i++)
            {
                peaks[i].min *= (float)volumes[i];
                peaks[i].max *= (float)volumes[i];
            }

            for (int i = 0; i < xs.Count; i++)
            {
                values[i] = toY(values[i]);
            }
            for (int i = 0; i < peaks.Length; i++)
            {
                peaks[i].min = toY(peaks[i].min);
                peaks[i].max = toY(peaks[i].max);
            }
            using var _ = context.PushOpacity(0.5);
            var points = new List<Avalonia.Point>();
            for (int i = 0; i < peaks.Length; i++)
            {
                double x = xs[i];
                var peak = peaks[i];
                points.Add(new(x, values[i]));
                points.Add(new(x + gap * peak.minRatio, peak.min));
            }
            for (int i = peaks.Length; i > 0; i--)
            {
                double x = xs[i];
                var peak = peaks[i - 1];
                points.Add(new(x, values[i]));
                points.Add(new(x + gap * peak.maxRatio, peak.max));
            }
            context.DrawCurve(points, Style.LIGHT_WHITE, gap, true);
        }

        double opacity = MathUtility.LineValue(-4.7, 0, -2.3, 1, TickAxis.ScaleLevel).Limit(0, 1);
        if (opacity <= 0)
            return;

        // 必须 using 限定作用域：原先裸 push 不还原，会把这层 opacity 泄漏给之后绘制的内容（如顶部状态条）。
        using var _ = context.PushOpacity(opacity);

        // 波形带上下分层（仅 voice）：上半区 = note 边界操作、下半区 = 音素操作。音素刻度/文字贴底边，
        // note 边界杆贴顶边。非 voice（instrument 等）无音素、不分层、不画 note 杆。
        bool layered = Part.SoundSource.Kind == SourceKind.Voice;
        var hoverItem = mOperatingWaveformItem ?? HoverItem();

        // 箱庭结构线：中线横贯整带作上下两室的公共壁——note 边界杆顶满上室、音素刻度线顶满下室，与之相交
        // 成格；顶边再画一条封住上室（底边不画：紧贴参数区标题栏，已有现成分界）。随音素 UI 同一 opacity 域淡出。
        if (layered)
        {
            var penFrame = new Pen(Style.LIGHT_WHITE.Opacity(0.25).ToBrush(), 1);
            context.DrawLine(penFrame, new(0, WaveformTop), new(Bounds.Width, WaveformTop));
            context.DrawLine(penFrame, new(0, WaveformCenterY), new(Bounds.Width, WaveformCenterY));
        }

        // 悬浮区块提亮（只亮所在区块、不整半带亮）：上半空白 = 鼠标所在 note 的区间、下半空白 = 所在显示音素
        // 的区段。悬在线上不亮区块——线自身换主题色（见 DrawBoundary / DrawNoteHandle）。
        if (layered && hoverItem is WaveformBackItem)
        {
            bool upper = MousePosition.Y < WaveformCenterY;
            double blockLeft = double.NaN, blockRight = double.NaN;
            if (upper)
            {
                double tick = TickAxis.X2Tick(MousePosition.X) - Part.Pos.Value;
                foreach (var note in Part.Notes)
                {
                    if (note.StartPos() > tick)
                        break;

                    if (note.EndPos() > tick)
                    {
                        blockLeft = TickAxis.Tick2X(note.GlobalStartPos());
                        blockRight = TickAxis.Tick2X(note.GlobalEndPos());
                        break;
                    }
                }
            }
            else
            {
                double time = tempoManager.GetTime(TickAxis.X2Tick(MousePosition.X));
                foreach (var note in Part.Notes)
                {
                    var display = note.DisplayPhonemes;
                    if (display.IsEmpty() || time < display.ConstFirst().StartTime || time >= display.ConstLast().EndTime)
                        continue;

                    foreach (var p in display)
                    {
                        if (p.StartTime <= time && time < p.EndTime)
                        {
                            blockLeft = TickAxis.Tick2X(tempoManager.GetTick(p.StartTime));
                            blockRight = TickAxis.Tick2X(tempoManager.GetTick(p.EndTime));
                            break;
                        }
                    }
                    break;
                }
            }
            if (!double.IsNaN(blockLeft))
            {
                double top = upper ? WaveformTop : WaveformCenterY;
                context.FillRectangle(Colors.White.Opacity(0.12).ToBrush(), new(blockLeft, top, blockRight - blockLeft, WAVEFORM_HEIGHT / 2));
            }
        }

        // 箱庭式分界线：各顶满自己半室——note 边界杆 = 上室全高、音素刻度线 = 下室全高，与中线基准线
        // 相交成格，每个音素 / 每段 note 区间是一个完整的「格子」，文字居中于格。
        double yPhonemeBottom = WaveformBottom;
        double yPhonemeTop = WaveformCenterY;
        double yNoteTop = WaveformTop;
        double yNoteBottom = WaveformCenterY;
        // 音素文字一律纯白，钉死 = 粗体、合成 = 常规——固定态只由字重表达，线不参与
        // （定稿版：粗线/降灰/内描边/主题色字/提亮紫都试过，或扎眼或隐晦或泛白，纯白加粗最稳）。
        IBrush brush = Style.WHITE.ToBrush();
        Typeface pinnedTypeface = new(AppFont.Current, FontStyle.Normal, FontWeight.Bold);
        // 音素刻度统一细线。
        IPen penPhoneme = new Pen(Style.LIGHT_WHITE.Opacity(0.5).ToBrush(), 1);
        // note 边界统一笔画（上半区的杆 + 下半区的核起点/末线是同一条边界的上下两段，样式一致）。
        // 降灰细线——顶满半室后线很长，纯白太扎眼；灰一档正好让格壁退为结构、波形和文字留在前景。
        IPen penNoteBoundary = new Pen(Style.LIGHT_WHITE.Opacity(0.7).ToBrush(), 1);
        // hover 提亮 = 纯白加粗（字色已让出纯白通道给悬浮态，不再与钉死标识撞车）。
        IPen penHover = new Pen(Style.WHITE.ToBrush(), 2);
        var hoverPhoneme = hoverItem as WaveformPhonemeResizeItem;
        var hoverNoteStart = hoverItem as WaveformNoteStartResizeItem;
        var hoverNoteEnd = hoverItem as WaveformNoteEndResizeItem;
        void DrawNoteHandle(double x, bool hovered)
        {
            context.DrawLine(hovered ? penHover : penNoteBoundary, new(x, yNoteTop), new(x, yNoteBottom));
        }

        foreach (var note in Part.Notes)
        {
            // 显示音素：固定 / 合成统一口径（已跨 note 去重叠，见 INote.DisplayPhonemes）。
            // 两者都没有 → 什么都不画（合成前 / 乘客被铺过 / 空 note 一律留白，也就没有可拖的边界）。
            var phonemes = note.DisplayPhonemes;
            if (phonemes.IsEmpty())
                continue;

            bool isPinned = !note.Phonemes.IsEmpty();

            var startTime = phonemes.ConstFirst().StartTime;
            var endTime = phonemes.ConstLast().EndTime;
            if (endTime < viewStartTime)
                continue;

            if (startTime > viewEndTime)
                break;

            // 画每个音素的开头线；末线（末音素的结尾）只交给「真正会画开头线的接管者」——沿相接链向后跳过
            // 无显示音素的乘客（melisma 被铺过的延音符），落到第一个有显示音素的相接 note：有 → 该边界由它的
            // 开头线接管、本 note 不画（再画会在相接处叠线）；无（链到头 / 空隙断链 / 邻居无内容）→ 自己画，
            // 否则 melisma 的有效末（= 链末乘客的尾）会悬空无线。endOwner = 链末 note = 上半区尾杆的属主，
            // 供末线 hover 与尾杆联动。
            var endOwner = note;
            var takeover = note.Next;
            while (takeover != null && takeover.StartPos() <= endOwner.EndPos() + 1e-6 && takeover.DisplayPhonemes.IsEmpty())
            {
                endOwner = takeover;
                takeover = takeover.Next;
            }
            bool drawNoteEnd = takeover == null || takeover.StartPos() > endOwner.EndPos() + 1e-6 || takeover.DisplayPhonemes.IsEmpty();
            // 悬停的音素边界号（左线 = i、右线 = i+1）换主题色提亮，指明"点下去拖的是哪条"。
            int hoverBoundary = hoverPhoneme != null && hoverPhoneme.Note == note ? hoverPhoneme.PhonemeIndex : -1;
            // 末边界（= note 有效末）是 note 边界的下半段（与上半 note 杆同笔画、hover 联动）；音素起边界是独立音素刻度。
            void DrawBoundary(int k, double x)
            {
                bool isNoteEnd = k == phonemes.Count;
                bool hovered = k == hoverBoundary || (isNoteEnd && hoverNoteEnd?.Note == endOwner);
                IPen linePen = hovered ? penHover : isNoteEnd ? penNoteBoundary : penPhoneme;
                context.DrawLine(linePen, new(x, yPhonemeTop), new(x, yPhonemeBottom));
            }
            // note 头线**只在上半 note 泳道**画（见 DrawNoteHandle），不伸进音素泳道——上下完全解耦。音素泳道只画音素边界；
            // note 尾线例外（末音素结尾恒 = note 有效末，故 k==count 那条以 note 笔画画在音素泳道里，仍是真实音素边界）。
            double right = double.NaN;
            for (int i = 0; i < phonemes.Count; i++)
            {
                var phoneme = phonemes[i];
                double left = TickAxis.Tick2X(tempoManager.GetTick(phoneme.StartTime));
                if (left != right)
                {
                    DrawBoundary(i, left);
                }
                right = TickAxis.Tick2X(tempoManager.GetTick(phoneme.EndTime));
                if (i < phonemes.Count - 1 || drawNoteEnd)
                    DrawBoundary(i + 1, right);
                // 音素文字居中于格；钉死 = 粗体、合成 = 常规。
                context.DrawString(phoneme.Symbol, new((left + right) / 2, (yPhonemeTop + yPhonemeBottom) / 2), brush, 12, Alignment.Center, isPinned ? pinnedTypeface : null);
            }
        }

        // note 边界杆（上半区，仅 voice 分层，与 UpdateItems 的热区同条件）：对**所有** note——无音素 note、
        // 延音符也画，边界操作不依赖音素。头杆恒有；尾杆仅与下个 note 不相接时（相接边界归下个 note 的头杆）。
        if (layered)
        {
            foreach (var note in Part.Notes)
            {
                if (note.EndTime < viewStartTime)
                    continue;

                if (note.StartTime > viewEndTime)
                    break;

                DrawNoteHandle(TickAxis.Tick2X(note.GlobalStartPos()), hoverNoteStart?.Note == note);
                if (note.Next == null || note.Next.StartPos() > note.EndPos() + 1e-6)
                    DrawNoteHandle(TickAxis.Tick2X(note.GlobalEndPos()), hoverNoteEnd?.Note == note);
            }

            // 切刀预览：悬在上半区空白且落点有 note 可切 → 鼠标处画虚线，提示单击即在此切分（落点与单击一致）。
            if (IsHover && hoverItem is WaveformBackItem && TrySplitNotePreview(MousePosition, out double splitX))
            {
                var splitPen = new Pen(Style.WHITE.Opacity(0.6).ToBrush(), 1) { DashStyle = DashStyle.Dash };
                context.DrawLine(splitPen, new(splitX, yNoteTop), new(splitX, WaveformCenterY));
            }
        }
    }

    void LoadBackgroundImage()
    {
        mBackground?.Dispose();
        string path = Settings.BackgroundImagePath.Value;
        mBackground = File.Exists(path) ? ImagePlayer.Load(path) : null;
        if (mBackground != null)
            mBackground.FrameChanged += InvalidateVisual;

        UpdateImagePlayback();
        InvalidateVisual();
    }

    // 解析当前 part 音源声库的立绘并缓存（按路径去重，避免每次重绘重载）；无立绘 / 文件不存在则清空。
    void LoadPortrait()
    {
        string? path = ResolvePortraitPath();
        if (path == mPortraitPath)
            return;

        mPortraitPath = path;
        mPortrait?.Dispose();
        mPortrait = path != null ? ImagePlayer.Load(path) : null;
        if (mPortrait != null)
            mPortrait.FrameChanged += InvalidateVisual;

        UpdateImagePlayback();
        InvalidateVisual();
    }

    // 立绘优先：只让「实际要画的那张图」（有立绘则立绘、否则背景图）的动图定时器在跑——被盖掉的那张停表省 CPU；
    // 控件未挂上视觉树（不可见）时两者都停。静态图无定时器，Start/Stop 皆空操作。
    void UpdateImagePlayback()
    {
        bool attached = this.GetVisualRoot() != null;
        var active = mPortrait ?? mBackground;
        if (mPortrait != null)
            mBackground?.Stop();   // 背景图被立绘盖掉

        if (attached)
            active?.Start();
        else
        {
            mPortrait?.Stop();
            mBackground?.Stop();
        }
    }

    // 仅支持路径变体的立绘（FileImageResource）；其他变体宿主暂不解码，走兜底（无立绘）。
    string? ResolvePortraitPath()
    {
        var part = Part;
        if (part == null)
            return null;

        var source = part.SoundSource;
        ImageResource? portrait = source.Kind == SourceKind.Voice
            ? (VoicesManager.TryGetVoiceInfo(source.Type, source.ID, out var voiceInfo) ? voiceInfo.Portrait : null)
            : (InstrumentsManager.TryGetInstrumentInfo(source.Type, source.ID, out var instrumentInfo) ? instrumentInfo.Portrait : null);

        return portrait is FileImageResource fileImage && File.Exists(fileImage.Path) ? fileImage.Path : null;
    }

    double QuantizedCellTicks()
    {
        int quantizationBase = (int)Quantization.Base;
        double division = (int)Math.Pow(2, Math.Log2(TickAxis.PixelsPerTick * MusicTheory.RESOLUTION / quantizationBase / MIN_GRID_GAP).Floor()).Limit(1, 32);
        return MusicTheory.RESOLUTION / quantizationBase / division;
    }

    // 量化吸附（公开：参数区 AutomationRenderer 画范围选区时复用同一吸附口径，共用 TickAxis/Quantization 故结果一致）。
    public double GetQuantizedTick(double tick)
    {
        double cell = QuantizedCellTicks();
        return (tick / cell).Round() * cell;
    }

    // 范围选区(region selection)的内部态：一条贯穿全音高的全局 tick 带。由 Shift+拖（任意工具）或 Pitch/Lock+Ctrl 拖建立，
    // 常驻直到主键点击/切 part 清空。同时是钢琴窗 Copy/Paste/Delete 的范围（按当前工具决定作用于哪类数据）与脚本 tl.pianoSelection() 的数据源。
    class Selection
    {
        public double Start { get; set; } = 0;
        public double End { get; set; } = 0;
        public double Duration => End - Start;

        public bool IsAcitve { get; set; } = false;
    }

    Selection mSelection = new();

    // 范围选区变化通知：参数区 AutomationRenderer 订阅后重绘——区状态归本视图，参数区也画同一条 tick 带（且可在参数区 Shift+拖建区）。
    public event Action? RegionSelectionChanged;

    // 设置范围选区（全局 tick 区间，自动取 min/max）。音符区拖、参数区拖统一经此，触发本视图 + 参数区重绘。
    public void SetRegionSelection(double startTick, double endTick)
    {
        mSelection.Start = Math.Min(startTick, endTick);
        mSelection.End = Math.Max(startTick, endTick);
        mSelection.IsAcitve = true;
        SelectObjectsInRegion();   // 同步高亮选区覆盖的 note/vibrato（让"选区覆盖了谁"可见）
        OnRegionSelectionChanged();
    }

    // 把选区覆盖的 note + vibrato 全设为选中（不按工具区分）。**因为 Ctrl+C/Delete/Cut 在有选区时都作用于全部类型**，
    // 故"区内对象全高亮"是真话(它们确会被复制/删除)、不误导；note 高亮的唯一判别就是"是否选中"。
    // 头在选区内、左闭右开，与 AllNotesInSelection / Copy·Delete 的 range 同批（区别于框选的"重叠"判定）。
    void SelectObjectsInRegion()
    {
        if (Part == null)
            return;

        double pos = Part.Pos.Value;
        double s = mSelection.Start - pos;
        double e = mSelection.End - pos;

        Part.Notes.SelectionChanged.BeginMerge();
        Part.Notes.DeselectAllItems();
        foreach (var note in Part.AllNotesInSelection(s, e))
            note.Select();
        Part.Notes.SelectionChanged.EndMerge();

        Part.Vibratos.DeselectAllItems();
        foreach (var vibrato in Part.AllVibratosInSelection(s, e))
            vibrato.Select();
    }

    // 清空范围选区（主键点击未拖 / 切 part 调用）。无选区则不动。
    public void ClearRegionSelection()
    {
        if (!mSelection.IsAcitve)
            return;

        mSelection.IsAcitve = false;
        OnRegionSelectionChanged();
    }

    void OnRegionSelectionChanged()
    {
        InvalidateVisual();
        RegionSelectionChanged?.Invoke();
    }

    // 脚本侧 tl.pianoSelection() 的数据源 + 参数区绘制源：当前范围选区的全局 tick 区间（start ≤ end）；无选区 null。pitch 维不参与（贯穿全音高）。
    public (double StartTick, double EndTick)? CurrentRegionSelection
        => mSelection.IsAcitve ? (mSelection.Start, mSelection.End) : null;

    // —— 范围选区右键菜单：copy/paste 全类型 + 按类（Notes/Pitch/Vibratos/Automations），优先于各工具的右键行为。——
    // 心智模型：复制=按区填剪贴板（全部或某类）、粘贴=从剪贴板取（全部或某类，在光标处）。Ctrl+C/V 仍按当前工具，是同一功能的快捷路径。
    internal enum RegionDataKind { Notes, Pitch, Vibratos, Automations }

    // 是否存在激活选区。右键菜单的触发条件：只要有激活选区，右键（带内带外都行）即弹菜单——
    // 因为复制的源是选区、粘贴的目标常在选区外（要在别处右键粘贴）。类比文本编辑器"选中后右键=对选区操作"。
    // 菜单优先于各工具右键；选区一清（左键点击）各工具右键即恢复。参数区共用同一判定。
    public bool HasRegionSelection => mSelection.IsAcitve;

    // 横坐标 x 是否落在激活选区带内（带贯穿全高，只判 X）。用于右键菜单按"带内/带外"调整复制/粘贴族先后。
    public bool IsInRegion(double x)
        => mSelection.IsAcitve && x >= TickAxis.Tick2X(mSelection.Start) && x <= TickAxis.Tick2X(mSelection.End);

    // 复制选区：先清空全部剪贴板，再填选定子集。kind=null 复制全部；否则只填该类（pitch/automations 各自从 CopyParameters 拆出）。
    public void CopyRegion(RegionDataKind? kind)
    {
        if (Part == null || !mSelection.IsAcitve)
            return;

        double pos = Part.Pos.Value;
        double start = mSelection.Start - pos;
        double end = mSelection.End - pos;
        ClearClipboards();
        if (kind is null or RegionDataKind.Notes)
            mNoteClipboard = Part.CopyNotes(start, end);
        if (kind is null or RegionDataKind.Vibratos)
            mVibratoClipboard = Part.CopyVibratos(start, end);
        if (kind is null or RegionDataKind.Pitch or RegionDataKind.Automations)
        {
            var p = Part.CopyParameters(start, end);
            mParameterClipboard = new()
            {
                Pitch = kind is null or RegionDataKind.Pitch ? p.Pitch : [],
                Automations = kind is null or RegionDataKind.Automations ? p.Automations : [],
            };
        }
    }

    // 单类粘贴（光标 pos 处，从剪贴板取该类）。整块粘贴（全部）走 PasteAt。
    // 粒度粘贴的用处：复制一次（全部）后，按需只粘其中某几类——1 次复制 + N 次单类粘贴，比"复制 N 次 + 粘贴 N 次"省一半操作。
    public void PasteRegion(RegionDataKind kind, double pos)
    {
        if (Part == null)
            return;

        bool select = !mSelection.IsAcitve;   // 有选区时不抢选中态（保持选区高亮）
        switch (kind)
        {
            case RegionDataKind.Notes:
                if (mNoteClipboard.IsEmpty()) return;
                Part.PasteAt(mNoteClipboard, pos, select);
                break;
            case RegionDataKind.Vibratos:
                if (mVibratoClipboard.IsEmpty()) return;
                Part.PasteAt(mVibratoClipboard, pos, select);
                break;
            case RegionDataKind.Pitch:
                if (mParameterClipboard.Pitch.IsEmpty()) return;
                Part.PasteAt(new ParameterClipboard { Pitch = mParameterClipboard.Pitch, Automations = [] }, pos, Settings.ParameterBoundaryExtension);
                break;
            case RegionDataKind.Automations:
                if (mParameterClipboard.Automations.Count == 0) return;
                Part.PasteAt(new ParameterClipboard { Pitch = [], Automations = mParameterClipboard.Automations }, pos, Settings.ParameterBoundaryExtension);
                break;
        }
        Part.Commit();
    }

    // 删除选区内指定类型（kind=null 全部；不清区本身）。Pitch/Automations 各自拆出（= ClearParameters 的拆分），支持粒度删/剪。
    public void DeleteRegion(RegionDataKind? kind)
    {
        if (Part == null || !mSelection.IsAcitve)
            return;

        double pos = Part.Pos.Value;
        double s = mSelection.Start - pos;
        double e = mSelection.End - pos;
        if (kind is null or RegionDataKind.Notes)
            Part.DeleteAllNotesInSelection(s, e);
        if (kind is null or RegionDataKind.Vibratos)
            Part.DeleteAllVibratosInSelection(s, e);
        if (kind is null or RegionDataKind.Pitch)
            Part.Pitch.Clear(s, e);
        if (kind is null or RegionDataKind.Automations)
            foreach (var automation in Part.Automations.Values)
                automation.Clear(s, e, Settings.ParameterBoundaryExtension);
        Part.Commit();
    }

    // 剪切选区内指定类型 = 复制 + 删除（同一 kind）。
    public void CutRegion(RegionDataKind? kind)
    {
        CopyRegion(kind);
        DeleteRegion(kind);
    }

    // 剪贴板是否有该类（控制 Paste 各单类子项的显隐——只列剪贴板里有的）。
    public bool ClipboardHas(RegionDataKind kind) => kind switch
    {
        RegionDataKind.Notes => !mNoteClipboard.IsEmpty(),
        RegionDataKind.Vibratos => !mVibratoClipboard.IsEmpty(),
        RegionDataKind.Pitch => !mParameterClipboard.Pitch.IsEmpty(),
        RegionDataKind.Automations => mParameterClipboard.Automations.Count > 0,
        _ => false,
    };

    NoteClipboard mNoteClipboard = new();
    VibratoClipboard mVibratoClipboard = new();
    ParameterClipboard mParameterClipboard = new() { Pitch = [], Automations = [] };

    // 复制语义：每次 Copy / Copy All 先清空全部剪贴板，再按"这次复制了什么"往里填（单类型填一种、Copy All 填三种）。
    // 粘贴只看剪贴板里现有什么就粘什么 → 粘贴永远等于上次复制的内容，无隐藏模式、不会出现剪贴板与"模式"失同步的陈旧数据。
    void ClearClipboards()
    {
        mNoteClipboard = new();
        mVibratoClipboard = new();
        mParameterClipboard = new() { Pitch = [], Automations = [] };
    }

    // Ctrl+C：有激活选区 → 复制整个选区(全部类型)。多复制无害(粘贴时再按需选)，且让"区内对象全高亮"成为真话(它们确会被复制) → 不误导。
    // 无选区 → 按当前工具复制选中对象(Note/Vibrato)。想只复制 pitch/某条 automation(低频) → 走右键各单类项。Ctrl+V 统一粘剪贴板现有全部。
    public void Copy()
    {
        if (Part == null)
            return;

        if (mSelection.IsAcitve)
        {
            CopyRegion(null);
            return;
        }

        ClearClipboards();
        switch (mDependency.PianoTool.Value)
        {
            case PianoTool.Note:
                mNoteClipboard = Part.CopyNotes();
                break;
            case PianoTool.Vibrato:
                mVibratoClipboard = Part.CopyVibratos();
                break;
        }
    }

    public void Paste()
    {
        if (Part == null)
            return;

        PasteAt(GetQuantizedTick(mDependency.Playhead.Pos) - Part.Pos.Value);
    }

    // 粘贴 = 剪贴板里有啥粘啥（音符/颤音/参数各自独立，非空才粘）。不看当前工具——粘贴语义恒等于上次复制。
    // 有激活选区时不选中粘贴物（select=false）：保持"选中态恒 == 选区覆盖"的不变量，避免"复制到底针对选区还是选中音符"的困惑；
    // 无选区则选中粘贴物（标准行为）。
    public void PasteAt(double pos)
    {
        if (Part == null)
            return;

        bool select = !mSelection.IsAcitve;
        bool any = false;
        if (!mNoteClipboard.IsEmpty())
        {
            Part.PasteAt(mNoteClipboard, pos, select);
            any = true;
        }
        if (!mVibratoClipboard.IsEmpty())
        {
            Part.PasteAt(mVibratoClipboard, pos, select);
            any = true;
        }
        if (!mParameterClipboard.IsEmpty)
        {
            Part.PasteAt(mParameterClipboard, pos, Settings.ParameterBoundaryExtension);
            any = true;
        }
        if (any)
            Part.Commit();
    }

    public void Cut()
    {
        Copy();
        Delete();
    }

    // Delete：有激活选区 → 删整个选区(全部类型，= 右键 Delete Selection)。与 Ctrl+C 复制全部、区内全高亮一致——
    // 否则 Pitch 工具下 Delete 只清 pitch、而高亮的 note 不删，又成误导。无选区 → 按当前工具删选中对象。想只删某类(低频)走右键。
    public void Delete()
    {
        if (Part == null)
            return;

        if (mSelection.IsAcitve)
        {
            DeleteRegion(null);
            return;
        }

        switch (mDependency.PianoTool.Value)
        {
            case PianoTool.Note:
                Part.DeleteAllSelectedNotes();
                Part.Commit();
                break;
            case PianoTool.Vibrato:
                Part.DeleteAllSelectedVibratos();
                Part.Commit();
                break;
            case PianoTool.Anchor:
                Part.Pitch.DeleteAllSelectedAnchors();
                Part.Commit();
                break;
            default:
                break;
        }
    }

    public void ChangeKey(int offset)
    {
        if (Part == null)
            return;

        if (offset == 0)
            return;

        var selectedNotes = Part.Notes.AllSelectedItems();
        if (selectedNotes.IsEmpty())
            return;

        Part.BeginMergeDirty();
        foreach (var note in selectedNotes)
        {
            note.Pitch.Set(note.Pitch.Value + offset);
        }
        Part.EndMergeDirty();
        Part.Commit();
    }

    public void OctaveUp()
    {
        ChangeKey(+12);
    }

    public void OctaveDown()
    {
        ChangeKey(-12);
    }

    public void EnterInputLyric(INote note)
    {
        if (mInputLyricNote != null)
            return;

        mInputLyricNote = note;
        mLyricInput.Display(note.Lyric.Value);
        mLyricInput.IsVisible = true;
        mLyricInput.Focus();
        mLyricInput.SelectAll();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (mLyricInput.IsVisible)
            mLyricInput.Arrange(LyricInputRect());

        if (mPhonemeInput.IsVisible)
            mPhonemeInput.Arrange(PhonemeInputRect());

        return finalSize;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            if (Part != null && mInputLyricNote != null)
            {
                var x = TickAxis.Tick2X(mInputLyricNote.GlobalStartPos());
                var y = PitchAxis.Pitch2Y(mInputLyricNote.Pitch.Value);
                var note = e.HasModifiers(ModifierKeys.Shift) ? mInputLyricNote.Last : mInputLyricNote.Next;
                mLyricInput.Unfocus();
                if (note != null)
                {
                    EnterInputLyric(note);
                    TickAxis.AnimateMove(x - TickAxis.Tick2X(note.GlobalStartPos()));
                    PitchAxis.AnimateMove(y - PitchAxis.Pitch2Y(note.Pitch.Value));
                }
                e.Handled = true;
            }
        }

        if (e.IsHandledByTextBox())
            return;
    }

    void OnLyricInputComplete()
    {
        if (mInputLyricNote == null)
            return;

        var newLyric = mLyricInput.Text;
        if (!string.IsNullOrEmpty(newLyric) && newLyric != mInputLyricNote.Lyric.Value)
        {
            mInputLyricNote.Lyric.Set(newLyric);
            mInputLyricNote.Commit();
        }

        mLyricInput.IsVisible = false;
        mInputLyricNote = null;
    }

    Rect LyricInputRect()
    {
        if (mInputLyricNote == null)
            return new Rect();

        double x = TickAxis.Tick2X(mInputLyricNote.GlobalStartPos());
        double y = PitchAxis.Pitch2Y(mInputLyricNote.Pitch.Value + 0.5);
        double w = mInputLyricNote.Dur.Value * TickAxis.PixelsPerTick;
        double h = LyricInputHeight;
        return new Rect(x, y - h / 2, Math.Max(w, LyricInputMinWidth), h);
    }

    // 波形带下半区双击音素 → 就地编辑音素符号。提交时按空白拆分：n 个 token = 原音素等分为 n 段逐一命名；
    // 空输入（全删 / 纯空格）= 删除该音素。见 OnPhonemeInputComplete。
    public void EnterInputPhoneme(INote note, int index)
    {
        if (mInputPhonemeNote != null)
            return;

        var display = note.DisplayPhonemes;
        if (index >= display.Count)
            return;

        mInputPhonemeNote = note;
        mInputPhonemeIndex = index;
        mPhonemeInput.Display(display[index].Symbol);
        mPhonemeInput.IsVisible = true;
        mPhonemeInput.Focus();
        mPhonemeInput.SelectAll();
    }

    void OnPhonemeInputComplete()
    {
        if (mInputPhonemeNote == null)
            return;

        var note = mInputPhonemeNote;
        int index = mInputPhonemeIndex;
        mInputPhonemeNote = null;
        mPhonemeInput.IsVisible = false;

        // 按任意空白拆分；符号是自由文本、不校验（引擎不认识的符号由合成侧自行处理）。
        var tokens = mPhonemeInput.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var display = note.DisplayPhonemes;
        if (index >= display.Count)
            return;

        if (tokens.Length == 1 && tokens[0] == display[index].Symbol)
            return;   // 无变化，不惊动数据（不锁定、不产生 undo 步）

        var head = note.Part.Head;
        note.LockPhonemes();
        if (note.PhonemeCount != display.Count)
        {
            // 锁定后钉死音素应与显示音素一一对应；不一致说明状态在竞态中漂了，整体回滚。
            note.Part.DiscardTo(head);
            return;
        }

        // 全序列下标 → 所属列表 + 局部下标（增删在具体列表上做，同 slot 归属不变）。
        var (list, local) = note.LocatePhoneme(index);
        var phoneme = list[local];
        if (tokens.Length == 0)
        {
            // 空输入 = 删除该音素。全删空则该 note 回到合成音素口径（清空两列表与清除锁定同语义）。
            list.RemoveAt(local);
        }
        else
        {
            // 等分：时长 / 伸缩权重均摊——辅音(w=0)固定时长等分；核(w>0)按权重比例填充，等权即显示等分。
            // 各段留在同一列表（归属不变）；引擎自定义属性留在首段（拆分不复制，避免语义不明的双份属性）。
            double duration = phoneme.Duration.Value / tokens.Length;
            double weight = phoneme.StretchWeight.Value / tokens.Length;
            phoneme.Symbol.Set(tokens[0]);
            phoneme.Duration.Set(duration);
            phoneme.StretchWeight.Set(weight);
            for (int i = 1; i < tokens.Length; i++)
            {
                list.Insert(local + i, Phoneme.Create(new PhonemeInfo
                {
                    Symbol = tokens[i],
                    Duration = duration,
                    StretchWeight = weight,
                }));
            }
        }
        note.Commit();
    }

    Rect PhonemeInputRect()
    {
        var note = mInputPhonemeNote;
        if (note == null || Part == null)
            return new Rect();

        var display = note.DisplayPhonemes;
        if (mInputPhonemeIndex >= display.Count)
            return new Rect();

        var tempoManager = Part.TempoManager;
        double left = TickAxis.Tick2X(tempoManager.GetTick(display[mInputPhonemeIndex].StartTime));
        double right = TickAxis.Tick2X(tempoManager.GetTick(display[mInputPhonemeIndex].EndTime));
        // 填满整个下室格子：高 = 下室全高、宽 = 音素区间宽（文字垂直居中即与画的符号同位置）。
        // 窄音素以区间中点为锚向两侧对称加宽到最小宽，保证加宽后文字中心不偏。
        double centerX = (left + right) / 2;
        double w = Math.Max(right - left, PhonemeInputMinWidth);
        return new Rect(centerX - w / 2, WaveformCenterY, w, WaveformBottom - WaveformCenterY);
    }

    INote? mInputLyricNote = null;
    INote? mInputPhonemeNote = null;
    int mInputPhonemeIndex;

    const int LyricInputFontSize = 12;
    const double LyricInputVerticalPadding = 8;
    const double LyricInputHeight = LyricInputFontSize + 2 * LyricInputVerticalPadding;
    const double LyricInputMinWidth = 60;
    const double PhonemeInputMinWidth = 48;

    readonly TextInput mLyricInput;
    readonly TextInput mPhonemeInput;

    // 钢琴窗右侧装饰图（静态 / 动图统一走 ImagePlayer）：立绘随当前音源声库变，背景图来自全局设置。
    // 立绘优先——有立绘只画立绘，否则才画背景图（见 Render 与 UpdateImagePlayback）。
    ImagePlayer? mPortrait = null;
    string? mPortraitPath = null;
    ImagePlayer? mBackground = null;

    Color WhiteKeyColor => GUI.Style.WHITE_KEY;
    Color BlackKeyColor => GUI.Style.BLACK_KEY;
    Color LineColor => GUI.Style.LINE;
    Color SelectionColor => GUI.Style.HIGH_LIGHT;
    // 范围选区(region selection)用白色：hue-neutral，且与"框选对象"的 HIGH_LIGHT 实色框靠色相+线型区分。
    static Color RegionSelectionColor => GUI.Style.WHITE;
    const double MIN_GRID_GAP = 12;
    const double MIN_REALITY_GRID_GAP = MIN_GRID_GAP * 2;
    const double LineWidth = 1;
    public const double WAVEFORM_HEIGHT = 56;   // 半室 28 = 标准文本框高，音素输入框恰好填满下室格子

    double WaveformTop => mDependency.WaveformBottom - WAVEFORM_HEIGHT;
    double WaveformBottom => mDependency.WaveformBottom;
    // 波形带上下分层的分界（仅 voice 语义生效）：上半区 = note 边界操作、下半区 = 音素操作。
    double WaveformCenterY => mDependency.WaveformBottom - WAVEFORM_HEIGHT / 2;

    readonly DisposableManager s = new();

    readonly IDependency mDependency;
    public TickAxis TickAxis => mDependency.TickAxis;
    public PitchAxis PitchAxis => mDependency.PitchAxis;
    IQuantization Quantization => mDependency.Quantization;
    IMidiPart? Part => mDependency.PartHolder.Value;
    ParameterButton PitchButton => mDependency.PitchButton;
}
