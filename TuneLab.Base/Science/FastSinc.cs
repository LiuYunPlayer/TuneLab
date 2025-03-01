namespace TuneLab.Base.Science;

public class FastSinc
{
    public FastSinc(int sincSamples, int sincResolution = 512)
    {
        // init sinc
        mResolution = sincResolution; // 对速度几乎无影响
        int sincSingleSideLength = sincSamples * sincResolution;
        int sincCount = sincSingleSideLength + 1;
        mValues = new double[sincCount];
        var win = MathUtility.KaiserWin(sincSingleSideLength * 2, 4.8); // 加窗效果要好一些
        mValues[0] = 1;
        for (int i = 1; i < sincCount; i++)
        {
            mValues[i] = MathUtility.Sinc((double)i / sincResolution * Math.PI) * win[sincSingleSideLength - i];
        }
    }

    public double Calculate(double x)
    {
        return mValues[(int)(Math.Abs(x) * mResolution)];
        // 使用线性插值精度要好一些,但要慢1/4左右
        double index = Math.Abs(x) * mResolution;
        int i = (int)index;
        return MathUtility.Lerp(mValues[i], mValues[i + 1], index - i);
    }

    readonly double[] mValues;
    readonly int mResolution;
}
