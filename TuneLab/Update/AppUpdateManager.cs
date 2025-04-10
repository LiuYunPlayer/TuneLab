using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Net.Http;
using TuneLab.Base.Utils;
using TuneLab.Utils;
using SystemHttpClient = System.Net.Http.HttpClient;
using System.Runtime.InteropServices;

namespace TuneLab.Update;

internal static class AppUpdateManager
{
    private static readonly string storageFile = Path.Combine(PathManager.ConfigsFolder, "UpdateIgnoreVersion.txt");

    public static async Task<UpdateInfo> CheckForUpdate(bool ignoreVersion = true)
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

        var queryParams = new Dictionary<string, object>
            {
                { "platform", PlatformHelper.GetPlatform() }
            };

        var response = await new Base.Utils.HttpClient("https://api.tunelab.app").GetAsync("/api/app/v2/get-update", queryParams);

        if (!response.IsSuccessful)
        {
            throw new Exception(response.ErrorMessage);
        }

        var data = Newtonsoft.Json.JsonConvert.DeserializeObject<UpdateInfo>(response.Content);

        if (data == null)
        {
            throw new Exception("CheckUpdateFailed");
        }

        if (data.version <= AppInfo.Version)
        {
            return null;
        }

        // read ignore version
        string mIgnoredVersion = File.ReadAllText(storageFile);
        if (Version.TryParse(mIgnoredVersion, out var ignoredVersion) && ignoredVersion == data.version && ignoreVersion)
        {
            return null;
        }

        return data;
    }

    public static async void UpdateBackground(string url)
    {
        try
        {
            // 获取临时文件夹路径，并创建 TuneLabUpdate 子文件夹
            string tempFolder = Path.GetTempPath();
            string updateFolder = Path.Combine(tempFolder, "TuneLabUpdate");

            if (!Directory.Exists(updateFolder))
            {
                Directory.CreateDirectory(updateFolder);
            }

            // 从 URL 中提取文件名
            string fileName = Path.GetFileName(new Uri(url).LocalPath);
            string destinationFilePath = Path.Combine(updateFolder, fileName);

            // 下载文件
            using (SystemHttpClient client = new SystemHttpClient())
            {
                Log.Info($"Downloading file from {url}...");
                byte[] fileData = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(destinationFilePath, fileData);
                Log.Info($"File downloaded to {destinationFilePath}");
            }

            string updaterName = "Updater";
            Func<string, bool> Exists;
            Action<string, string, bool> Copy;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                updaterName += ".exe";
                Exists = File.Exists;
                Copy = File.Copy;
            }
            else
            {
                Exists = Directory.Exists;
                Copy = FileUtils.CopyDirectory;
            }

            // 获取当前应用程序目录
            string appDirectory = PathManager.ExcutableFolder;
            string updaterPath = Path.Combine(appDirectory, updaterName);
            string updaterDestinationPath = Path.Combine(updateFolder, updaterName);

            // 检查 Updater 是否存在
            if (Exists(updaterPath))
            {
                // 复制 Updater 到 TuneLabUpdate 文件夹
                Copy(updaterPath, updaterDestinationPath, true); // overwrite 如果目标文件已存在
                Log.Info($"Copied Updater to {updaterDestinationPath}");
            }
            else
            {
                Log.Info("Updater not found in the application directory.");
            }

            ProcessHelper.CreateProcess(updaterDestinationPath, [
                "-restart",
                $"-input{destinationFilePath}",
                $"-output{appDirectory}",
            ]);
        }
        catch (Exception ex)
        {
            Log.Error($"An error occurred: {ex.Message}");
        }
    }

    public static void SaveIgnoreVersion(Version version)
    {
        File.WriteAllText(storageFile, version.ToString());
    }
}
