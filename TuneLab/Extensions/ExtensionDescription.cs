using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

namespace TuneLab.Extensions;

internal class ExtensionDescription
{
    public required string name { get; set; }
    public string version { get; set; } = "1.0.0";
    public string[] assemblies { get; set; } = [];
    public string[] platforms { get; set; } = [];

    public bool IsPlatformAvailable()
    {
        if (platforms.IsEmpty())
            return true;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (platforms.Contains("osx"))
                return true;

            return IsArchitectureAvailable("osx");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (platforms.Contains("win"))
                return true;

            return IsArchitectureAvailable("win");
        }
        else
        {
            return false;
        }
    }

    bool IsArchitectureAvailable(string os)
    {
        switch (RuntimeInformation.ProcessArchitecture)
        {
            case Architecture.X64:
                return platforms.Contains(os + "-x64");
            case Architecture.Arm64:
                return platforms.Contains(os + "-arm64");
            case Architecture.X86:
                return platforms.Contains(os + "-x86");
            case Architecture.Arm:
                return platforms.Contains(os + "-arm32");
            default:
                return false;
        }
    }
}
