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
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Utils;
using TuneLab.Base.Utils;

namespace TuneLab.Extensions.Formats.TLP;

[ImportFormat("tlp")]
[ExportFormat("tlp")]
internal class TuneLabProject : IImportFormat, IExportFormat
{
    const int CURRENT_VERSION = 0;
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
                    Color = (System.Drawing.Color)(track.ContainsKey("color") ? System.Drawing.Color.FromArgb((int)track["color"]) : System.Drawing.Color.FromArgb(255, 58, 63, 105))
                };

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
                        midiPartInfo.Voice.Type = (string)part["voice"]["type"];
                        midiPartInfo.Voice.ID = (string)part["voice"]["id"];

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
                                Properties = FromJson(note["properties"])
                            };

                            if (note.TryGetValue("phonemes", out var phonemes))
                            {
                                foreach (JObject phoneme in phonemes)
                                {
                                    var phonemeInfo = new PhonemeInfo()
                                    {
                                        StartTime = (double)phoneme["startTime"],
                                        EndTime = (double)phoneme["endTime"],
                                        Symbol = (string)phoneme["symbol"],
                                    };

                                    noteInfo.Phonemes.Add(phonemeInfo);
                                }
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
                    partInfo.Dur = (int)part["dur"];

                    trackInfo.Parts.Add(partInfo);
                }

                projectInfo.Tracks.Add(trackInfo);
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
            track.Add("color", trackInfo.Color.ToArgb());

            var parts = new JArray();
            foreach (var partInfo in trackInfo.Parts)
            {
                var part = new JObject();
                part.Add("name", partInfo.Name);
                part.Add("pos", partInfo.Pos);
                part.Add("dur", partInfo.Dur);

                if (partInfo is MidiPartInfo midiPartInfo)
                {
                    part.Add("type", "midi");
                    part.Add("gain", midiPartInfo.Gain);
                    part.Add("voice", new JObject()
                    {
                        { "type", midiPartInfo.Voice.Type },
                        { "id", midiPartInfo.Voice.ID },
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
                            var phonemes = new JArray();
                            foreach (var phonemeInfo in noteInfo.Phonemes)
                            {
                                var phoneme = new JObject();
                                phoneme.Add("startTime", phonemeInfo.StartTime);
                                phoneme.Add("endTime", phonemeInfo.EndTime);
                                phoneme.Add("symbol", phonemeInfo.Symbol);

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
