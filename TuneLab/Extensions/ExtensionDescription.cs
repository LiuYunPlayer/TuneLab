using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;
using TuneLab.Utils;

namespace TuneLab.Extensions;

internal class ExtensionDescription : ExtensionInfo
{
    public required string name { get; set; }
    public string version { get; set; } = "1.0.0";
    public string author { get; set; } = string.Empty;
    public string introduction { get; set; } = string.Empty;
    public ExtensionInfo[] extensions { get; set; } = [];
}
