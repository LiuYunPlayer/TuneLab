namespace TuneLab.Extensions.Synthesizer;

public class DirtyEvent
{
    public bool Handled { get; set; }

    public void Accept()
    {
        Handled = true;
    }
}
