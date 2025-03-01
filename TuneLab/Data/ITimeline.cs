namespace TuneLab.Data;

internal interface ITimeline
{
    ITempoManager TempoManager { get; }
    ITimeSignatureManager TimeSignatureManager { get; }
}
