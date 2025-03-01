using System;
using System.IO;

namespace TuneLab.Utils;

internal class LockFile : IDisposable
{
    public static LockFile? Create(string path)
    {
        try
        {
            PathManager.MakeSureExist(Path.GetDirectoryName(path)!);
            return new LockFile(File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        var path = mFileStream.Name;
        mFileStream.Close();
        mFileStream.Dispose();
        File.Delete(path);
    }

    LockFile(FileStream fileStream)
    {
        mFileStream = fileStream;
    }

    FileStream mFileStream;
}
