using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls.Shapes;
using DynamicData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Extensions.Formats.TLP;

internal class TuneLabProject : IImportFormat, IExportFormat
{
    // v0=legacy 1.x（几何存 dur、老音素 startTime/endTime）；v1=2.0.0 中间态（新音素、几何仍 dur）；
    // v2=当前（part 几何锚点 Pos+StartOffset+EndOffset）。音素前置量的 isLead↔preutterance 两种表示同为 v2
    //（当前版顶替 isLead 那版），故靠**字段**而非版本号区分（见音素读取）。反序列化按版本忠实降级 legacy 几何。
    const int CURRENT_VERSION = 2;
    public ProjectInfo Deserialize(Stream streamToRead)
    {
        using (StreamReader reader = new StreamReader(streamToRead, Encoding.UTF8))
        {
            string content = reader.ReadToEnd();

            var projectInfo = new ProjectInfo();
            JObject? project = JsonConvert.DeserializeObject<JObject>(content);
            if (project == null)
                throw new Exception("Json deserialization failed!");

            var versoin = (int)project["version"];
            if (versoin > CURRENT_VERSION)
                throw new Exception("Unsupported Version");

            var tempos = project["tempos"].ToArray();
            foreach (JObject tempo in tempos)
            {
                var tempoInfo = new TempoInfo()
                {
                    Pos = (double)tempo["pos"],
                    Bpm = (double)tempo["bpm"]
                };

                projectInfo.Tempos.Add(tempoInfo);
            }

            var timeSignatures = project["timeSignatures"].ToArray();
            foreach (JObject timeSignature in timeSignatures)
            {
                var timeSignatureInfo = new TimeSignatureInfo()
                {
                    BarIndex = (int)timeSignature["barIndex"],
                    Numerator = (int)timeSignature["numerator"],
                    Denominator = (int)timeSignature["denominator"],
                };

                projectInfo.TimeSignatures.Add(timeSignatureInfo);
            }

            var tracks = project["tracks"].ToArray();
            foreach (JObject track in tracks)
            {
                var trackInfo = new TrackInfo()
                {
                    Name = (string)track["name"],
                    Gain = (double)track["gain"],
                    Pan = (double)track["pan"],
                    Mute = (bool)track["mute"],
                    Solo = (bool)track["solo"],
                };

                if (track.TryGetValue("color", out var color))
                    trackInfo.Color = (string)color;

                if (track.TryGetValue("asRefer", out var asRefer))
                    trackInfo.AsRefer = (bool)asRefer;

                if (track.TryGetValue("exportEnabled", out var exportEnabled))
                    trackInfo.ExportEnabled = (bool)exportEnabled;

                if (track.TryGetValue("exportChannels", out var exportChannels))
                    trackInfo.ExportChannels = (int)exportChannels;

                var parts = track["parts"].ToArray();
                foreach (JObject part in parts)
                {
                    PartInfo? partInfo = null;

                    var type = (string)part["type"];
                    if (type == "midi")
                    {
                        var midiPartInfo = new MidiPartInfo();

                        midiPartInfo.Gain = (double?)part["gain"] ?? 0;
                        midiPartInfo.Properties = FromJson(part["properties"]);
                        midiPartInfo.SoundSource.Type = (string)part["voice"]["type"];
                        midiPartInfo.SoundSource.ID = (string)part["voice"]["id"];
                        // 缺省（旧工程无此键）= Voice。
                        midiPartInfo.SoundSource.Kind = (string?)part["voice"]?["kind"] == "instrument" ? SourceKind.Instrument : SourceKind.Voice;

                        var notes = part["notes"].ToArray();
                        foreach (JObject note in notes)
                        {
                            var noteInfo = new NoteInfo()
                            {
                                Pos = (int)note["pos"],
                                Dur = (int)note["dur"],
                                Pitch = (int)note["pitch"],
                                Lyric = (string)note["lyric"],
                                Pronunciation = (string)note["pronunciation"],
                                Properties = FromJson(note["properties"]),
                            };

                            if (note.TryGetValue("phonemes", out var phonemes))
                            {
                                // 当前格式：每音素 duration/stretchWeight，前置量为 note 级 preutterance（拍前发声量）。
                                // v<1（legacy 1.x）：startTime/endTime（相对音符头的秒，音符头=0）→ 时长 = endTime − startTime（音素连续）；
                                //   旧模型无前置 / 弹性概念：按区间中点落在音符头之前（(start+end)/2 < 0）判前置辅音折算出 note 级前置量、权重恒 0
                                //   （老版随音符等比缩放，布局「全 w=0 退化为按原长等比」复刻之）。
                                bool legacy = versoin < 1;
                                double leadPreutterance = 0;   // 仅 legacy：从前置前缀折算
                                bool stillLead = true;
                                foreach (JObject phoneme in phonemes)
                                {
                                    string symbol = (string)phoneme["symbol"] ?? "";
                                    double duration, weight;
                                    if (legacy)
                                    {
                                        double startTime = (double?)phoneme["startTime"] ?? 0;
                                        double endTime = (double?)phoneme["endTime"] ?? 0;
                                        weight = 0;
                                        duration = Math.Max(0, endTime - startTime);
                                        if (stillLead && (startTime + endTime) < 0) leadPreutterance += duration; else stillLead = false;
                                    }
                                    else
                                    {
                                        duration = (double?)phoneme["duration"] ?? 0;
                                        weight = (double?)phoneme["stretchWeight"] ?? 0;
                                    }
                                    noteInfo.Phonemes.Add(new PhonemeInfo()
                                    {
                                        Symbol = symbol,
                                        Duration = duration,
                                        StretchWeight = weight,
                                        Properties = phoneme.TryGetValue("properties", out var phonemeProps) ? FromJson(phonemeProps) : null,
                                    });
                                }
                                noteInfo.Preutterance = legacy ? leadPreutterance : ((double?)note["preutterance"] ?? 0);
                            }

                            midiPartInfo.Notes.Add(noteInfo);
                        }

                        if (part.TryGetValue("pitch", out var pitch))
                        {
                            foreach (JArray values in pitch.ToArray())
                            {
                                var line = new List<Point>();
                                bool flag = false;
                                double x = 0;
                                foreach (double value in values)
                                {
                                    if (flag)
                                    {
                                        line.Add(new Point(x, value));
                                    }
                                    else
                                    {
                                        x = value;
                                    }
                                    flag = !flag;
                                }

                                midiPartInfo.Pitch.Add(line);
                            }
                        }

                        if (part.TryGetValue("vibratos", out var vibratos))
                        {
                            foreach (JObject vibrato in vibratos.ToArray())
                            {
                                var vibratoInfo = new VibratoInfo()
                                {
                                    Pos = (double)vibrato["pos"],
                                    Dur = (double)vibrato["dur"],
                                    Frequency = (double)vibrato["frequency"],
                                    Amplitude = (double)vibrato["amplitude"],
                                    Phase = (double)vibrato["phase"],
                                    Attack = (double)vibrato["attack"],
                                    Release = (double)vibrato["release"],
                                };

                                if (vibrato.TryGetValue("affectedAutomations", out var affectedAutomations))
                                {
                                    foreach (JProperty property in affectedAutomations.Children())
                                    {
                                        vibratoInfo.AffectedAutomations.Add(property.Name, (double)property.Value);
                                    }
                                }

                                midiPartInfo.Vibratos.Add(vibratoInfo);
                            }
                        }

                        if (part.TryGetValue("automations", out var automations))
                        {
                            foreach (JProperty property in automations.Children())
                            {
                                var automationInfo = new AutomationInfo();

                                var key = property.Name;
                                var automation = property.Value;

                                automationInfo.DefaultValue = (double)automation["default"];

                                var values = automation["values"].ToArray();
                                bool flag = false;
                                double x = 0;
                                foreach (double value in values)
                                {
                                    if (flag)
                                    {
                                        automationInfo.Points.Add(new Point(x, value));
                                    }
                                    else
                                    {
                                        x = value;
                                    }
                                    flag = !flag;
                                }

                                midiPartInfo.Automations.Add(key, automationInfo);
                            }
                        }

                        partInfo = midiPartInfo;
                    }
                    else if (type == "audio")
                    {
                        var audioPartInfo = new AudioPartInfo();

                        audioPartInfo.Path = (string)part["path"];

                        partInfo = audioPartInfo;
                    }

                    if (partInfo == null)
                        continue;

                    partInfo.Name = (string)part["name"];
                    partInfo.Pos = (int)part["pos"];
                    // 几何字段由版本号唯一决定（不按字段存在与否猜测）：
                    //   v<2（legacy）：只存 dur、无前向裁剪概念 → 锚点即起点（StartOffset=0）、终点 = 锚点 + dur。
                    //   v≥2：锚点模型 startOffset/endOffset。
                    if (versoin < 2)
                    {
                        partInfo.StartOffset = 0;
                        partInfo.EndOffset = (double)part["dur"];
                    }
                    else
                    {
                        partInfo.StartOffset = (double)part["startOffset"];
                        partInfo.EndOffset = (double)part["endOffset"];
                    }

                    trackInfo.Parts.Add(partInfo);
                }

                projectInfo.Tracks.Add(trackInfo);
            }

            if (project.TryGetValue("exportConfig", out var exportConfig) && exportConfig is JObject exportConfigObj)
            {
                if (exportConfigObj.TryGetValue("exportPath", out var exportPath))
                    projectInfo.ExportConfig.ExportPath = (string)exportPath;
                if (exportConfigObj.TryGetValue("fileName", out var fileName))
                    projectInfo.ExportConfig.FileName = (string)fileName;
                if (exportConfigObj.TryGetValue("sampleRate", out var sampleRate))
                    projectInfo.ExportConfig.SampleRate = (int)sampleRate;
                if (exportConfigObj.TryGetValue("bitDepth", out var bitDepth))
                    projectInfo.ExportConfig.BitDepth = (int)bitDepth;
                if (exportConfigObj.TryGetValue("masterExportEnabled", out var masterExportEnabled))
                    projectInfo.ExportConfig.MasterExportEnabled = (bool)masterExportEnabled;
                if (exportConfigObj.TryGetValue("masterExportChannels", out var masterExportChannels))
                    projectInfo.ExportConfig.MasterExportChannels = (int)masterExportChannels;
            }

            return projectInfo;
        }
    }

