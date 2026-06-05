using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.Science;
using TuneLab.Foundation.DataStructures;
using TuneLab.Primitives.DataStructures;
using TuneLab.Foundation.Utils;
using TuneLab.GUI.Input;
using TuneLab.Utils;

namespace TuneLab.GUI.Components
{
    internal class AmplitudeViewer : Control
    {
        bool LevelUnit = false;//Level Unit is the absolute Amp.

        // 弹道参数（按真实时间计，dB/秒），模拟商业 DAW 峰值表的物理表现：
        // 起音瞬时贴顶（峰值表特性），下降按固定速率平滑回落，柱体流畅不抖。
        const double BarReleaseDbPerSec = 48.0;   // 信号柱回落速率
        const double PeakHoldSeconds = 1.0;       // 峰值标记到顶后的保持时长
        const double PeakReleaseDbPerSec = 24.0;  // 保持结束后峰值标记的回落速率
        const double MaxFrameSeconds = 0.1;       // 单帧 dt 上限，防止停顿后跳变

        // 渐变分区阈值（dB）：绿色一直到 -12，过渡到黄色，到 0 dB 转红色
        const double WarnDb = -12.0;
        const double WarnPeakDb = -3.0;
        const double PeakDb = 0.0;

        double mMaxAmpValue = 6.0;
        double mMinAmpValue = -86.8;

        double mTargetDb;     // 最近推入的目标电平（衰减的目标，暂停时设为底）
        double mBarDb;        // 当前柱体显示电平（已应用弹道）
        double mPeakHoldDb;   // 峰值保持标记电平
        double mPeakHoldTimer;// 剩余保持时间（秒）

        // 独立动画时钟：弹道（回落/峰值保持）由它驱动，不依赖音频回调，
        // 这样暂停后柱体仍能平滑降到 0，而非瞬间清零。
        readonly System.Diagnostics.Stopwatch mClock = new();
        Avalonia.Threading.DispatcherTimer? mTimer;

        IBrush? mCachedBrush;
        double mCachedBrushHeight = -1;

        public AmplitudeViewer() {
            Width = 4;
            mTargetDb = mMinAmpValue;
            mBarDb = mMinAmpValue;
            mPeakHoldDb = mMinAmpValue;
        }

        public void SetRange(double min, double max)
        {
            mMaxAmpValue = max;
            mMinAmpValue = min;
            mCachedBrush = null;
            Reset();
        }

        private double Db2Amp(double db)
        {
            return Math.Pow(10, (db / 20));
        }

        // 将一个 dB 值映射到柱体高度比例（0=底，1=顶），dB 线性或绝对幅度线性。
        private double Percent(double value)
        {
            var minValue = LevelUnit ? Db2Amp(mMinAmpValue) : mMinAmpValue;
            var maxValue = LevelUnit ? Db2Amp(mMaxAmpValue) : mMaxAmpValue;
            var v = LevelUnit ? Db2Amp(value) : value;
            return ((v - minValue) / (maxValue - minValue)).Limit(0, 1);
        }

