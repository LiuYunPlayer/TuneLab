using System.Runtime.InteropServices;

namespace TuneLab.Utils
{
    internal class PlatformHelper
    {
        public static string GetPlatform()
        {
            string os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
             RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos" :
             RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "unknown";

            string arch = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "x64" :
                           RuntimeInformation.ProcessArchitecture == Architecture.X86 ? "x86" :
                           RuntimeInformation.ProcessArchitecture == Architecture.Arm ? "arm" :
                           RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "unknown";

            string platform = $"{os}-{arch}";
            return platform;
        }
    }
}
