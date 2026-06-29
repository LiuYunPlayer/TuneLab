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
                    context.DrawString($"{midiPart.Name}[{midiPart.SoundSource.Name}]", titleRect, titleBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter, typeface: new Typeface(FontFamily.Default, weight: isEditingPart ? FontWeight.Bold : FontWeight.Normal));
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
                using (context.PushClip(titleRect))
                {
                    context.DrawString($"{audioPart.Name}[{audioPart.Path}]", titleRect, titleBrush, 12, Alignment.LeftCenter, Alignment.LeftCenter);
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
                                double startTime = audioPart.TempoManager.GetTime(audioPart.StartPos);
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

            // 编辑维度：在标题栏右上角放一枚醒目的 ✏️ 图标（斜放，更像“在写”），明确“正在编辑此 part”。
            // 钳到视野内：横向锚到 part 右缘与视口右缘的较小者，故 part 滑出右侧时图标仍贴在视口右沿可见。
            if (isEditingPart)
            {
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
