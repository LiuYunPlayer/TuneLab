using System.Runtime.InteropServices;

namespace TuneLab.Utils
{
    internal class PlatformHelper
    {
        public static string GetPlatform()
        {
            return $"{GetOS()}-{GetArchitecture()}";
        }

        public static string GetOS()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "win";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "osx";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "linux";

            return  "unknown";
        }

        public static string GetArchitecture()
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => "x86",
                Architecture.X64 => "x64",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "arm64",
                _ => "unknown"
            };
        }
    }
}
