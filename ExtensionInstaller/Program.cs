using System.Diagnostics;
using System.IO.Compression;

namespace ExtensionInstaller;

internal class Program
{
    static void Main(string[] args)
    {
        if (args.Length < 2)
            return;

        var processID = args[0];
        var process = Process.GetProcessById(int.Parse(processID));
        var processFileName = process.MainModule!.FileName;
        var tlxs = args.Skip(1);

        process.CloseMainWindow();
        process.WaitForExit();

        var extensionFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuneLab", "Extensions");
        foreach (var filePath in tlxs)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            var dir = Path.Combine(extensionFolder, name);

            if (Directory.Exists(dir))
                Directory.Delete(dir, true);

            ZipFile.ExtractToDirectory(filePath, dir, true);
        }

        Process.Start(processFileName, tlxs);
    }
}
