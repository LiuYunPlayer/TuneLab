using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.IO;
using System.Linq;
using System.Text;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Formats.TLP;

internal class TuneLabProjectCbor : IImportFormat, IExportFormat
{
    const int CURRENT_VERSION = 1;

    // 读取期工程版本（ReadProject 起始重置为 0=legacy；version 字段恒先于 tracks 写出，故 note/音素读到时已就位）。
    int mReadVersion;

    public ProjectInfo Deserialize(Stream streamToRead)
    {
        using var memoryStream = new MemoryStream();
        streamToRead.CopyTo(memoryStream);
        var bytes = memoryStream.ToArray();

        var reader = new CborReader(bytes);
        return ReadProject(reader);
    }

    public Stream Serialize(ProjectInfo projectInfo)
    {
        var writer = new CborWriter();
        WriteProject(writer, projectInfo);
        return new MemoryStream(writer.Encode());
    }

    #region Read Methods

    private ProjectInfo ReadProject(CborReader reader)
    {
        mReadVersion = 0;   // 缺省 legacy；version 字段写在最前，notes 之前必已读到
        var projectInfo = new ProjectInfo();

        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            switch (key)
            {
                case "version":
                    var version = reader.ReadInt32();
                    if (version > CURRENT_VERSION)
                        throw new Exception("Unsupported Version");
                    mReadVersion = version;
                    break;
                case "editorInfo":
                    ReadEditorInfo(reader, projectInfo.EditorInfo);
                    break;
                case "tempos":
                    ReadTempos(reader, projectInfo.Tempos);
                    break;
                case "timeSignatures":
                    ReadTimeSignatures(reader, projectInfo.TimeSignatures);
                    break;
                case "tracks":
                    ReadTracks(reader, projectInfo.Tracks);
                    break;
                case "exportConfig":
                    ReadExportConfig(reader, projectInfo.ExportConfig);
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndMap();

        return projectInfo;
    }

    private void ReadEditorInfo(CborReader reader, EditorInfo editorInfo)
    {
        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            switch (key)
            {
                case "playheadPos":
                    editorInfo.PlayheadPos = reader.ReadDouble();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndMap();
    }

    private void ReadTempos(CborReader reader, List<TempoInfo> tempos)
    {
        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            var tempoInfo = new TempoInfo();
            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                switch (key)
                {
                    case "pos":
                        tempoInfo.Pos = reader.ReadDouble();
                        break;
                    case "bpm":
                        tempoInfo.Bpm = reader.ReadDouble();
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
            reader.ReadEndMap();
            tempos.Add(tempoInfo);
        }
        reader.ReadEndArray();
    }

    private void ReadTimeSignatures(CborReader reader, List<TimeSignatureInfo> timeSignatures)
    {
        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            var tsInfo = new TimeSignatureInfo();
            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                switch (key)
                {
                    case "barIndex":
                        tsInfo.BarIndex = reader.ReadInt32();
                        break;
                    case "numerator":
                        tsInfo.Numerator = reader.ReadInt32();
                        break;
                    case "denominator":
                        tsInfo.Denominator = reader.ReadInt32();
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
            reader.ReadEndMap();
            timeSignatures.Add(tsInfo);
        }
        reader.ReadEndArray();
    }

    private void ReadTracks(CborReader reader, List<TrackInfo> tracks)
    {
        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            var trackInfo = new TrackInfo();
            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                switch (key)
                {
                    case "name":
                        trackInfo.Name = reader.ReadTextString();
                        break;
                    case "gain":
                        trackInfo.Gain = reader.ReadDouble();
                        break;
                    case "pan":
                        trackInfo.Pan = reader.ReadDouble();
                        break;
                    case "mute":
                        trackInfo.Mute = reader.ReadBoolean();
                        break;
                    case "solo":
                        trackInfo.Solo = reader.ReadBoolean();
                        break;
                    case "color":
                        trackInfo.Color = reader.ReadTextString();
                        break;
                    case "asRefer":
                        trackInfo.AsRefer = reader.ReadBoolean();
                        break;
                    case "exportEnabled":
                        trackInfo.ExportEnabled = reader.ReadBoolean();
                        break;
                    case "exportChannels":
                        trackInfo.ExportChannels = reader.ReadInt32();
                        break;
                    case "parts":
                        ReadParts(reader, trackInfo.Parts);
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
            reader.ReadEndMap();
            tracks.Add(trackInfo);
        }
        reader.ReadEndArray();
    }

    private void ReadExportConfig(CborReader reader, ExportConfigInfo config)
    {
        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            switch (key)
            {
                case "exportPath":
                    config.ExportPath = reader.ReadTextString();
                    break;
                case "fileName":
                    config.FileName = reader.ReadTextString();
                    break;
                case "sampleRate":
                    config.SampleRate = reader.ReadInt32();
                    break;
                case "bitDepth":
                    config.BitDepth = reader.ReadInt32();
                    break;
                case "masterExportEnabled":
                    config.MasterExportEnabled = reader.ReadBoolean();
                    break;
                case "masterExportChannels":
                    config.MasterExportChannels = reader.ReadInt32();
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndMap();
    }

    private void ReadParts(CborReader reader, List<PartInfo> parts)
    {
        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            PartInfo? partInfo = null;
            string? type = null;
            string name = "";
            double pos = 0;
            double dur = 0;

            // First pass to get type
            var startPosition = reader.BytesRemaining;
            reader.ReadStartMap();

            // We need to read all values, so let's use a temporary storage
            MidiPartInfo? midiPartInfo = null;
            AudioPartInfo? audioPartInfo = null;

            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                switch (key)
                {
                    case "type":
                        type = reader.ReadTextString();
                        if (type == "midi")
                            midiPartInfo = new MidiPartInfo();
                        else if (type == "audio")
                            audioPartInfo = new AudioPartInfo();
                        break;
                    case "name":
                        name = reader.ReadTextString();
                        break;
                    case "pos":
                        pos = reader.ReadDouble();
                        break;
                    case "dur":
                        dur = reader.ReadDouble();
                        break;
                    case "gain":
                        if (midiPartInfo != null)
                            midiPartInfo.Gain = reader.ReadDouble();
                        else
                            reader.SkipValue();
                        break;
                    case "voice":
                        if (midiPartInfo != null)
                            ReadVoice(reader, midiPartInfo);
                        else
                            reader.SkipValue();
                        break;
                    case "properties":
                        if (midiPartInfo != null)
                            midiPartInfo.Properties = ReadPropertyObject(reader);
                        else
                            reader.SkipValue();
                        break;
                    case "notes":
                        if (midiPartInfo != null)
                            ReadNotes(reader, midiPartInfo.Notes);
                        else
                            reader.SkipValue();
                        break;
                    case "pitch":
                        if (midiPartInfo != null)
                            ReadPitch(reader, midiPartInfo.Pitch);
                        else
                            reader.SkipValue();
                        break;
                    case "vibratos":
                        if (midiPartInfo != null)
                            ReadVibratos(reader, midiPartInfo.Vibratos);
                        else
                            reader.SkipValue();
                        break;
                    case "automations":
                        if (midiPartInfo != null)
                            ReadAutomations(reader, midiPartInfo.Automations);
                        else
                            reader.SkipValue();
                        break;
                    case "path":
                        if (audioPartInfo != null)
                            audioPartInfo.Path = reader.ReadTextString();
                        else
                            reader.SkipValue();
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
            reader.ReadEndMap();

            if (midiPartInfo != null)
            {
                midiPartInfo.Name = name;
                midiPartInfo.Pos = pos;
                midiPartInfo.Dur = dur;
                parts.Add(midiPartInfo);
            }
            else if (audioPartInfo != null)
            {
                audioPartInfo.Name = name;
                audioPartInfo.Pos = pos;
                audioPartInfo.Dur = dur;
                parts.Add(audioPartInfo);
            }
        }
        reader.ReadEndArray();
    }

    private void ReadVoice(CborReader reader, MidiPartInfo midiPartInfo)
    {
        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            switch (key)
            {
                case "type":
                    midiPartInfo.SoundSource.Type = reader.ReadTextString();
                    break;
                case "id":
                    midiPartInfo.SoundSource.ID = reader.ReadTextString();
                    break;
                case "kind":
                    // 缺省（旧工程无此键）= Voice，经 MidiPartInfo.SoundSource 默认值兜底。
                    midiPartInfo.SoundSource.Kind = reader.ReadTextString() == "instrument" ? SourceKind.Instrument : SourceKind.Voice;
                    break;
                default:
                    reader.SkipValue();
                    break;
            }
        }
        reader.ReadEndMap();
    }

    private void ReadNotes(CborReader reader, List<NoteInfo> notes)
    {
        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            double pos = 0;
            double dur = 0;
            int pitch = 0;
            string lyric = "";
            string pronunciation = "";
            PropertyObject properties = PropertyObject.Empty;
            List<PhonemeInfo> phonemes = new();

            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                switch (key)
                {
                    case "pos":
                        pos = reader.ReadDouble();
                        break;
                    case "dur":
                        dur = reader.ReadDouble();
                        break;
                    case "pitch":
                        pitch = reader.ReadInt32();
                        break;
                    case "lyric":
                        lyric = reader.ReadTextString();
                        break;
                    case "pronunciation":
                        pronunciation = reader.ReadTextString();
                        break;
                    case "properties":
                        properties = ReadPropertyObject(reader);
                        break;
                    case "phonemes":
                        ReadPhonemes(reader, phonemes);
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
            reader.ReadEndMap();

            var noteInfo = new NoteInfo
            {
                Pos = pos,
                Dur = dur,
                Pitch = pitch,
                Lyric = lyric,
                Pronunciation = pronunciation,
                Properties = properties,
                Phonemes = phonemes,
            };
            notes.Add(noteInfo);
        }
        reader.ReadEndArray();
    }

    private void ReadPhonemes(CborReader reader, List<PhonemeInfo> phonemes)
    {
        // 按工程版本分支（mReadVersion，不再逐音素探测字段）：
        //   v≥1：duration/stretchWeight/isLead（每音素时长 + 弹性权重 + 前置标记，位置由布局派生不存）。
        //   v<1（legacy）：startTime/endTime（相对音符头的秒，音符头=0）→ 时长 = endTime − startTime（音素连续）；
        //     旧模型无前置 / 弹性概念：按区间中点落在音符头之前（(start+end)/2 < 0）判定为前置辅音（IsLead）；
        //     第一个非前置音素默认为元音（弹性 w=1、吸收伸缩），其余（前置 + 后辅音）刚性 w=0。
        bool legacy = mReadVersion < 1;
        bool legacyVowelAssigned = false;
        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            string symbol = "";
            double weight = 0;
            double duration = 0;
            bool isLead = false;
            double startTime = 0, endTime = 0;
            PropertyObject? properties = null;

            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                switch (key)
                {
                    case "startTime": startTime = reader.ReadDouble(); break;
                    case "endTime": endTime = reader.ReadDouble(); break;
                    case "duration": duration = reader.ReadDouble(); break;
                    case "stretchWeight": weight = reader.ReadDouble(); break;
                    case "isLead": isLead = reader.ReadBoolean(); break;
                    case "symbol": symbol = reader.ReadTextString(); break;
                    case "properties": properties = ReadPropertyObject(reader); break;
                    default: reader.SkipValue(); break;
                }
            }
            reader.ReadEndMap();

            if (legacy)
            {
                isLead = (startTime + endTime) < 0;
                weight = (!isLead && !legacyVowelAssigned) ? 1 : 0;   // 首个非前置音素 = 元音
                if (!isLead) legacyVowelAssigned = true;
                duration = Math.Max(0, endTime - startTime);
            }
            phonemes.Add(new PhonemeInfo
            {
                Symbol = symbol,
                Duration = duration,
                StretchWeight = weight,
                IsLead = isLead,
                Properties = properties,
            });
        }
        reader.ReadEndArray();
    }