    public Stream Serialize(ProjectInfo projectInfo)
    {
        var project = new JObject();
        project.Add("version", CURRENT_VERSION);

        var tempos = new JArray();
        foreach (var tempoInfo in projectInfo.Tempos)
        {
            var tempo = new JObject();
            tempo.Add("pos", tempoInfo.Pos);
            tempo.Add("bpm", tempoInfo.Bpm);

            tempos.Add(tempo);
        }
        project.Add("tempos", tempos);

        var timeSignatures = new JArray();
        foreach (var timeSignatureInfo in projectInfo.TimeSignatures)
        {
            var timeSignature = new JObject();
            timeSignature.Add("barIndex", timeSignatureInfo.BarIndex);
            timeSignature.Add("numerator", timeSignatureInfo.Numerator);
            timeSignature.Add("denominator", timeSignatureInfo.Denominator);

            timeSignatures.Add(timeSignature);
        }
        project.Add("timeSignatures", timeSignatures);

        var tracks = new JArray();
        foreach (var trackInfo in projectInfo.Tracks)
        {
            var track = new JObject();
            track.Add("name", trackInfo.Name);
            track.Add("gain", trackInfo.Gain);
            track.Add("pan", trackInfo.Pan);
            track.Add("mute", trackInfo.Mute);
            track.Add("solo", trackInfo.Solo);
            track.Add("color", trackInfo.Color);
            track.Add("asRefer", trackInfo.AsRefer);
            track.Add("exportEnabled", trackInfo.ExportEnabled);
            track.Add("exportChannels", trackInfo.ExportChannels);

            var parts = new JArray();
            foreach (var partInfo in trackInfo.Parts)
            {
                var part = new JObject();
                part.Add("name", partInfo.Name);
                part.Add("pos", partInfo.Pos);
                part.Add("startOffset", partInfo.StartOffset);
                part.Add("endOffset", partInfo.EndOffset);

                if (partInfo is MidiPartInfo midiPartInfo)
                {
                    part.Add("type", "midi");
                    part.Add("gain", midiPartInfo.Gain);
                    part.Add("voice", new JObject()
                    {
                        { "type", midiPartInfo.SoundSource.Type },
                        { "id", midiPartInfo.SoundSource.ID },
                        { "kind", midiPartInfo.SoundSource.Kind == SourceKind.Instrument ? "instrument" : "voice" },
                    });
                    part.Add("properties", ToJson(midiPartInfo.Properties));

                    var notes = new JArray();
                    foreach (var noteInfo in  midiPartInfo.Notes)
                    {
                        var note = new JObject();
                        note.Add("pos", noteInfo.Pos);
                        note.Add("dur", noteInfo.Dur);
                        note.Add("pitch", noteInfo.Pitch);
                        note.Add("lyric", noteInfo.Lyric);
                        note.Add("pronunciation", noteInfo.Pronunciation);
                        note.Add("properties", ToJson(noteInfo.Properties));
                        if (!noteInfo.Phonemes.IsEmpty())
                        {
                            note.Add("preutterance", noteInfo.Preutterance);   // note 级前置量（拍前发声量）
                            var phonemes = new JArray();
                            foreach (var phonemeInfo in noteInfo.Phonemes)
                            {
                                var phoneme = new JObject();
                                phoneme.Add("symbol", phonemeInfo.Symbol);
                                phoneme.Add("duration", phonemeInfo.Duration);
                                phoneme.Add("stretchWeight", phonemeInfo.StretchWeight);
                                // per-phoneme 引擎自定义属性（空则省略，pay-as-you-go）。
                                if (phonemeInfo.Properties is { } phonemeProps && phonemeProps.Map.Count > 0)
                                    phoneme.Add("properties", ToJson(phonemeProps));

                                phonemes.Add(phoneme);
                            }

                            note.Add("phonemes", phonemes);
                        }

                        notes.Add(note);
                    }
                    part.Add("notes", notes);

                    var automations = new JObject();
                    foreach (var automationInfo in midiPartInfo.Automations)
                    {
                        var automation = new JObject();
                        automation.Add("default", automationInfo.Value.DefaultValue);

                        var values = new JArray();
                        foreach (var pointInfo in automationInfo.Value.Points)
                        {
                            values.Add(pointInfo.X);
                            values.Add(pointInfo.Y);
                        }
                        automation.Add("values", values);

                        automations.Add(automationInfo.Key, automation);
                    }
                    part.Add("automations", automations);

                    var pitch = new JArray();
                    foreach (var lineInfo in midiPartInfo.Pitch)
                    {
                        var values = new JArray();
                        foreach (var pointInfo in lineInfo)
                        {
                            values.Add(pointInfo.X);
                            values.Add(pointInfo.Y);
                        }
                        pitch.Add(values);
                    }
                    part.Add("pitch", pitch);

                    var vibratos = new JArray();
                    foreach (var vibratoInfo in midiPartInfo.Vibratos)
                    {
                        var vibrato = new JObject();
                        vibrato.Add("pos", vibratoInfo.Pos);
                        vibrato.Add("dur", vibratoInfo.Dur);
                        vibrato.Add("frequency", vibratoInfo.Frequency);
                        vibrato.Add("amplitude", vibratoInfo.Amplitude);
                        vibrato.Add("phase", vibratoInfo.Phase);
                        vibrato.Add("attack", vibratoInfo.Attack);
                        vibrato.Add("release", vibratoInfo.Release);

                        if (vibratoInfo.AffectedAutomations.Count != 0)
                        {
                            var affectiveAutomations = new JObject();
                            foreach (var kvp in vibratoInfo.AffectedAutomations)
                            {
                                affectiveAutomations.Add(kvp.Key, kvp.Value);
                            }
                            vibrato.Add("affectedAutomations", affectiveAutomations);
                        }

                        vibratos.Add(vibrato);
                    }
                    part.Add("vibratos", vibratos);
                }
                else if (partInfo is AudioPartInfo audioPartInfo)
                {
                    part.Add("type", "audio");
                    part.Add("path", audioPartInfo.Path);
                }

                parts.Add(part);
            }
            track.Add("parts", parts);

            tracks.Add(track);
        }
        project.Add("tracks", tracks);

        var exportConfig = new JObject();
        exportConfig.Add("exportPath", projectInfo.ExportConfig.ExportPath);
        exportConfig.Add("fileName", projectInfo.ExportConfig.FileName);
        exportConfig.Add("sampleRate", projectInfo.ExportConfig.SampleRate);
        exportConfig.Add("bitDepth", projectInfo.ExportConfig.BitDepth);
        exportConfig.Add("masterExportEnabled", projectInfo.ExportConfig.MasterExportEnabled);
        exportConfig.Add("masterExportChannels", projectInfo.ExportConfig.MasterExportChannels);
        project.Add("exportConfig", exportConfig);

        return new MemoryStream(Encoding.UTF8.GetBytes(project.ToString(Formatting.None)));
    }

