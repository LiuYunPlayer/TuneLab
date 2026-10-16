using System;
using System.IO;
using System.Text;

namespace TuneLab.Extensions.Scripted;

// LSP 式帧：`Content-Length: <n>\r\n\r\n` + UTF-8 JSON 体（设计文档 §5.1）。
// 单读线程用 TryRead；写侧套锁（SendNotification 与 request 应答可并发）。
internal static class WireFraming
{
    public const long MaxFrameBytes = 512L * 1024 * 1024;

    public static bool TryRead(Stream input, out string json)
    {
        json = string.Empty;
        long length = -1;
        // 头段：逐行读到空行为止；只认 Content-Length，其余头忽略（前向兼容）。
        while (true)
        {
            var line = ReadLine(input);
            if (line == null)
                return false;                       // EOF（宿主死/管道关）
            if (line.Length == 0)
                break;                              // 空行 = 头结束
            var idx = line.IndexOf(':');
            if (idx <= 0)
                continue;
            if (line[..idx].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(line[(idx + 1)..].Trim(), out var parsed) && parsed >= 0)
                length = parsed;
        }
        if (length < 0)
            return false;
        if (length > MaxFrameBytes)
            throw new InvalidDataException(string.Format("frame too large: {0} bytes", length));
        var body = new byte[(int)length];
        var done = 0;
        while (done < body.Length)
        {
            var read = input.Read(body, done, body.Length - done);
            if (read <= 0)
                return false;
            done += read;
        }
        json = Encoding.UTF8.GetString(body);
        return true;
    }

    public static void Write(Stream output, string json)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes(string.Format("Content-Length: {0}\r\n\r\n", body.Length));
        output.Write(header, 0, header.Length);
        output.Write(body, 0, body.Length);
        output.Flush();
    }

    static string? ReadLine(Stream input)
    {
        var sb = new StringBuilder(64);
        while (true)
        {
            var c = input.ReadByte();
            if (c < 0)
                return sb.Length == 0 ? null : StripCr(sb);
            if (c == '\n')
                return StripCr(sb);
            sb.Append((char)c);
            if (sb.Length > 64 * 1024)
                throw new InvalidDataException("header line too long");
        }
    }

    static string StripCr(StringBuilder sb)
        => sb.Length > 0 && sb[sb.Length - 1] == '\r' ? sb.ToString(0, sb.Length - 1) : sb.ToString();
}
