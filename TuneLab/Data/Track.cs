using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Data;
using TuneLab.Base.Structures;
using TuneLab.Base.Science;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;
using TuneLab.Utils;
using TuneLab.Base.Utils;

namespace TuneLab.Data;

internal class Track : DataObject, ITrack
{
    public IProject Project => mProject;
    public ITempoManager TempoManager => mProject.TempoManager;
    public ITimeSignatureManager TimeSignatureManager => mProject.TimeSignatureManager;
    public DataProperty<string> Name { get; }
    public DataProperty<bool> IsMute { get; }
    public DataProperty<bool> IsSolo { get; }
    public DataProperty<bool> AsRefer { get; }
    public DataProperty<double> Gain { get; }
    public DataProperty<double> Pan { get; }
    public DataProperty<string> Color { get; }
    public bool ExportEnabled { get; set; } = false;
    public int ExportChannels { get; set; } = 1;
    public IReadOnlyDataObjectLinkedList<IPart> Parts => mParts;
    public IReadOnlyDataObjectList<ITrackPlugin> Plugins => mPlugins;

    IDataProperty<string> ITrack.Name => Name;

    IDataProperty<bool> ITrack.IsMute => IsMute;

    IDataProperty<bool> ITrack.IsSolo => IsSolo;

    IDataProperty<bool> ITrack.AsRefer => AsRefer;

    IDataProperty<double> ITrack.Gain => Gain;
    IDataProperty<double> ITrack.Pan => Pan;
    IDataProperty<string> ITrack.Color => Color;

    public Track(IProject project, TrackInfo info)
    {
        mProject = project;
        Name = new DataString(this, string.Empty);
        IsMute = new DataStruct<bool>(this);
        IsSolo = new DataStruct<bool>(this);
        AsRefer = new DataStruct<bool>(this);
        Gain = new DataStruct<double>(this);
        Pan = new DataStruct<double>(this);
        Color = new DataString(this);
        mParts = new();
        mParts.Attach(this);
        mPlugins = new();
        mPlugins.Attach(this);

        mParts.ItemAdded.Subscribe(part => { part.Track = this; part.Activate(); });
        mParts.ItemRemoved.Subscribe(part => { part.Deactivate(); });
        
        // Subscribe to plugin list changes to handle plugin activation
        mPlugins.ItemAdded.Subscribe(plugin => { OnPluginAdded(plugin); });
        mPlugins.ItemRemoved.Subscribe(plugin => { OnPluginRemoved(plugin); });

        IDataObject<TrackInfo>.SetInfo(this, info);
    }

    public void InsertPart(IPart part)
    {
        mParts.Insert(part);
    }

    public bool RemovePart(IPart part)
    {
        return mParts.Remove(part);
    }

    public MidiPart CreatePart(MidiPartInfo info)
    {
        return new MidiPart(this, info);
    }

    public AudioPart CreatePart(AudioPartInfo info)
    {
        return new AudioPart(this, info);
    }

    public Part CreatePart(PartInfo info)
    {
        if (info is MidiPartInfo midiPartInfo)
            return CreatePart(midiPartInfo);
        else if (info is AudioPartInfo audioPartInfo)
            return CreatePart(audioPartInfo);

        throw new ArgumentException();
    }
    
    public ITrackPlugin AddPlugin(TrackPluginInfo info)
    {
        var plugin = new TrackPlugin(this, info);
        mPlugins.Add(plugin);
        return plugin;
    }
    
    public bool RemovePlugin(ITrackPlugin plugin)
    {
        return mPlugins.Remove(plugin);
    }
    
    private TrackPlugin CreatePlugin(TrackPluginInfo info)
    {
        return new TrackPlugin(this, info);
    }
    
    private void OnPluginAdded(ITrackPlugin plugin)
    {
        // Try to load the plugin when added
        if (!string.IsNullOrEmpty(plugin.PluginUid.Value) || !string.IsNullOrEmpty(plugin.PluginPath.Value))
        {
            plugin.LoadPlugin();
        }
    }
    
    private void OnPluginRemoved(ITrackPlugin plugin)
    {
        // Unload and dispose the plugin when removed
        plugin.UnloadPlugin();
        plugin.Dispose();
    }

    public void Activate()
    {
        AudioEngine.AddTrack(this);
        foreach (var part in mParts)
        {
            part.Activate();
        }
        
        // Load plugins
        foreach (var plugin in mPlugins)
        {
            plugin.LoadPlugin();
        }
    }

    public void Deactivate()
    {
        AudioEngine.RemoveTrack(this);
        foreach (var part in mParts)
        {
            part.Deactivate();
        }
        
        // Unload plugins
        foreach (var plugin in mPlugins)
        {
            plugin.UnloadPlugin();
        }
    }

    public TrackInfo GetInfo()
    {
        return new()
        {
            Name = Name,
            Mute = IsMute,
            Solo = IsSolo,
            Gain = Gain,
            Pan = Pan,
            Color = Color,
            AsRefer = AsRefer,
            ExportEnabled = ExportEnabled,
            ExportChannels = ExportChannels,
            Parts = mParts.GetInfo().ToInfo(),
            Plugins = mPlugins.Select(p => p.GetInfo()).ToList()
        };
    }

    void IDataObject<TrackInfo>.SetInfo(TrackInfo info)
    {
        IDataObject<TrackInfo>.SetInfo(Name, info.Name);
        IDataObject<TrackInfo>.SetInfo(IsMute, info.Mute);
        IDataObject<TrackInfo>.SetInfo(IsSolo, info.Solo);
        IDataObject<TrackInfo>.SetInfo(Gain, info.Gain);
        IDataObject<TrackInfo>.SetInfo(Pan, info.Pan);
        IDataObject<TrackInfo>.SetInfo(Color, info.Color);
        IDataObject<TrackInfo>.SetInfo(AsRefer, info.AsRefer);
        ExportEnabled = info.ExportEnabled;
        ExportChannels = info.ExportChannels;
        IDataObject<TrackInfo>.SetInfo(mParts, info.Parts.Convert(CreatePart).ToArray());
        IDataObject<TrackInfo>.SetInfo(mPlugins, info.Plugins.Select(CreatePlugin).ToArray());
    }

    class PartList : DataObjectLinkedList<IPart>
    {
        protected override bool IsInOrder(IPart prev, IPart next)
        {
            if (prev.StartPos() < next.StartPos())
                return true;

            if (prev.StartPos() > next.StartPos())
                return false;

            if (prev.EndPos() > next.EndPos())
                return true;

            if (prev.EndPos() < next.EndPos())
                return false;

            return true;
        }
    }
    
    class PluginList : DataObjectList<ITrackPlugin>
    {
    }

    readonly IProject mProject;
    readonly PartList mParts;
    readonly PluginList mPlugins;
}
