using System;
using System.IO;
using TuneLab.Foundation.Utils;

namespace TuneLab.Utils;

internal class FileLogger : ILogger
{
    public FileLogger(string path)
    {
        PathManager.MakeSureExist(Path.GetDirectoryName(path)!);
        mStreamWriter = new StreamWriter(path);
    }

    ~FileLogger()
    {
        mStreamWriter.Close();
    }

    public void WriteLine(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        Console.WriteLine(message);
        mStreamWriter.WriteLine(message);
        mStreamWriter.Flush();
    }

    readonly StreamWriter mStreamWriter;
}
