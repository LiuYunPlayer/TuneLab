namespace TuneLab.Utils;

internal class IDGenerator
{
    public long GenerateID()
    {
        return ++id;
    }

    long id = 0;
}
