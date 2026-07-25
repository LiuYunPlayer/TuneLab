using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Data;
using TuneLab.SDK;
using TuneLab.Utils;
using TuneLab.I18N;
using TuneLab.Extensions.Derivers;

using Point = Avalonia.Point;

namespace TuneLab.UI;

internal partial class TrackScrollView
{
    class TrackScrollViewItem(TrackScrollView trackScrollView) : Item
    {
        public TrackScrollView TrackScrollView => trackScrollView;
    }

    interface IPartItem
    {
        IPart Part { get; }
        int TrackIndex { get; }
    }

    class PartItem(TrackScrollView trackScrollView) : TrackScrollViewItem(trackScrollView), IPartItem
    {
        public IPart Part { get; set; }
        public int TrackIndex { get; set; }

        public Rect Rect()
        {
            double top = TrackScrollView.TrackVerticalAxis.GetTop(TrackIndex);
            double bottom = TrackScrollView.TrackVerticalAxis.GetBottom(TrackIndex);
            double left = TrackScrollView.TickAxis.Tick2X(Part.StartPos());
            double right = TrackScrollView.TickAxis.Tick2X(Part.EndPos());

            return new Rect(left, top, right - left, bottom - top);
        }

        public override bool Raycast(Avalonia.Point point)
        {
            return Rect().Contains(point);
        }

