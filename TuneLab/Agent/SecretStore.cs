using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using TuneLab.Foundation;

namespace TuneLab.Agent;

// 跨平台密钥保护。各平台用其原生机制（均按当前用户会话保护，无需自管主密钥）：
//   Windows : DPAPI（ProtectedData）— 内联加密成 blob，存进配置文件本身。
//   macOS   : Keychain，经 `security` CLI 存取。
//   Linux   : Secret Service（GNOME Keyring / KWallet），经 libsecret 的 `secret-tool` CLI 存取。
// mac/linux 是"存进 OS 凭据库、文件只留引用"；故对外暴露两套：DPAPI 内联(blob) 与 OS 外部库(account 键)。
// 任一不可用（如 Linux 未装 libsecret-tools、headless）时调用方降级为明文并告警。
internal static class SecretStore
{
    const string ServiceName = "TuneLab";

    // ── Windows DPAPI（内联）──
    public static string DpapiProtect(string plaintext)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string DpapiUnprotect(string blob)
    {
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(blob), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty; // 换机/换用户 → 解不开，当作空让用户重填
        }
    }

    // ── macOS / Linux 外部凭据库 ──

    public static bool OsStore(string account, string secret)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                return Run("security", ["add-generic-password", "-U", "-a", account, "-s", ServiceName, "-w", secret]).code == 0;
            if (OperatingSystem.IsLinux())
                return Run("secret-tool", ["store", "--label=TuneLab API Key", "service", ServiceName, "account", account], secret).code == 0;
        }
        catch (Exception ex)
        {
            Log.Warning("OS secret store failed: " + ex.Message);
        }
        return false;
    }

    public static string? OsRetrieve(string account)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var (code, output) = Run("security", ["find-generic-password", "-a", account, "-s", ServiceName, "-w"]);
                return code == 0 ? output.TrimEnd('\n', '\r') : null;
            }
            if (OperatingSystem.IsLinux())
            {
                var (code, output) = Run("secret-tool", ["lookup", "service", ServiceName, "account", account]);
                return code == 0 && output.Length > 0 ? output : null;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("OS secret retrieve failed: " + ex.Message);
        }
        return null;
    }

    public static void OsDelete(string account)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
                Run("security", ["delete-generic-password", "-a", account, "-s", ServiceName]);
            else if (OperatingSystem.IsLinux())
                Run("secret-tool", ["clear", "service", ServiceName, "account", account]);
        }
        catch (Exception ex)
        {
            Log.Warning("OS secret delete failed: " + ex.Message);
        }
    }

    // 跑一个 CLI：参数走 ArgumentList（免引号转义；secret 作单独 arg 不经 shell）。secret-tool 经 stdin 收密钥。
    static (int code, string output) Run(string file, string[] args, string? stdin = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi);
        if (process == null)
            return (-1, string.Empty);

        if (stdin != null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
