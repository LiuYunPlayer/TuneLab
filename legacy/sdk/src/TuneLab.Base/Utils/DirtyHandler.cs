using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Base.Utils;

public class DirtyHandler
{
    public event Action? OnDirty;
    public event Action? OnReset;
    public bool IsDirty { get; private set; }
    public void SetDirty()
    {
        if (IsDirty)
            return;

        IsDirty = true;
        OnDirty?.Invoke();
    }

    public void Reset()
    {
        if (!IsDirty)
            return;

        OnReset?.Invoke();
        IsDirty = false;
    }
}
