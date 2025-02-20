using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Utils;

internal static class FileUtils
{
    /// <summary>
    /// 递归复制文件夹及其内容
    /// </summary>
    /// <param name="sourceDir">源文件夹路径</param>
    /// <param name="destinationDir">目标文件夹路径</param>
    /// <param name="overwrite">是否覆盖</param>
    public static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite = false)
    {
        if (overwrite && Directory.Exists(destinationDir))
        {
            Directory.Delete(destinationDir, true);
        }

        // 创建目标文件夹
        if (!Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        // 复制所有文件
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            string destFile = Path.Combine(destinationDir, fileName);
            File.Copy(file, destFile, overwrite: true);
        }

        // 递归复制子文件夹
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string dirName = Path.GetFileName(subDir);
            string destSubDir = Path.Combine(destinationDir, dirName);
            CopyDirectory(subDir, destSubDir);
        }
    }
}
