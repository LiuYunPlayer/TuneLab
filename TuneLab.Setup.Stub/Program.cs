using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace TuneLab.Setup.Stub;

// 极小外层解压器（无 Avalonia）：从自身尾部读 SFX 载荷 → 解压到临时目录 → 运行向导 → 清理。
// 与 pack-installer.ps1 写入的 footer 约定一致：末尾 24 字节 = magic(8: "TLSFX1\0\0") + offset(8,LE) + length(8,LE)。
internal static class Program
{
    static readonly byte[] Magic = Encoding.ASCII.GetBytes("TLSFX1\0\0");
    const int FooterSize = 24;

    [STAThread]
    static int Main(string[] args)
    {
        string? tempDir = null;
        try
        {
            string exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot resolve installer path.");

            byte[] payload = ReadAppendedPayload(exe);

            tempDir = Path.Combine(Path.GetTempPath(), "TuneLab.Setup." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            using (var ms = new MemoryStream(payload))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                zip.ExtractToDirectory(tempDir, overwriteFiles: true);

            string wizard = Path.Combine(tempDir, "TuneLab.Setup.exe");
            if (!File.Exists(wizard))
                throw new FileNotFoundException("Wizard executable missing in payload.", wizard);

            // 透传参数给向导（如 -silent/-dir 等），等其退出。
            var psi = new ProcessStartInfo(wizard) { UseShellExecute = false, WorkingDirectory = tempDir };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode ?? 0;
        }
        catch (Exception ex)
        {
            MessageBox(IntPtr.Zero, ex.Message, "TuneLab Setup", 0x10 /*MB_ICONERROR*/);
            return 1;
        }
        finally
        {
            if (tempDir != null)
                try { Directory.Delete(tempDir, true); } catch { /* best-effort */ }
        }
    }

    static byte[] ReadAppendedPayload(string exePath)
    {
        using var fs = File.OpenRead(exePath);
        if (fs.Length < FooterSize)
            throw new InvalidOperationException("Installer payload not found (file too small).");

        fs.Seek(-FooterSize, SeekOrigin.End);
        Span<byte> footer = stackalloc byte[FooterSize];
        fs.ReadExactly(footer);

        for (int i = 0; i < Magic.Length; i++)
            if (footer[i] != Magic[i])
                throw new InvalidOperationException("Installer payload not found (bad signature).");

        long offset = BitConverter.ToInt64(footer.Slice(8, 8));
        long length = BitConverter.ToInt64(footer.Slice(16, 8));
        if (offset < 0 || length <= 0 || offset + length > fs.Length - FooterSize)
            throw new InvalidOperationException("Installer payload not found (invalid footer).");

        var buf = new byte[length];
        fs.Seek(offset, SeekOrigin.Begin);
        fs.ReadExactly(buf, 0, (int)length);
        return buf;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
