using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using TuneLab.Foundation.Utils;
using Timer = System.Timers.Timer;

namespace TuneLab.Animation;

internal class AnimationManager : IDisposable
{
    public static readonly AnimationManager SharedManager = new();
    public double FrameInterval { get => mFrameInterval; set { mFrameInterval = value; if (mTimer != null) mTimer.Interval = value; } }

    public void Init()
    {
        var context = SynchronizationContext.Current;
        if (context == null)
        {
            Log.Warning("AnimationManager init failed!");
            return;
        }

        mUpdateTimes = new();
        for (int i = 0; i < mAverageFrameCount; i++)
        {
            mUpdateTimes.Enqueue(0);
        }

        mStopwatch.Start();
        mTimer = new();
        mTimer.Interval = mFrameInterval;
        mTimer.Elapsed += (s, e) =>
        {
            context.Post(_ => { Update(); }, null);
        };
        mTimer.Start();
    }

    public void Dispose()
    {
        if (mTimer == null)
            return;

        mTimer.Dispose();
        mTimer = null;
        mUpdateTimes = null;
    }

    public void StartAnimation(IAnimation animation)
    {
        lock (mAnimations)
        {
            if (mAnimations.ContainsKey(animation))
                return;

            mAnimations.Add(animation, Now);
        }
    }

    public void StopAnimation(IAnimation animation)
    {
        lock (mAnimations)
        {
            if (!mCanRemove)
            {
                mNeedRemoveAnimations.Add(animation);
                return;
            }

            RemoveAnimation(animation);
        }
    }

    void RemoveAnimation(IAnimation animation)
    {
        if (!mAnimations.ContainsKey(animation))
            return;

        mAnimations.Remove(animation);
    }

    const int mAverageFrameCount = 30;
    Queue<double>? mUpdateTimes;
    double mLastUpdateSec = 0;
    int mLastSecUpdateCount = 0;

    void Update()
    {
        lock (mAnimations)
        {
            double now = Now;

            /*if (mUpdateTimes != null)
            {
                int fps = (int)(1000 * mAverageFrameCount / Math.Max(now - mUpdateTimes.Dequeue(), 1));
                mUpdateTimes.Enqueue(now);
                var charArray = new char[fps];
                charArray.Fill('█');
                //Log.Debug("FPS: " + new string(charArray) + fps);
            }*/
            int sec = (int)(now / 1000);
            if (sec == mLastUpdateSec)
                mLastSecUpdateCount++;
            else
            {
                int fps = mLastSecUpdateCount;
                var charArray = new char[fps];
                charArray.Fill('█');
                //Log.Debug("FPS: " + new string(charArray) + fps);
                mLastSecUpdateCount = 0;
                mLastUpdateSec = sec;
            }

            ForbidRemove();

            foreach (var animation in mAnimations)
            {
                animation.Key.Update(now - animation.Value);
            }

            AllowRemove();
        }
    }

    void ForbidRemove()
    {
        mCanRemove = false;
    }

    void AllowRemove()
    {
        mCanRemove = true;

        foreach (var animation in mNeedRemoveAnimations)
        {
            RemoveAnimation(animation);
        }

        mNeedRemoveAnimations.Clear();
    }

    double Now => mStopwatch.ElapsedMilliseconds;

    Timer? mTimer;
    readonly Stopwatch mStopwatch = new();
    readonly Dictionary<IAnimation, double> mAnimations = new();

    double mFrameInterval = 16;

    bool mCanRemove = true;
    readonly HashSet<IAnimation> mNeedRemoveAnimations = new();
}
