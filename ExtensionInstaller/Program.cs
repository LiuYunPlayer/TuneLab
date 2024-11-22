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
            foreach (var arg in args)
            {
                if (arg == "-restart")
                {
                    restart = true;
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(arg);
                var entry = ZipFile.OpenRead(arg).GetEntry("description.json");
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

                ZipFileHelper.ExtractToDirectory(arg, dir);
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

    struct Description
    {
        public string name { get; set; }
    }
}
