using System.Diagnostics;

namespace Updater;

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
            string? input = null;
            string? output = null;
            foreach (var arg in args)
            {
                if (arg == "-restart")
                {
                    restart = true;
                    continue;
                }
                else if (arg.StartsWith("-input"))
                {
                    input = arg.Substring(6);
                }
                else if (arg.StartsWith("-output"))
                {
                    output = arg.Substring(7);
                }
            }

            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
            {
                return;
            }
            
            Console.WriteLine("Uninstalling TuneLab...");
            if (Directory.Exists(output))
            {
                while (true)
                {
                    try
                    {
                        Directory.Delete(output, true);
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

            Console.WriteLine("Installing TuneLab...");

            ZipFileHelper.ExtractToDirectory(input, output);
            Console.WriteLine("TuneLab has been successfully installed!\n");

            if (restart) Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TuneLab"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to update: " + ex.ToString());
            while (true) Console.ReadLine();
        }
    }

    struct Description
    {
        public string name { get; set; }
    }
}