    private void ReadPitch(CborReader reader, List<List<Point>> pitch)
    {
        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            var line = new List<Point>();
            reader.ReadStartArray();
            while (reader.PeekState() != CborReaderState.EndArray)
            {
                var x = reader.ReadDouble();
                var y = reader.ReadDouble();
                line.Add(new Point(x, y));
            }
            reader.ReadEndArray();
            pitch.Add(line);
        }
        reader.ReadEndArray();
    }

    private void ReadVibratos(CborReader reader, List<VibratoInfo> vibratos)
    {
        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
        {
            var vibratoInfo = new VibratoInfo();
            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var key = reader.ReadTextString();
                switch (key)
                {
                    case "pos":
                        vibratoInfo.Pos = reader.ReadDouble();
                        break;
                    case "dur":
                        vibratoInfo.Dur = reader.ReadDouble();
                        break;
                    case "frequency":
                        vibratoInfo.Frequency = reader.ReadDouble();
                        break;
                    case "amplitude":
                        vibratoInfo.Amplitude = reader.ReadDouble();
                        break;
                    case "phase":
                        vibratoInfo.Phase = reader.ReadDouble();
                        break;
                    case "attack":
                        vibratoInfo.Attack = reader.ReadDouble();
                        break;
                    case "release":
                        vibratoInfo.Release = reader.ReadDouble();
                        break;
                    case "affectedAutomations":
                        ReadAffectedAutomations(reader, vibratoInfo.AffectedAutomations);
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
            reader.ReadEndMap();
            vibratos.Add(vibratoInfo);
        }
        reader.ReadEndArray();
    }

    private void ReadAffectedAutomations(CborReader reader, Map<string, double> affectedAutomations)
    {
        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            var value = reader.ReadDouble();
            affectedAutomations.Add(key, value);
        }
        reader.ReadEndMap();
    }

    private void ReadAutomations(CborReader reader, Map<string, AutomationInfo> automations)
    {
        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            var automationInfo = new AutomationInfo();

            reader.ReadStartMap();
            while (reader.PeekState() != CborReaderState.EndMap)
            {
                var propKey = reader.ReadTextString();
                switch (propKey)
                {
                    case "default":
                        automationInfo.DefaultValue = reader.ReadDouble();
                        break;
                    case "values":
                        reader.ReadStartArray();
                        while (reader.PeekState() != CborReaderState.EndArray)
                        {
                            var x = reader.ReadDouble();
                            var y = reader.ReadDouble();
                            automationInfo.Points.Add(new Point(x, y));
                        }
                        reader.ReadEndArray();
                        break;
                    default:
                        reader.SkipValue();
                        break;
                }
            }
            reader.ReadEndMap();

            automations.Add(key, automationInfo);
        }
        reader.ReadEndMap();
    }

    internal static PropertyObject ReadPropertyObject(CborReader reader)
    {
        var map = new Map<string, PropertyValue>();

        reader.ReadStartMap();
        while (reader.PeekState() != CborReaderState.EndMap)
        {
            var key = reader.ReadTextString();
            map.Add(key, ReadPropertyValue(reader));
        }
        reader.ReadEndMap();

        return new PropertyObject(map);
    }

    // 递归读任意 PropertyValue（对象值与数组元素共用）。
    static PropertyValue ReadPropertyValue(CborReader reader)
    {
        switch (reader.PeekState())
        {
            case CborReaderState.Boolean:
                return reader.ReadBoolean();
            case CborReaderState.UnsignedInteger:
            case CborReaderState.NegativeInteger:
            case CborReaderState.SinglePrecisionFloat:
            case CborReaderState.DoublePrecisionFloat:
            case CborReaderState.HalfPrecisionFloat:
                return reader.ReadDouble();
            case CborReaderState.TextString:
                return reader.ReadTextString();
            case CborReaderState.StartMap:
                return ReadPropertyObject(reader);
            case CborReaderState.StartArray:
                return ReadPropertyArray(reader);
            case CborReaderState.Null:
                reader.ReadNull();
                return PropertyValue.Null;
            default:
                reader.SkipValue();
                return PropertyValue.Null;
        }
    }

    static PropertyArray ReadPropertyArray(CborReader reader)
    {
        var values = new List<PropertyValue>();

        reader.ReadStartArray();
        while (reader.PeekState() != CborReaderState.EndArray)
            values.Add(ReadPropertyValue(reader));
        reader.ReadEndArray();

        return new PropertyArray(values);
    }

    #endregion

    #region Write Methods

    private void WriteProject(CborWriter writer, ProjectInfo projectInfo)
    {
        writer.WriteStartMap(null); // indefinite-length map

        writer.WriteTextString("version");
        writer.WriteInt32(CURRENT_VERSION);

        writer.WriteTextString("editorInfo");
        WriteEditorInfo(writer, projectInfo.EditorInfo);

        writer.WriteTextString("tempos");
        WriteTempos(writer, projectInfo.Tempos);

        writer.WriteTextString("timeSignatures");
        WriteTimeSignatures(writer, projectInfo.TimeSignatures);

        writer.WriteTextString("tracks");
        WriteTracks(writer, projectInfo.Tracks);

        writer.WriteTextString("exportConfig");
        WriteExportConfig(writer, projectInfo.ExportConfig);

        writer.WriteEndMap();
    }

    private void WriteEditorInfo(CborWriter writer, EditorInfo editorInfo)
    {
        writer.WriteStartMap(null);
        writer.WriteTextString("playheadPos");
        writer.WriteDouble(editorInfo.PlayheadPos);
        writer.WriteEndMap();
    }

    private void WriteTempos(CborWriter writer, List<TempoInfo> tempos)
    {
        writer.WriteStartArray(null); // indefinite-length array
        foreach (var tempo in tempos)
        {
            writer.WriteStartMap(null);
            writer.WriteTextString("pos");
            writer.WriteDouble(tempo.Pos);
            writer.WriteTextString("bpm");
            writer.WriteDouble(tempo.Bpm);
            writer.WriteEndMap();
        }
        writer.WriteEndArray();
    }

    private void WriteTimeSignatures(CborWriter writer, List<TimeSignatureInfo> timeSignatures)
    {
        writer.WriteStartArray(null);
        foreach (var ts in timeSignatures)
        {
            writer.WriteStartMap(null);
            writer.WriteTextString("barIndex");
            writer.WriteInt32(ts.BarIndex);
            writer.WriteTextString("numerator");
            writer.WriteInt32(ts.Numerator);
            writer.WriteTextString("denominator");
            writer.WriteInt32(ts.Denominator);
            writer.WriteEndMap();
        }
        writer.WriteEndArray();
    }

    private void WriteTracks(CborWriter writer, List<TrackInfo> tracks)
    {
        writer.WriteStartArray(null);
        foreach (var track in tracks)
        {
            writer.WriteStartMap(null);

            writer.WriteTextString("name");
            writer.WriteTextString(track.Name);

            writer.WriteTextString("gain");
            writer.WriteDouble(track.Gain);

            writer.WriteTextString("pan");
            writer.WriteDouble(track.Pan);

            writer.WriteTextString("mute");
            writer.WriteBoolean(track.Mute);

            writer.WriteTextString("solo");
            writer.WriteBoolean(track.Solo);

            writer.WriteTextString("color");
            writer.WriteTextString(track.Color);

            writer.WriteTextString("asRefer");
            writer.WriteBoolean(track.AsRefer);

            writer.WriteTextString("exportEnabled");
            writer.WriteBoolean(track.ExportEnabled);

            writer.WriteTextString("exportChannels");
            writer.WriteInt32(track.ExportChannels);

            writer.WriteTextString("parts");
            WriteParts(writer, track.Parts);

            writer.WriteEndMap();
        }
        writer.WriteEndArray();
    }

    private void WriteExportConfig(CborWriter writer, ExportConfigInfo config)
    {
        writer.WriteStartMap(null);

        writer.WriteTextString("exportPath");
        writer.WriteTextString(config.ExportPath);

        writer.WriteTextString("fileName");
        writer.WriteTextString(config.FileName);

        writer.WriteTextString("sampleRate");
        writer.WriteInt32(config.SampleRate);

        writer.WriteTextString("bitDepth");
        writer.WriteInt32(config.BitDepth);

        writer.WriteTextString("masterExportEnabled");
        writer.WriteBoolean(config.MasterExportEnabled);

        writer.WriteTextString("masterExportChannels");
        writer.WriteInt32(config.MasterExportChannels);

        writer.WriteEndMap();
    }

    private void WriteParts(CborWriter writer, List<PartInfo> parts)
    {
        writer.WriteStartArray(null);
        foreach (var part in parts)
        {
            if (part is MidiPartInfo midiPart)
            {
                WriteMidiPart(writer, midiPart);
            }
            else if (part is AudioPartInfo audioPart)
            {
                WriteAudioPart(writer, audioPart);
            }
        }
        writer.WriteEndArray();
    }

    private void WriteMidiPart(CborWriter writer, MidiPartInfo midiPart)
    {
        writer.WriteStartMap(null);

        writer.WriteTextString("type");
        writer.WriteTextString("midi");

        writer.WriteTextString("name");
        writer.WriteTextString(midiPart.Name);

        writer.WriteTextString("pos");
        writer.WriteDouble(midiPart.Pos);

        writer.WriteTextString("dur");
        writer.WriteDouble(midiPart.Dur);

        writer.WriteTextString("gain");
        writer.WriteDouble(midiPart.Gain);

        writer.WriteTextString("voice");
        writer.WriteStartMap(null);
        writer.WriteTextString("type");
        writer.WriteTextString(midiPart.SoundSource.Type);
        writer.WriteTextString("id");
        writer.WriteTextString(midiPart.SoundSource.ID);
        writer.WriteTextString("kind");
        writer.WriteTextString(midiPart.SoundSource.Kind == SourceKind.Instrument ? "instrument" : "voice");
        writer.WriteEndMap();

        writer.WriteTextString("properties");
        WritePropertyObject(writer, midiPart.Properties);

        writer.WriteTextString("notes");
        WriteNotes(writer, midiPart.Notes);

        writer.WriteTextString("pitch");
        WritePitch(writer, midiPart.Pitch);

        writer.WriteTextString("vibratos");
        WriteVibratos(writer, midiPart.Vibratos);

        writer.WriteTextString("automations");
        WriteAutomations(writer, midiPart.Automations);

        writer.WriteEndMap();
    }

    private void WriteAudioPart(CborWriter writer, AudioPartInfo audioPart)
    {
        writer.WriteStartMap(null);

        writer.WriteTextString("type");
        writer.WriteTextString("audio");

        writer.WriteTextString("name");
        writer.WriteTextString(audioPart.Name);

        writer.WriteTextString("pos");
        writer.WriteDouble(audioPart.Pos);

        writer.WriteTextString("dur");
        writer.WriteDouble(audioPart.Dur);

        writer.WriteTextString("path");
        writer.WriteTextString(audioPart.Path);

        writer.WriteEndMap();
    }

    private void WriteNotes(CborWriter writer, List<NoteInfo> notes)
    {
        writer.WriteStartArray(null);
        foreach (var note in notes)
        {
            writer.WriteStartMap(null);

            writer.WriteTextString("pos");
            writer.WriteDouble(note.Pos);

            writer.WriteTextString("dur");
            writer.WriteDouble(note.Dur);

            writer.WriteTextString("pitch");
            writer.WriteInt32(note.Pitch);

            writer.WriteTextString("lyric");
            writer.WriteTextString(note.Lyric);

            writer.WriteTextString("pronunciation");
            writer.WriteTextString(note.Pronunciation ?? "");

            writer.WriteTextString("properties");
            WritePropertyObject(writer, note.Properties);

            if (note.Phonemes.Count > 0)
            {
                writer.WriteTextString("phonemes");
                WritePhonemes(writer, note.Phonemes);
            }

            writer.WriteEndMap();
        }
        writer.WriteEndArray();
    }

    private void WritePhonemes(CborWriter writer, List<PhonemeInfo> phonemes)
    {
        writer.WriteStartArray(null);
        foreach (var phoneme in phonemes)
        {
            writer.WriteStartMap(null);

            writer.WriteTextString("symbol");
            writer.WriteTextString(phoneme.Symbol);

            writer.WriteTextString("duration");
            writer.WriteDouble(phoneme.Duration);

            writer.WriteTextString("stretchWeight");
            writer.WriteDouble(phoneme.StretchWeight);

            writer.WriteTextString("isLead");
            writer.WriteBoolean(phoneme.IsLead);

            // per-phoneme 引擎自定义属性（空则省略，pay-as-you-go）。
            if (phoneme.Properties is { } props && props.Map.Count > 0)
            {
                writer.WriteTextString("properties");
                WritePropertyObject(writer, props);
            }

            writer.WriteEndMap();
        }
        writer.WriteEndArray();
    }

    private void WritePitch(CborWriter writer, List<List<Point>> pitch)
    {
        writer.WriteStartArray(null);
        foreach (var line in pitch)
        {
            writer.WriteStartArray(null);
            foreach (var point in line)
            {
                writer.WriteDouble(point.X);
                writer.WriteDouble(point.Y);
            }
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
    }

    private void WriteVibratos(CborWriter writer, List<VibratoInfo> vibratos)
    {
        writer.WriteStartArray(null);
        foreach (var vibrato in vibratos)
        {
            writer.WriteStartMap(null);

            writer.WriteTextString("pos");
            writer.WriteDouble(vibrato.Pos);

            writer.WriteTextString("dur");
            writer.WriteDouble(vibrato.Dur);

            writer.WriteTextString("frequency");
            writer.WriteDouble(vibrato.Frequency);

            writer.WriteTextString("amplitude");
            writer.WriteDouble(vibrato.Amplitude);

            writer.WriteTextString("phase");
            writer.WriteDouble(vibrato.Phase);

            writer.WriteTextString("attack");
            writer.WriteDouble(vibrato.Attack);

            writer.WriteTextString("release");
            writer.WriteDouble(vibrato.Release);

            if (vibrato.AffectedAutomations.Count > 0)
            {
                writer.WriteTextString("affectedAutomations");
                writer.WriteStartMap(null);
                foreach (var kvp in vibrato.AffectedAutomations)
                {
                    writer.WriteTextString(kvp.Key);
                    writer.WriteDouble(kvp.Value);
                }
                writer.WriteEndMap();
            }

            writer.WriteEndMap();
        }
        writer.WriteEndArray();
    }

    private void WriteAutomations(CborWriter writer, IReadOnlyMap<string, AutomationInfo> automations)
    {
        writer.WriteStartMap(null);
        foreach (var kvp in automations)
        {
            writer.WriteTextString(kvp.Key);

            writer.WriteStartMap(null);

            writer.WriteTextString("default");
            writer.WriteDouble(kvp.Value.DefaultValue);

            writer.WriteTextString("values");
            writer.WriteStartArray(null);
            foreach (var point in kvp.Value.Points)
            {
                writer.WriteDouble(point.X);
                writer.WriteDouble(point.Y);
            }
            writer.WriteEndArray();

            writer.WriteEndMap();
        }
        writer.WriteEndMap();
    }

    internal static void WritePropertyObject(CborWriter writer, PropertyObject properties)
    {
        writer.WriteStartMap(null);
        foreach (var property in properties.Map)
        {
            // 稀疏：null / multiple 哨兵不写键（与 presence 语义一致：absent = 默认）。
            if (property.Value.IsNull() || property.Value.IsMultiple())
                continue;

            writer.WriteTextString(property.Key);
            WritePropertyValue(writer, property.Value);
        }
        writer.WriteEndMap();
    }

    // 递归写任意 PropertyValue（对象值与数组元素共用）。数组元素须按位写齐，故 Null 元素写成 CBOR null 占位。
    static void WritePropertyValue(CborWriter writer, PropertyValue value)
    {
        if (value.ToObject(out var propertyObject))
            WritePropertyObject(writer, propertyObject);
        else if (value.ToArray(out var propertyArray))
            WritePropertyArray(writer, propertyArray);
        else if (value.ToBool(out var boolValue))
            writer.WriteBoolean(boolValue);
        else if (value.ToDouble(out var doubleValue))
            writer.WriteDouble(doubleValue);
        else if (value.ToString(out var stringValue))
            writer.WriteTextString(stringValue);
        else
            writer.WriteNull();
    }

    static void WritePropertyArray(CborWriter writer, PropertyArray array)
    {
        writer.WriteStartArray(null);   // 不定长；空数组也照写 []（present-[] 是真实值，不可省）
        foreach (var value in array)
            WritePropertyValue(writer, value);
        writer.WriteEndArray();
    }

    #endregion
}
