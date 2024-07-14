using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using TuneLab.Base.Utils;

namespace TuneLab.Utils;

public class UpdateInfo
{
    public Version? version;
    public string? url;
    public string? description;
    public DateTime publishedAt;
}

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

        var response = await new HttpClient("https://api.tunelab.app").GetAsync("/api/app/get-update", queryParams);

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

    public static void SaveIgnoreVersion(Version version)
    {
        File.WriteAllText(storageFile, version.ToString());
    }
}
