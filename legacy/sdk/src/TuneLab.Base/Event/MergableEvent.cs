using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

namespace TuneLab.Base.Event;

public class MergableEvent : IMergableEvent
{
    public static implicit operator Action(MergableEvent e) => e.Invoke;

    public MergableEvent()
    {
        mMergeHandler = new(() => { Action?.Invoke(); });
    }

    public void Subscribe(Action action)
    {
        Action += action;
    }

    public void Unsubscribe(Action action)
    {
        Action -= action;
    }

    public void BeginMerge()
    {
        mMergeHandler.Begin();
    }

    public void EndMerge()
    {
        mMergeHandler.End();
    }

    public void Invoke()
    {
        mMergeHandler.Trigger();
    }

    event Action? Action;

    readonly MergeHandler mMergeHandler;
}
