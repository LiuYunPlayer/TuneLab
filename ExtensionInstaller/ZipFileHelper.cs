using System.IO.Compression;

namespace ExtensionInstaller
{
    internal class ZipFileHelper
    {
        public static void ExtractToDirectory(string sourceArchiveFileName, string destinationDirectoryName)
        {
            bool overflow = true;
            using (var zip = ZipFile.Open(sourceArchiveFileName, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    string fullName = entry.FullName;
                    string name = entry.Name;
                    if (fullName.EndsWith("/") || fullName.EndsWith("\\"))
                    {
                        string splitChar = fullName.Last() + "";
                        string[] splitNames = fullName.Split(splitChar).Where(d => d.Length > 0).ToArray();
                        string targetDir = Path.Combine(destinationDirectoryName, Path.Combine(splitNames));
                        if (!Directory.Exists(targetDir)) { Directory.CreateDirectory(targetDir); }
                    }
                    else if (fullName == name)
                    {
                        string targetFile = Path.Combine(destinationDirectoryName, fullName);
                        string targetDir = Path.GetDirectoryName(targetFile);
                        if (!Directory.Exists(targetDir)) { Directory.CreateDirectory(targetDir); }
                        entry.ExtractToFile(targetFile, overflow);
                    }
                    else if (fullName.Length > name.Length)
                    {
                        string splitChar = fullName.Substring(fullName.Length - name.Length - 1, 1);
                        string[] splitNames = fullName.Split(splitChar).Where(d => d.Length > 0).ToArray();
                        string targetFile = Path.Combine(destinationDirectoryName, Path.Combine(splitNames));
                        string targetDir = Path.GetDirectoryName(targetFile);
                        if (!Directory.Exists(targetDir)) { Directory.CreateDirectory(targetDir); }
                        entry.ExtractToFile(targetFile, overflow);
                    }
                };
            }
        }
    }
}
