using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Configs;
using TuneLab.Data;
using TuneLab.GUI;

namespace TuneLab.UI;

internal static class TrackColorManager
{
    public static double HueChangeRate
    { 
        get => mHueChangeRate; 
        set
        {
            if (mHueChangeRate == value) 
                return; 

            mHueChangeRate = value;
            if (mHueChangeRate == 0) 
                timer.Stop(); 
            else 
                timer.Start();
        }
    }

    public static Color GetColor(this ITrack track)
    {
        var color = GetFixedColor(track);
        var hsv = color.ToHsv();
        var newHSV = new HsvColor(hsv.A, (hsv.H + offset) % 360, hsv.S, hsv.V);
        return newHSV.ToRgb();
    }

    public static Color GetFixedColor(this ITrack track)
    {
        if (Color.TryParse(track.Color.Value, out var color))
            return color;

        return Style.DefaultTrackColor;
    }

    public static void RegisterOnTrackColorUpdated(this Avalonia.Visual visual, Action? action = null)
    {
        var context = SynchronizationContext.Current;
        if (context == null)
            return;

        timer.Elapsed += (s, e) => { context.Post(_ => { visual.InvalidateVisual(); action?.Invoke(); }, null); };
    }

    static TrackColorManager()
    {
        timer.Elapsed += (s, e) =>
        { 
            offset += HueChangeRate / 1000 * timer.Interval; 
            offset %= 360;
            if (offset < 0) 
                offset += 360;
        };
        HueChangeRate = Settings.TrackHueChangeRate;
        Settings.TrackHueChangeRate.Modified.Subscribe(() => HueChangeRate = Settings.TrackHueChangeRate);
    }

    static System.Timers.Timer timer = new System.Timers.Timer() { Interval = 16.5 };
    static double offset = 0;
    static double mHueChangeRate = 0;
}