        // part 自绘：本体底色/标题/midi 音符或波形/基础轮廓/选中（白内描边）/编辑（标题右上角斜放 ✏️ 图标）。
        // 竖向位置经 GetTop/GetBottom（drag-aware）取得，故拖动轨道头时内容自动跟随。
        public override void Render(DrawingContext context)
        {
            var view = TrackScrollView;
            var project = view.Project;
            if (project == null)
                return;

            var part = Part;
            var track = part.Track;
            var tempoManager = project.TempoManager;
            double startPos = view.TickAxis.MinVisibleTick;
            double endPos = view.TickAxis.MaxVisibleTick;

            bool isEditingPart = part == view.mDependency.EditingPart.Value;
            double top = view.TrackVerticalAxis.GetTop(TrackIndex);
            double bottom = view.TrackVerticalAxis.GetBottom(TrackIndex);
            double left = Math.Max(view.TickAxis.Tick2X(part.StartPos()), -8);
            double right = Math.Min(view.TickAxis.Tick2X(part.EndPos()), view.Bounds.Width + 8);

            // 派生角标一次性求出（供标题右裁 + 后续绘制共用）：仅音频 part、有记录时。
            bool hasBadge = false;
            int badgeCount = 0;
            var badgeStatus = DerivationRecordStatus.Invalidated;
            double badgeProgress = -1;
            Rect badgeRect = default;
            if (part is IAudioPart badgeAudioPart && DerivationTaskManager.TryGetPartBadge(badgeAudioPart, out badgeCount, out badgeStatus, out badgeProgress))
            {
                hasBadge = true;
                badgeRect = DerivationBadgeRect(view, part, TrackIndex);
            }

            var trackColor = track.GetColor();
            // 选中→白色内描边（不叠白罩——会与编排区范围选区的白叠加层撞）；
            // 编辑→仅在标题右上角放一枚斜放 ✏️ 图标，不改本体色。
            var frameColor = trackColor;

            IBrush titleBrush = Brushes.Black;
            IBrush statusBrush = Brushes.White;
            double partLineWidth = 1;

            var partRect = new Rect(left, top, right - left, bottom - top);
            context.DrawRectangle(trackColor.Opacity(0.25).ToBrush(), null, partRect, 4, 4);

            var titleRect = partRect.WithHeight(16).Adjusted(Math.Max(0, -partRect.Left) + 8, 0, -8, 0);
            context.DrawRectangle(frameColor.ToBrush(), null, partRect.WithHeight(16).ToRoundedRect(new(4, 4, 0, 0)));
            // note 内容上移到 +24（与上方状态条 16–19 拉开 ~5px，避免缩略图贴着状态条难分）；下边缘仍留 4px。
            var contentRect = partRect.Adjusted(0, 24, 0, -4);
            if (part is MidiPart midiPart)
            {
                using (context.PushClip(titleRect))
                {
                    context.DrawString($"{midiPart.Name}[{midiPart.SoundSource.Name}]", titleRect, titleBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter, typeface: new Typeface(AppFont.Current, weight: isEditingPart ? FontWeight.Bold : FontWeight.Normal));
                }

                if (!midiPart.Notes.IsEmpty())
                {
                    using (context.PushClip(contentRect))
                    {
                        var (minPitch, maxPitch) = midiPart.PitchRange();
                        double pitchGap = maxPitch - minPitch + 1;
                        double pitchHeight = Math.Min(contentRect.Height / pitchGap, 4);   // 上限 4（偶数像素对齐）：音域很窄时不至于显得过胖
                        double partStartPos = Math.Max(startPos, midiPart.StartPos) - midiPart.Pos;
                        double partEndPos = Math.Min(endPos, midiPart.EndPos) - midiPart.Pos;
                        IBrush brush = frameColor.ToBrush();
                        foreach (var note in midiPart.Notes)
                        {
                            if (note.EndPos() <= partStartPos)
                                continue;

                            if (note.StartPos() >= partEndPos)
                                break;

                            double noteLeft = view.TickAxis.Tick2X(note.StartPos() + midiPart.Pos);
                            double noteRight = view.TickAxis.Tick2X(note.EndPos() + midiPart.Pos);
                            context.FillRectangle(brush, new(noteLeft, contentRect.Y + (maxPitch - note.Pitch.Value) * pitchHeight, noteRight - noteLeft, pitchHeight));
                        }
                    }
                }

                // 合成状态条：贴标题栏下沿，只标“非可播放”的脏/错区间——待合成&合成中=灰（合成中叠灰色流光）、失败=红；
                // 已合成/空 part 不显条（绿=可播放=干净）。位置随标题固定、跨轨对齐，落在标题(16)与内容(20)之间、不压音符。
                const double titleHeight = 16, stripHeight = 3;
                double stripTop = top + titleHeight;
                if (stripTop + stripHeight <= bottom)   // part 够高才画
                {
                    using (context.PushClip(partRect))
                        SynthesisStatusStrip.DrawCoarse(context, midiPart.GetSynthesisStatus(), tempoManager, view.TickAxis, stripTop, stripHeight, 1.5, view.SynthesisShimmerPhase);
                }
            }
            else if (part is AudioPart audioPart)
            {
                // 标题右缘让位派生角标（有角标时裁到角标左缘 − 4px），避免长文件名钻到角标下面。
                var audioTitleRect = hasBadge ? titleRect.WithWidth(Math.Max(0, badgeRect.Left - 4 - titleRect.Left)) : titleRect;
                using (context.PushClip(audioTitleRect))
                {
                    context.DrawString($"{audioPart.Name}[{audioPart.Path}]", audioTitleRect, titleBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter);
                }

                var statusRect = contentRect.Adjusted(Math.Max(0, -contentRect.Left) + 8, 0, -8, 0);
                switch (audioPart.Status.Value)
                {
                    case AudioPartStatus.Loading:
                        using (context.PushClip(statusRect))
                            context.DrawString("Loading...".Tr(view), statusRect, statusBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter);
                        break;
                    case AudioPartStatus.Unlinked:
                        using (context.PushClip(statusRect))
                            context.DrawString("Failed to load audio. Double click to relink.".Tr(view), statusRect, statusBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter);
                        break;
                    case AudioPartStatus.Linked:
                        if (audioPart.ChannelCount > 0)
                        {
                            for (int channelIndex = 0; channelIndex < audioPart.ChannelCount; channelIndex++)
                            {
                                if (audioPart.EndPos < view.TickAxis.MinVisibleTick)
                                    continue;

                                if (audioPart.StartPos > view.TickAxis.MaxVisibleTick)
                                    break;

                                var waveform = audioPart.GetWaveform(channelIndex);
                                if (waveform == null)
                                    continue;

                                double minTick = Math.Max(view.TickAxis.MinVisibleTick, audioPart.StartPos);
                                double maxTick = Math.Min(view.TickAxis.MaxVisibleTick, audioPart.EndPos);
                                double minX = view.TickAxis.Tick2X(minTick);
                                double maxX = view.TickAxis.Tick2X(maxTick);
                                var xs = new List<double>();
                                var positions = new List<double>();
                                double gap = 1;
                                double xp = minX - gap;
                                // 样本 0 锚在锚点 Pos（非可见起点）：前向裁剪时波形随之揭示后段、锚点前为静音，与播放一致。
                                double startTime = audioPart.TempoManager.GetTime(audioPart.Pos.Value);
                                do
                                {
                                    xp += gap;
                                    xs.Add(xp);
                                    double time = tempoManager.GetTime(view.TickAxis.X2Tick(xp));
                                    positions.Add((time - startTime) * ((IAudioSource)audioPart).SampleRate);
                                }
                                while (xp < maxX);

                                if (positions.Count < 2)
                                    continue;

                                double channelHeight = contentRect.Height / audioPart.ChannelCount;
                                float channelTop = (float)(contentRect.Top + channelHeight * channelIndex);
                                float r = (float)channelHeight / 2;
                                float toY(float value) => channelTop + (1 - value) * r;

                                var values = waveform.GetValues(positions);
                                var peaks = waveform.GetPeaks(positions, values);
                                for (int i = 0; i < xs.Count; i++)
                                {
                                    values[i] = toY(values[i]);
                                }
                                for (int i = 0; i < peaks.Length; i++)
                                {
                                    peaks[i].min = toY(peaks[i].min);
                                    peaks[i].max = toY(peaks[i].max);
                                }
                                // 性能优先先采用画矩形的方案
                                IBrush waveformBrush = frameColor.ToBrush();
                                for (int i = 0; i < peaks.Length; i++)
                                {
                                    var peak = peaks[i];
                                    context.FillRectangle(waveformBrush, new(xs[i], peak.max, gap, peak.min - peak.max));
                                }
                            }
                        }
                        break;
                }
            }

            // 基础轮廓（恒在）：轨道色淡描边，给未选中 part 一道边。
            context.DrawRectangle(
                null,
                new Pen(frameColor.Opacity(0.5).ToBrush(), partLineWidth),
                partRect.Inflate(-partLineWidth / 2),
                4, 4);

            // 选中维度：2px 白色内描边（内描边落在 part 内，相邻 part 紧挨也不重叠/越界）。
            if (part.IsSelected)
            {
                context.DrawRectangle(null, new Pen(Style.WHITE.ToBrush(), 2), partRect.Inflate(-1), 4, 4);
            }

            // 派生记录角标（仅音频 part、有记录时，早先已一次性求出）：标题栏右上角、主导态着色；
            // 点击（命中在 DerivationBadgeItem）= 选中该 part + 打开 Derivation 侧栏并滚到其组（不自带 flyout）。
            if (hasBadge)
            {
                DrawDerivationBadge(context, badgeRect, badgeCount, badgeStatus, badgeProgress);
            }

            // 编辑维度：在标题栏右上角放一枚醒目的 ✏️ 图标（斜放，更像“在写”），明确“正在编辑此 part”。
            // 钳到视野内：横向锚到 part 右缘与视口右缘的较小者，故 part 滑出右侧时图标仍贴在视口右沿可见。
            if (isEditingPart)
            {
                // 钢琴窗视野白框：横向 = 钢琴窗可见 tick 区间；纵向 = 可见音高区间按缩略图同一套映射
                // （contentRect.Y + (maxPitch + 1 - pitch) * pitchHeight，与上方画 note 的式子一致）换算，均钳进 part。
                // 与“选中”的 2px 白色内描边区分：此框 1px 半透明，且随钢琴窗滚动/缩放实时移动。
                double viewportLeft = Math.Max(view.TickAxis.Tick2X(view.PianoTickAxis.MinVisibleTick), left);
                double viewportRight = Math.Min(view.TickAxis.Tick2X(view.PianoTickAxis.MaxVisibleTick), right);
                double viewportTop = titleRect.Bottom;   // 上界钳到标题栏下沿，白框不入侵标题栏
                double viewportBottom = bottom;
                if (part is MidiPart editingMidiPart && !editingMidiPart.Notes.IsEmpty())
                {
                    var (minPitch, maxPitch) = editingMidiPart.PitchRange();
                    double pitchHeight = Math.Min(contentRect.Height / (maxPitch - minPitch + 1), 4);
                    double PitchToThumbY(double pitch) => contentRect.Y + (maxPitch + 1 - pitch) * pitchHeight;
                    viewportTop = Math.Max(PitchToThumbY(view.PianoPitchAxis.MaxVisiblePitch), titleRect.Bottom);
                    viewportBottom = Math.Min(PitchToThumbY(view.PianoPitchAxis.MinVisiblePitch), bottom);
                }
                if (viewportRight > viewportLeft && viewportBottom > viewportTop)
                {
                    const double viewportLineWidth = 1;
                    var viewportRect = new Rect(viewportLeft, viewportTop, viewportRight - viewportLeft, viewportBottom - viewportTop);
                    context.DrawRectangle(null, new Pen(Style.WHITE.Opacity(0.7).ToBrush(), viewportLineWidth), viewportRect.Inflate(-viewportLineWidth / 2));
                }

                const double iconSize = 14;
                const double margin = 4;
                double centerX, centerY = top + 8;   // 标题栏(16px)垂直居中
                double maxCenterX = Math.Min(right, view.Bounds.Width) - margin - iconSize / 2;
                double minCenterX = Math.Max(left, 0) + margin + iconSize / 2;   // 极窄 part：退到可见左缘而非画出框外
                centerX = Math.Max(minCenterX, maxCenterX);

                // 绕图标中心转 ~140°，让笔尖朝左下、笔身斜向右上（书写姿态）。
                var center = new Avalonia.Point(centerX, centerY);
                var rotate = Avalonia.Matrix.CreateTranslation(-center.X, -center.Y)
                    * Avalonia.Matrix.CreateRotation(140 * Math.PI / 180)
                    * Avalonia.Matrix.CreateTranslation(center.X, center.Y);
                using (context.PushTransform(rotate))
                    context.DrawString("✏️", center, titleBrush, iconSize, Alignment.Center);
            }
        }
    }