        // 绿→黄→红渐变笔刷，锚定到整条控件高度（不随填充高度漂移），按高度缓存。
        private IBrush MeterBrush(double height)
        {
            if (mCachedBrush != null && mCachedBrushHeight == height) return mCachedBrush;

            var brush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, height, RelativeUnit.Absolute), // 底部
                EndPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),        // 顶部
            };
            brush.GradientStops.Add(new GradientStop(Style.AMP_NORMAL, 0));
            brush.GradientStops.Add(new GradientStop(Style.AMP_NORMAL, Percent(WarnDb)));
            brush.GradientStops.Add(new GradientStop(Style.AMP_WARN, Percent(WarnPeakDb)));
            brush.GradientStops.Add(new GradientStop(Style.AMP_PEAK, Percent(PeakDb)));
            brush.GradientStops.Add(new GradientStop(Style.AMP_PEAK, 1));

            mCachedBrush = brush;
            mCachedBrushHeight = height;
            return brush;
        }

        public override void Render(DrawingContext context)
        {
            var r = this.Rect();
            context.FillRectangle(Style.BACK.ToBrush(), r);//Bg

            double level = Percent(mBarDb);
            if (level > 0)
            {
                var litRect = new Rect(r.Left, r.Height * (1.0 - level), r.Width, r.Height * level);
                context.FillRectangle(MeterBrush(r.Height), litRect);
            }

            // 峰值保持标记：削顶（≥0dB）时变红作为 clip 提示，否则用亮线。
            if (mPeakHoldDb > mMinAmpValue)
            {
                double y = r.Height * (1.0 - Percent(mPeakHoldDb));
                var peakColor = mPeakHoldDb >= PeakDb ? Style.AMP_PEAK : Style.LIGHT_WHITE;
                context.DrawLine(new Pen(peakColor.ToBrush()), new Avalonia.Point(r.Left, y), new Avalonia.Point(r.Right, y));
            }
        }

        // 立即清零（硬复位，如改量程/初始化）。
        public void Reset()
        {
            mTargetDb = mMinAmpValue;
            mBarDb = mMinAmpValue;
            mPeakHoldDb = mMinAmpValue;
            mPeakHoldTimer = 0;
            StopTimer();
            InvalidateVisual();
        }

        // 暂停/静音：把目标拉到底，动画时钟继续把柱体平滑降到 0 后自行停转。
        public void Release()
        {
            mTargetDb = mMinAmpValue;
            EnsureRunning();
        }

        // 推入一帧瞬时电平（来自音频回调）：瞬时起音 + 抬升峰值标记，回落交给时钟。
        public void SetAmplitude(double amp)
        {
            // 守护 -inf/NaN（静音时 amp=0 → dB=-inf），并夹到显示下限。
            double v = double.IsNaN(amp) ? mMinAmpValue : Math.Max(amp, mMinAmpValue);
            mTargetDb = v;

            if (v > mBarDb) mBarDb = v;                       // 瞬时起音
            if (mBarDb >= mPeakHoldDb)                        // 峰值标记随柱上升
            {
                mPeakHoldDb = mBarDb;
                mPeakHoldTimer = PeakHoldSeconds;
            }

            EnsureRunning();
            InvalidateVisual();
        }

        void EnsureRunning()
        {
            mTimer ??= CreateTimer();
            if (!mTimer.IsEnabled)
            {
                mClock.Restart();
                mTimer.Start();
            }
        }

        Avalonia.Threading.DispatcherTimer CreateTimer()
        {
            var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += OnTick;
            return timer;
        }

        void StopTimer()
        {
            mTimer?.Stop();
            mClock.Reset();
        }

        // 每帧按真实经过时间推进弹道：柱体平滑回落、峰值保持后回落；全部沉底即停转。
        void OnTick(object? sender, System.EventArgs e)
        {
            double dt = Math.Min(mClock.Elapsed.TotalSeconds, MaxFrameSeconds);
            mClock.Restart();

            if (mTargetDb > mBarDb)
                mBarDb = mTargetDb;                                   // 起音（兜底）
            else if (mBarDb > mTargetDb)
                mBarDb = Math.Max(mTargetDb, mBarDb - BarReleaseDbPerSec * dt);

            if (mBarDb >= mPeakHoldDb)
            {
                mPeakHoldDb = mBarDb;
                mPeakHoldTimer = PeakHoldSeconds;
            }
            else
            {
                mPeakHoldTimer -= dt;
                if (mPeakHoldTimer <= 0)
                    mPeakHoldDb = Math.Max(mBarDb, mPeakHoldDb - PeakReleaseDbPerSec * dt);
            }

            InvalidateVisual();

            if (mBarDb <= mMinAmpValue && mPeakHoldDb <= mMinAmpValue)
                StopTimer();
        }
    }

    internal class StereoAmplitudeViewer:DockPanel
    {
        AmplitudeViewer leftViewer = new AmplitudeViewer();
        AmplitudeViewer rightViewer = new AmplitudeViewer();

        public StereoAmplitudeViewer()
        {
            leftViewer.Margin = new Avalonia.Thickness(0, 0, 2, 0);
            rightViewer.Margin = new Avalonia.Thickness(0, 0, 0, 0);
            this.AddDock(leftViewer, Dock.Left);
            this.AddDock(rightViewer, Dock.Left);
        }
        public void SetRange(double min,double max)
        {
            leftViewer.SetRange(min, max);
            rightViewer.SetRange(min, max);
        }
        public void Reset()
        {
            leftViewer.Reset();
            rightViewer.Reset();
        }

        public void Release()
        {
            leftViewer.Release();
            rightViewer.Release();
        }

        public void SetValue(Tuple<double,double> Amplitude)
        {
            leftViewer.SetAmplitude(Amplitude.Item1);
            rightViewer.SetAmplitude(Amplitude.Item2);
        }
    }
}
