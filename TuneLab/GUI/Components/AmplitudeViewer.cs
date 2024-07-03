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
using TuneLab.Base.Science;
using TuneLab.Base.Structures;
using TuneLab.Base.Utils;
using TuneLab.GUI.Input;
using TuneLab.Utils;
using TuneLab.Views;

namespace TuneLab.GUI.Components
{
    internal class AmplitudeViewer : Control
    {
        bool LevelUnit = false;//Level Unit is the absolute Amp.

        int mDelaySampleCount = 50;
        List<double> mAmplitudeDelay = new List<double>();

        double mMaxAmpValue = 6.0;
        double mMinAmpValue = -86.8;

        double mAmpValue;
        double mDelayAmpValue;
        public AmplitudeViewer() {
            Width = 4;
            Reset();
        }

        public void SetRange(double min, double max)
        {
            mMaxAmpValue = max;
            mMinAmpValue = min;
            Reset();
        }

        private double Db2Amp(double db)
        {
            return Math.Pow(10, (db / 20));
        }
        private Rect AmpRect(double Amplitude)
        {
            var minValue = LevelUnit ? Db2Amp(mMinAmpValue) : mMinAmpValue;
            var maxValue = LevelUnit ? Db2Amp(mMaxAmpValue) : mMaxAmpValue;
            var ampValue = LevelUnit ? Db2Amp(Amplitude) : Amplitude;
            double percent = ((ampValue - minValue) / (maxValue - minValue)).Limit(0, 1);
            Rect r = this.Rect();
            return new Rect(
                r.Left, r.Height * (1.0 - percent),
                r.Width, r.Height * percent
                );
        }
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Style.BACK.ToBrush(), this.Rect());//Bg

            Rect delayRect = AmpRect(mDelayAmpValue);
            Rect ampRect = AmpRect(mAmpValue);
            context.FillRectangle(Style.AMP_DELAY.ToBrush(), delayRect);

            context.FillRectangle(Style.AMP_NORMAL.ToBrush(), ampRect);

            if (delayRect.Height > 0) context.DrawLine(new Pen(Style.AMP_NORMAL.ToBrush()), delayRect.TopLeft, delayRect.TopRight);
        }

        public void Reset()
        {
            mAmplitudeDelay.Clear();
            SetAmplitude(mMinAmpValue);
        }

        public void SetAmplitude(double amp)
        {
            mAmpValue = amp;
            mAmplitudeDelay.Add(mAmpValue);
            while (mAmplitudeDelay.Count > mDelaySampleCount) mAmplitudeDelay.RemoveAt(0);
            mDelayAmpValue = mAmplitudeDelay.Max();
            RefreshUI();
        }

        void RefreshUI()
        {
            InvalidateArrange();
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

        public void SetValue(Tuple<double,double> Amplitude)
        {
            leftViewer.SetAmplitude(Amplitude.Item1);
            rightViewer.SetAmplitude(Amplitude.Item2);
        }
    }
}