    // 派生角标几何（音频 part 标题栏右上角固定 32×16 小圆角标：派生图标 + 数量）：Render 与命中共用同一算式、保证一致。
    // 右缘留 10px（避开 part 右缘 ±8 的裁剪手柄命中区）；窄 part 退到可见左缘 + 4px、不画出框外。
    static Rect DerivationBadgeRect(TrackScrollView view, IPart part, int trackIndex)
    {
        double top = view.TrackVerticalAxis.GetTop(trackIndex);
        double left = Math.Max(view.TickAxis.Tick2X(part.StartPos()), -8);
        double partRight = view.TickAxis.Tick2X(part.EndPos());
        const double w = 32, h = 12, rightPad = 10, leftPad = 4, scrollReserve = 12;
        // 徽标右缘 = min(part 右缘 − 手柄留白, 视口右缘 − 纵向滚动条预留)：part 尾在视野内贴其右缘、尾在视野外贴视口
        // 右沿但避开右侧自动滚动条（thumb 8 + 边距 2 ≈ 10，留 12）。
        double badgeRight = Math.Min(partRight - rightPad, view.Bounds.Width - scrollReserve);
        double bx = Math.Max(badgeRight - w, left + leftPad);
        double by = top + (16 - h) / 2;   // 标题栏 16、徽标 12 => 上下各留 2px（避开选中 part 的 2px 白描边）
        return new Rect(bx, by, w, h);
    }

