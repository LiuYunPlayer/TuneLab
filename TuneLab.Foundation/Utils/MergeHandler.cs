using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Foundation.Utils;

public class MergeHandler(Action action)
{
    public static implicit operator Action(MergeHandler mergeHandler)
    {
        return mergeHandler.Trigger;
    }

    public bool IsMerging => mMergingFlag > 0;

    public void Begin()
    {
        mMergingFlag++;
    }

    public void End()
    {
        if (!IsMerging)
            throw new Exception("End Merge without Begin!");

        mMergingFlag--;

        if (IsMerging)
            return;

        if (mRequestCount == 0)
            return;

        action();
        mRequestCount = 0;
    }

    public void Trigger()
    {
        if (IsMerging)
        {
            mRequestCount++;
            return;
        }

        action();
    }

    int mMergingFlag = 0;
    int mRequestCount = 0;
}
