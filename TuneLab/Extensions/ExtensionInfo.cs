using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;
using TuneLab.Utils;

namespace TuneLab.Extensions;

internal class ExtensionInfo
{
    public string type { get; set; } = string.Empty;
    public string[] assemblies { get; set; } = [];
    public string[] platforms { get; set; } = [];

    public bool IsPlatformAvailable()
    {
        if (platforms.IsEmpty())
            return true;

        return platforms.Contains(PlatformHelper.GetOS()) | platforms.Contains(PlatformHelper.GetPlatform());
    }
}
