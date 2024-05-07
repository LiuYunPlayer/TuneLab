using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Structures;
using TuneLab.Base.Science;
using TuneLab.Base.Utils;

namespace TuneLab.Audio;

internal class Waveform
{
    public struct Peak
    {
        public float min;
        public float max;
        public double minRatio;
        public double maxRatio;
    }

    public int SampleCount => mAudioData.Count;

    public Waveform(IReadOnlyList<float> audioData, int peakHop = 64)
    {
        mAudioData = audioData;
        mPeakHop = peakHop;
        UpdatePeak(0, SampleCount);
    }

    public Peak[] GetPeaks(IReadOnlyList<double> positions, IReadOnlyList<float> values)
    {
        int forCount = positions.Count - 1;
        int lastIndex;
        int lastPi;
        int nextIndex;
        int nextPi;

        var peaks = new Peak[forCount];

        for (int peakIndex = 0; peakIndex < forCount; peakIndex++)
        {
            double nextPosition = positions[peakIndex + 1];
            nextIndex = Math.Min((int)nextPosition + 1, SampleCount);
            if (nextIndex <= 0)
            {
                continue;
            }

            double lastPosition = positions[peakIndex];
            lastIndex = Math.Max((int)lastPosition + 1, 0);
            if (lastIndex >= SampleCount)
            {
                break;
            }

            var peak = new Peak();
            double length = nextPosition - lastPosition;

            var leftValue = values[peakIndex];
            var rightValue = values[peakIndex + 1];
            if (leftValue < rightValue)
            {
                peak.min = leftValue;
                peak.max = rightValue;
                peak.minRatio = 0;
                peak.maxRatio = 1;
            }
            else
            {
                peak.min = rightValue;
                peak.max = leftValue;
                peak.minRatio = 1;
                peak.maxRatio = 0;
            }

            if (lastIndex >= nextIndex)
            {
                goto Assignment;
            }

            lastPi = lastIndex / mPeakHop;
            nextPi = nextIndex / mPeakHop;

            // 当不横跨一个peak时直接找min&max
            if (lastPi == nextPi)
            {
                var offset = lastIndex;
                var count = Math.Max(1, nextIndex - offset);
                for (int i = 0; i < count; i++)
                {
                    var d = mAudioData[i + offset];
                    if (d < peak.min)
                    {
                        peak.min = d;
                        peak.minRatio = (i + offset - lastPosition) / length;
                    }
                    else if (d > peak.max)
                    {
                        peak.max = d;
                        peak.maxRatio = (i + offset - lastPosition) / length;
                    }
                }
            }
            else
            {
                var startEnd = (lastPi + 1) * mPeakHop;
                var offset = lastIndex;
                var count = startEnd - offset;

                for (int i = 0; i < count; i++)
                {
                    var d = mAudioData[i + offset];
                    if (d < peak.min)
                    {
                        peak.min = d;
                        peak.minRatio = (i + offset - lastPosition) / length;
                    }
                    else if (d > peak.max)
                    {
                        peak.max = d;
                        peak.maxRatio = (i + offset - lastPosition) / length;
                    }
                }

                var endStart = nextPi * mPeakHop;
                offset = endStart;
                count = nextIndex - offset;

                for (int i = 0; i < count; i++)
                {
                    var d = mAudioData[i + offset];
                    if (d < peak.min)
                    {
                        peak.min = d;
                        peak.minRatio = (i + offset - lastPosition) / length;
                    }
                    else if (d > peak.max)
                    {
                        peak.max = d;
                        peak.maxRatio = (i + offset - lastPosition) / length;
                    }
                }

                for (int pi = lastPi + 1; pi < nextPi; pi++)
                {
                    var p = mPeaks[pi];
                    var dmin = p.min;
                    if (dmin < peak.min)
                    {
                        peak.min = dmin;
                        peak.minRatio = (p.minIndex - lastPosition) / length;
                    }
                    var dmax = p.max;
                    if (dmax > peak.max)
                    {
                        peak.max = dmax;
                        peak.maxRatio = (p.maxIndex - lastPosition) / length;
                    }
                }
            }

        Assignment:
            peaks[peakIndex] = peak;
        }

        return peaks;
    }

    public float[] GetValues(IReadOnlyList<double> positions, int sincSamples = 4)
    {
        var sinc = MathUtility.GetFastSinc(sincSamples);

        float[] values = new float[positions.Count];

        for (int pi = 0; pi < positions.Count; pi++)
        {
            double p = positions[pi];
            double sp = p - sincSamples;
            double ep = p + sincSamples;
            int offset = Math.Max((int)sp + 1, 0);
            int count = Math.Min(ep.Ceil(), SampleCount) - offset;
            if (count <= 0)
            {
                continue;
            }

            double rp = p - offset;
            for (int i = 0; i < count; i++)
            {
                values[pi] += mAudioData[i + offset] * (float)sinc.Calculate(i - rp);
            }
        }

        return values;
    }

    void UpdatePeak(int startIndex, int endIndex)
    {
        // 入参合法化
        startIndex = Math.Max(startIndex, 0);
        endIndex = Math.Min(endIndex, SampleCount);
        if (startIndex >= endIndex)
        {
            return;
        }

        // 初始化peak数组
        int startPeakIndex = startIndex / mPeakHop;
        int endPeakIndex = ((double)endIndex / mPeakHop).Ceil();

        if (mPeaks.Count < endPeakIndex)
        {
            mPeaks.Resize(endPeakIndex);
        }

        for (int peakIndex = startPeakIndex; peakIndex < endPeakIndex; peakIndex++)
        {
            int si = peakIndex * mPeakHop;
            int ei = Math.Min(si + mPeakHop, SampleCount);
            var min = mAudioData[si];
            var max = min;
            int minIndex = si;
            int maxIndex = minIndex;
            for (int i = si + 1; i < ei; i++)
            {
                var d = mAudioData[i];
                if (d < min)
                {
                    min = d;
                    minIndex = i;
                }
                else if (d > max)
                {
                    max = d;
                    maxIndex = i;
                }
            }
            mPeaks[peakIndex] = new PeakInfo() { min = min, max = max, minIndex = minIndex, maxIndex = maxIndex };
        }
    }

    struct PeakInfo
    {
        public float min;
        public float max;
        public int minIndex;
        public int maxIndex;
    }

    readonly IReadOnlyList<float> mAudioData;
    readonly int mPeakHop;
    readonly List<PeakInfo> mPeaks = new();
}
