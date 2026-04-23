using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace ExtensionInstaller;

internal class Program
{
    static void Main(string[] args)
    {
        try
        {
            string lockFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuneLab", "TuneLab.lock");
            if (File.Exists(lockFilePath))
            {
                Console.WriteLine("Waiting for TuneLab to exit...");
                do
                {
                    Thread.Sleep(1000);
                }
                while (File.Exists(lockFilePath));
            }

            bool restart = false;
            var extensionFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuneLab", "Extensions");

            // Parse args into install (zip) paths and uninstall (directory) paths
            List<string> installPaths = new();
            List<string> uninstallPaths = new();
            var mode = ArgMode.Install;

            foreach (var arg in args)
            {
                if (arg == "-restart")
                {
                    restart = true;
                    continue;
                }

                if (arg == "-uninstall")
                {
                    mode = ArgMode.Uninstall;
                    continue;
                }

                if (arg == "-install")
                {
                    mode = ArgMode.Install;
                    continue;
                }

                switch (mode)
                {
                    case ArgMode.Uninstall:
                        uninstallPaths.Add(arg);
                        break;
                    default:
                        installPaths.Add(arg);
                        break;
                }
            }

            // Process uninstalls
            foreach (var dir in uninstallPaths)
            {
                var name = Path.GetFileName(dir);
                Console.WriteLine("Uninstalling " + name + "...");
                if (Directory.Exists(dir))
                {
                    while (true)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            break;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Failed to delete directory: " + ex.ToString());
                            Console.WriteLine("Try again...");
                            Thread.Sleep(1000);
                        }
                    }
                }
                Console.WriteLine(name + " has been successfully uninstalled!\n");
            }

            // Process installs
            foreach (var zipPath in installPaths)
            {
                var name = Path.GetFileNameWithoutExtension(zipPath);
                var entry = ZipFile.OpenRead(zipPath).GetEntry("description.json");
                if (entry != null)
                {
                    var description = JsonSerializer.Deserialize<Description>(entry.Open());
                    if (!string.IsNullOrEmpty(description.name))
                        name = description.name;
                }
                var dir = Path.Combine(extensionFolder, name);

                Console.WriteLine("Uninstalling " + name + "...");
                if (Directory.Exists(dir))
                {
                    while (true)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            break;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Failed to delete file: " + ex.ToString());
                            Console.WriteLine("Try again...");
                            Thread.Sleep(1000);
                        }
                    }
                }

                Console.WriteLine("Installing " + name + "...");

                ZipFileHelper.ExtractToDirectory(zipPath, dir);
                Console.WriteLine(name + " has been successfully installed!\n");
            }

            if (restart) Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TuneLab.exe"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Installation failed: " + ex.ToString());
            while (true) Console.ReadLine();
        }
    }

    enum ArgMode
    {
        Install,
        Uninstall,
    }

    struct Description
    {
        public string name { get; set; }
    }
}
