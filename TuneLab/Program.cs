using System;
using System.IO;
using Avalonia;
using Avalonia.ReactiveUI;

namespace TuneLab;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.SvgImageExtension).Assembly);
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
    }


    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Log the exception to a file
        LogExceptionToFile((Exception)e.ExceptionObject);
    }

    private static void LogExceptionToFile(Exception exception)
    {
        string logFileName = $"Error_Log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log";
        var logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TuneLab", "Logs");
        if (!Directory.Exists(logFilePath))
        {
            Directory.CreateDirectory(logFilePath);
        }
        var logFilePathAndName = Path.Combine(logFilePath, logFileName);

        try
        {
            using (StreamWriter writer = File.AppendText(logFilePathAndName))
            {
                writer.WriteLine($"[{DateTime.Now}] Exception: {exception.Message}");
                writer.WriteLine($"Stack Trace: {exception.StackTrace}");
                writer.WriteLine();
            }
        }
        catch (Exception ex)
        {
            // If logging to file fails, you might want to log it elsewhere or handle it differently
            Console.WriteLine($"Error logging exception: {ex.Message}");
        }
    }
}
