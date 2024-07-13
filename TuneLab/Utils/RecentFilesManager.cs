using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TuneLab.Base.Utils;

namespace TuneLab.Utils;

internal static class RecentFilesManager
{
    private const int MaxFiles = 20;
    private static readonly string storageFile = Path.Combine(PathManager.ConfigsFolder, "RecentFiles.txt");
    public static event EventHandler? RecentFilesChanged;

    public static void Init()
    {
        if (!Path.Exists(PathManager.ConfigsFolder))
        {
            try
            {
                Directory.CreateDirectory(PathManager.ConfigsFolder);
            }
            catch
            {
                Log.Error($"Not able to create config folder: {PathManager.ConfigsFolder}");
            }
        }

        // 确保存储文件存在
        if (!File.Exists(storageFile))
        {
            try
            {
                File.Create(storageFile).Close();
            }
            catch (Exception ex)
            {
                Log.Error($"Not able to create storage file: {ex.Message}");
            }
        }
    }

    public static void AddFile(string filePath)
    {
        var recentFiles = GetRecentFiles();

        // 查找已存在的文件路径
        var existingFileIndex = recentFiles.FindIndex(f => f.FilePath == filePath);

        if (existingFileIndex != -1)
        {
            // 如果存在，将其移动到开头
            var existingFile = recentFiles[existingFileIndex];
            recentFiles.RemoveAt(existingFileIndex);
            recentFiles.Insert(0, existingFile);
        }
        else
        {
            // 如果不存在，添加新文件路径到开头
            recentFiles.Insert(0, new FileRecord
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath
            });
        }

        // 限制列表大小为MaxFiles
        if (recentFiles.Count > MaxFiles)
        {
            recentFiles.RemoveRange(MaxFiles, recentFiles.Count - MaxFiles);
        }

        SaveRecentFiles(recentFiles);

        RecentFilesChanged?.Invoke(null, EventArgs.Empty);
    }

    public static List<FileRecord> GetRecentFiles()
    {
        var recentFiles = new List<FileRecord>();

        if (File.Exists(storageFile))
        {
            try
            {
                var lines = File.ReadAllLines(storageFile);
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        recentFiles.Add(new FileRecord
                        {
                            FileName = Path.Combine(Path.GetFileName(Path.GetDirectoryName(line)), Path.GetFileName(line)),
                            FilePath = line
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Read storage file error: {ex.Message}");
            }
        }

        return recentFiles;
    }

    private static void SaveRecentFiles(List<FileRecord> recentFiles)
    {
        try
        {
            var lines = recentFiles.Select(f => f.FilePath).ToArray();
            File.WriteAllLines(storageFile, lines);
        }
        catch (Exception ex)
        {
            Log.Error($"Write storage file error: {ex.Message}");
        }
    }
}

public class FileRecord
{
    public string FileName { get; set; }
    public string FilePath { get; set; }
}