    // 按前景色缓存的派生图标 SvgImage（GetImage 内含 LoadFromSvg 解析、昂贵；徽标在进度 tick 时重绘，必须缓存）。
    static readonly Dictionary<uint, IImage> sDeriveIcons = new();
    static IImage DeriveIcon(Color color)
    {
        uint key = color.ToUInt32();
        if (!sDeriveIcons.TryGetValue(key, out var image))
            sDeriveIcons[key] = image = Assets.Derive.GetImage(color);
        return image;
    }

    // 徽标背景即「完成度」语义：
    //   · 有任务进行中 => 亮色底填到平均进度（已完成段亮 + 未完成段暗），前景白；
    //   · 无在飞任务且可应用（= 完成态）=> 整块亮色底（满进度），前景白；
    //   · 其余（失败/已失效/仅排队）=> 暗底 + 状态色前景（失败红 / 已失效暗 / 白）。
    // 不用主题色当"轨道色"那种大面积撞色场景——徽标是叠在标题栏上的小 chip，亮底面积小、且是明确的完成语义。
    static void DrawDerivationBadge(DrawingContext context, Rect rect, int count, DerivationRecordStatus dominant, double runningProgress)
    {
        bool inProgress = runningProgress >= 0;
        bool completed = !inProgress && dominant == DerivationRecordStatus.Applicable;

        using (context.PushClip(new RoundedRect(rect, 2)))
        {
            context.FillRectangle(Style.BACK.Opacity(0.85).ToBrush(), rect);   // 暗底（未完成段 / 非完成态）
            if (inProgress)
            {
                double p = Math.Clamp(runningProgress, 0, 1);
                if (p > 0)
                    context.FillRectangle(Style.HIGH_LIGHT.ToBrush(), new Rect(rect.X, rect.Y, rect.Width * p, rect.Height));
            }
            else if (completed)
            {
                context.FillRectangle(Style.HIGH_LIGHT.ToBrush(), rect);   // 完成态：满亮底
            }
        }

        var content = (inProgress || completed) ? Colors.White : dominant switch
        {
            DerivationRecordStatus.Failed => Style.SYNTHESIS_FAILED,
            DerivationRecordStatus.Invalidated => Style.LIGHT_WHITE.Opacity(0.5),
            _ => Colors.White,   // Queued
        };

        const double iconSize = 10;
        var iconRect = new Rect(rect.X + 3, rect.Y + (rect.Height - iconSize) / 2, iconSize, iconSize);
        context.DrawImage(DeriveIcon(content), iconRect);

        var numCenter = new Point((iconRect.Right + rect.Right - 3) / 2, rect.Center.Y);
        context.DrawString(count > 9 ? "9+" : count.ToString(), numCenter, content.ToBrush(), 9, Alignment.Center);
    }

