using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Utils;

internal class IDGenerator
{
    public long GenerateID()
    {
        return ++id;
    }

    long id = 0;
}
