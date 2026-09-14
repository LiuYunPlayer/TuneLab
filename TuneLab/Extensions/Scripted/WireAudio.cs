using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace TuneLab.Extensions.Scripted;

// 内联音频/曲线面的编解码（设计 §6：inline plane——小数据走 base64 little-endian，shm 面 M3 上线后
// 只剩兜底角色）。f32 = PCM 样本，f64 = 曲线求值结果。
internal static class WireAudio
{
    public static string EncodeF32(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * 4];
        for (int i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), samples[i]);
        return Convert.ToBase64String(bytes);
    }

    public static float[] DecodeF32(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var samples = new float[bytes.Length / 4];
        for (int i = 0; i < samples.Length; i++)
            samples[i] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(i * 4, 4));
        return samples;
    }

    public static string EncodeF64(IReadOnlyList<double> values)
    {
        var bytes = new byte[values.Count * 8];
        for (int i = 0; i < values.Count; i++)
            BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i * 8, 8), values[i]);
        return Convert.ToBase64String(bytes);
    }

    public static double[] DecodeF64(string base64)
    {
        var bytes = Convert.FromBase64String(base64);
        var values = new double[bytes.Length / 8];
        for (int i = 0; i < values.Length; i++)
            values[i] = BinaryPrimitives.ReadDoubleLittleEndian(bytes.AsSpan(i * 8, 8));
        return values;
    }
}