    PropertyObject FromJson(JToken jToken)
    {
        var map = new Map<string, PropertyValue>();

        foreach (JProperty property in jToken.Children())
        {
            var key = property.Name;
            var value = property.Value;
            switch (value.Type)
            {
                case JTokenType.Boolean:
                    map.Add(key, (bool)value);
                    break;
                case JTokenType.Integer:
                    map.Add(key, (int)value);
                    break;
                case JTokenType.Float:
                    map.Add(key, (double)value);
                    break;
                case JTokenType.String:
                    map.Add(key, (string)value);
                    break;
                case JTokenType.Object:
                    map.Add(key, FromJson(value));
                    break;
            }
        }
        return new(map);
    }

    JObject ToJson(PropertyObject properties)
    {
        var json = new JObject();
        foreach (var property in properties.Map)
        {
            var key = property.Key;
            var value = property.Value;
            if (value.ToObject(out var propertyObject))
            {
                json.Add(key, ToJson(propertyObject));
            }
            else if (value.ToBool(out var boolValue))
            {
                json.Add(key, boolValue);
            }
            else if (value.ToDouble(out var doubleValue))
            {
                json.Add(key, doubleValue);
            }
            else if (value.ToString(out var strinValue))
            {
                json.Add(key, strinValue);
            }
        }
        return json;
    }
}
