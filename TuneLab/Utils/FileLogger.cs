using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

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

    StreamWriter mStreamWriter;
}
