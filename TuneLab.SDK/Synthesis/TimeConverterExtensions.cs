namespace TuneLab.SDK;

// ITimeConverter 的单点换算（便利写法，每次临时建一个单元素数组；成批的点用接口上的批量版）。
public static class TimeConverterExtensions
{
    public static double ToSecond(this ITimeConverter converter, double tick) => converter.ToSeconds([tick])[0];
    public static double ToTick(this ITimeConverter converter, double second) => converter.ToTicks([second])[0];
}
