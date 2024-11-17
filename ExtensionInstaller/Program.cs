using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

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
                } while (File.Exists(lockFilePath));
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
                using (var archive = ArchiveFactory.Open(arg))
                {
                    var entry = archive.Entries.FirstOrDefault(e => e.Key.EndsWith("description.json", StringComparison.OrdinalIgnoreCase));
                    if (entry != null)
                    {
                        using (var stream = entry.OpenEntryStream())
                        {
                            var description = JsonSerializer.Deserialize<Description>(stream);
                            if (!string.IsNullOrEmpty(description.name))
                                name = description.name;
                        }
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
                                Console.WriteLine("Failed to delete directory: " + ex);
                                Console.WriteLine("Try again...");
                                Thread.Sleep(1000);
                            }
                        }
                    }

                    Console.WriteLine("Installing " + name + "...");

                    foreach (var archiveEntry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        // Extract entries
                        archiveEntry.WriteToDirectory(dir, new ExtractionOptions
                        {
                            ExtractFullPath = true,
                            Overwrite = true
                        });
                    }

                    Console.WriteLine(name + " has been successfully installed!\n");
                }
            }

            if (restart) Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TuneLab.exe"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Installation failed: " + ex);
            while (true) Console.ReadLine();
        }
    }

    struct Description
    {
        public string name { get; set; }
    }
}
