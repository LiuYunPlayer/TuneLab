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

        // part 自绘：本体底色/标题/midi 音符或波形/基础轮廓/选中（白叠加+白边框）/编辑（强调色边框+外发光）。
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
            // 选中/编辑不再靠改色区分（会与轨道色撞色）：颜色恒为轨道色，
            // 选中用白色叠加+白边框、编辑用强调色边框+外发光，二者是解耦的两维。
            var frameColor = trackColor;

            IBrush titleBrush = Brushes.Black;
            IBrush statusBrush = Brushes.White;
            double partLineWidth = 1;

            var partRect = new Rect(left, top, right - left, bottom - top);
            context.DrawRectangle(trackColor.Opacity(0.25).ToBrush(), null, partRect, 4, 4);

            var titleRect = partRect.WithHeight(16).Adjusted(Math.Max(0, -partRect.Left) + 8, 0, -8, 0);
            context.DrawRectangle(frameColor.ToBrush(), null, partRect.WithHeight(16).ToRoundedRect(new(4, 4, 0, 0)));
            var contentRect = partRect.Adjusted(0, 20, 0, -4);
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
                        double pitchHeight = Math.Min(contentRect.Height / pitchGap, 8);
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

                // 合成状态条：贴标题栏下沿一条同款细带（上沿拍直贴标题、下沿小圆角），全局一眼扫到谁在跑/失败。
                // 位置随标题固定、跨轨对齐；落在标题(16)与内容(20)之间的空隙里、不压音符。编辑视图静态——无流光、无 hover。
                var synthesisStatus = midiPart.GetSynthesisStatus();
                if (synthesisStatus.Count > 0)
                {
                    const double titleHeight = 16, stripHeight = 3;
                    double stripTop = top + titleHeight;
                    if (stripTop + stripHeight <= bottom)   // part 够高才画
                    {
                        using (context.PushClip(partRect))
                            SynthesisStatusStrip.Draw(context, synthesisStatus, tempoManager, view.TickAxis, stripTop, stripHeight, 1.5);
                    }
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

            // 选中维度：整块罩半透明白（色彩无关地“提亮”）+ 内侧 2px 白边框。
            if (part.IsSelected)
            {
                context.DrawRectangle(Style.WHITE.Opacity(0.16).ToBrush(), null, partRect, 4, 4);
                context.DrawRectangle(null, new Pen(Style.WHITE.ToBrush(), 2), partRect.Inflate(-1), 4, 4);
            }

            // 编辑维度：取轨道色的提亮版做“同色相外发光”（向外渐隐的同心环）+ 紧贴外缘的提亮实边框。
            // 用色相一致的光晕 → 任意轨道色下都和谐（不像固定强调色在异色 part 上突兀）；
            // 靠“只有编辑态才有光晕”这个效果本身区分，与选中的白色叠加/白边框（不同色相+内外位置）并存不冲突。
            if (isEditingPart)
            {
                var glowColor = trackColor.Brighter(0.5);
                int rings = TrackScrollView.GlowRingCount;
                for (int g = rings; g >= 1; g--)
                {
                    double inflate = g * 2;
                    double opacity = 0.55 * (1 - (g - 1) / (double)rings);
                    double radius = 4 + inflate;
                    context.DrawRectangle(null, new Pen(glowColor.Opacity(opacity).ToBrush(), 2.5), partRect.Inflate(inflate), radius, radius);
                }
                context.DrawRectangle(null, new Pen(glowColor.ToBrush(), 2), partRect.Inflate(1), 5, 5);
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
