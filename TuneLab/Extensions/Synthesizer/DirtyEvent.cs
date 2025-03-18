namespace TuneLab.Extensions.Synthesizer;

internal class DirtyEvent
{
    public bool Handled { get; set; }

    public void Accept()
    {
        Handled = true;
    }
}
