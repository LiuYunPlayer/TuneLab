using Avalonia.Media;

namespace TuneLab.Utils;

// 外部来源色串的安全解析：automation config 色是插件可控输入、不可信，非法串回退 SDK 默认中性灰，
// 渲染/构建路径无条件不崩。宿主自有常量色仍走 Color.Parse（写错即早炸、不该被兜住）。
internal static class ColorUtils
{
    static readonly Color Fallback = Color.Parse("#888888");

    public static Color ParseOrFallback(string? colorStr)
        => colorStr != null && Color.TryParse(colorStr, out var color) ? color : Fallback;
}