    // 派生角标命中区（音频 part、有记录时才注册）：点击 = 打开 Derivation 面板并定位到该 part 的组。
    class DerivationBadgeItem(TrackScrollView trackScrollView) : TrackScrollViewItem(trackScrollView)
    {
        public IPart Part = null!;
        public int TrackIndex;

        public override bool Raycast(Avalonia.Point point)
        {
            return DerivationBadgeRect(TrackScrollView, Part, TrackIndex).Contains(point);
        }
    }

    class PartEndResizeItem(TrackScrollView trackScrollView) : TrackScrollViewItem(trackScrollView)
    {
        public IPart Part;
        public int TrackIndex;

        public override bool Raycast(Avalonia.Point point)
        {
            double top = TrackScrollView.TrackVerticalAxis.GetTop(TrackIndex);
            double bottom = TrackScrollView.TrackVerticalAxis.GetBottom(TrackIndex);
            double x = TrackScrollView.TickAxis.Tick2X(Part.EndPos());
            return point.Y >= top && point.Y <= bottom && point.X > x - 8 && point.X < x + 8;
        }
    }

    // 左边缘拖拽命中区（可见起点 ±8px、贯穿整轨高）：前向裁剪/扩展手柄，与右边缘的 PartEndResizeItem 对称。
    class PartStartResizeItem(TrackScrollView trackScrollView) : TrackScrollViewItem(trackScrollView)
    {
        public IPart Part;
        public int TrackIndex;

        public override bool Raycast(Avalonia.Point point)
        {
            double top = TrackScrollView.TrackVerticalAxis.GetTop(TrackIndex);
            double bottom = TrackScrollView.TrackVerticalAxis.GetBottom(TrackIndex);
            double x = TrackScrollView.TickAxis.Tick2X(Part.StartPos());
            return point.Y >= top && point.Y <= bottom && point.X > x - 8 && point.X < x + 8;
        }
    }

    class PartNameItem (TrackScrollView trackScrollView) : TrackScrollViewItem(trackScrollView), IPartItem
    {
        public IPart Part { get; set; }
        public int TrackIndex { get; set; }

        public override bool Raycast(Avalonia.Point point)
        {
            double top = TrackScrollView.TrackVerticalAxis.GetTop(TrackIndex);
            double left = TrackScrollView.TickAxis.Tick2X(Part.StartPos());
            double right = TrackScrollView.TickAxis.Tick2X(Part.EndPos());

            var titleRect = new Rect(left, top, right - left, 16);
            return titleRect.Contains(point);
        }
    }
}
