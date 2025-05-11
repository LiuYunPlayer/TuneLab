using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TuneLab.Core.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Property;

namespace TuneLab.Extensions.Format.BuiltIn;

[ImportableFormat]
[ExportableFormat]
internal class UtaFormatixV1Data : IImportableFormat, IExportableFormat
{
    public string FileExtension => "ufdata";
    public ProjectInfo Deserialize(Stream stream)
    {
        return (ProjectInfo)UfDataUtility.Deserialize(stream);
    }

    public Stream Serialize(ProjectInfo projectInfo)
    {
        return UfDataUtility.Serialize(projectInfo);
    }

    internal static class UfDataUtility
    {
        public static object Deserialize(Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                string content = reader.ReadToEnd();

                var projectInfo = new ProjectInfo();
                JObject? project = JsonConvert.DeserializeObject<JObject>(content);
                if (project == null)
                    return null;

                var tempos = project["project"]?["tempos"]?.ToArray();
                foreach (JObject tempo in tempos)
                {
                    var tempoInfo = new TempoInfo()
                    {
                        Pos = (double)tempo["tickPosition"],
                        Bpm = (double)tempo["bpm"]
                    };

                    projectInfo.Tempos.Add(tempoInfo);
                }

                var timeSignatures = project["project"]?["timeSignatures"]?.ToArray();
                foreach (JObject timeSignature in timeSignatures)
                {
                    var timeSignatureInfo = new TimeSignatureInfo()
                    {
                        BarIndex = (int)timeSignature["measurePosition"],
                        Numerator = (int)timeSignature["numerator"],
                        Denominator = (int)timeSignature["denominator"],
                    };

                    projectInfo.TimeSignatures.Add(timeSignatureInfo);
                }

                var trackNum = 1;
                var tracks = project["project"]?["tracks"]?.ToArray();
                foreach (JObject track in tracks)
                {
                    var trackInfo = new TrackInfo()
                    {
                        Name = (string)track["name"] ?? $"Track_{trackNum}",
                        Gain = 0,
                        Pan = 0,
                        Mute = false,
                        Solo = false
                    };
                    var midiPartInfo = new MidiPartInfo();
                    midiPartInfo.Voice.Type = "";
                    midiPartInfo.Voice.ID = "";

                    var notes = track["notes"]?.ToArray();
                    foreach (JObject note in notes)
                    {
                        var properties = new JObject
                        {
                            ["Phoneme"] = (string?)note["phoneme"] ?? ""
                        };

                        var noteInfo = new NoteInfo()
                        {
                            Pos = (int?)note["tickOn"] ?? 0,
                            Dur = ((int?)note["tickOff"] ?? 0) - ((int?)note["tickOn"] ?? 0),
                            Pitch = (int?)note["key"] ?? 0,
                            Lyric = (string?)note["lyric"] ?? "a",
                            Properties = FromJson(properties),
                        };

                        midiPartInfo.Notes.Add(noteInfo);
                    }

                    JObject? pitch = (JObject)track["pitch"];
                    if (pitch != null)
                    {
                        var pitchTicks = pitch?["ticks"]?.ToArray();
                        var pitchValues = pitch?["values"]?.ToArray();

                        if ((bool)pitch?["isAbsolute"] == false)
                        {
                            var pitchBend = new AutomationInfo();
                            for (int i = 0; i < pitchTicks.Count(); i++)
                            {
                                pitchBend.Points.Add(new Point((double)pitchTicks[i], (double)pitchValues[i]));
                            }
                            midiPartInfo.Automations.Add("PitchBend", pitchBend);
                        }

                        if ((bool)pitch?["isAbsolute"] == true)
                        {
                            var pitchBend = new List<Point>();
                            double prevPos = 0;
                            for (int i = 0; i < pitchTicks.Count(); i++)
                            {
                                if ((double)pitchTicks[i] - (double)prevPos >= 480)
                                {
                                    midiPartInfo.Pitch.Add(pitchBend);
                                    pitchBend = [new Point((double)pitchTicks[i], (double)pitchValues[i])];
                                }
                                else
                                {
                                    pitchBend.Add(new Point((double)pitchTicks[i], (double)pitchValues[i]));
                                }

                                prevPos = (double)pitchTicks[i];
                            }
                            midiPartInfo.Pitch.Add(pitchBend);
                        }
                    }

                    PartInfo? partInfo = midiPartInfo;

                    if (partInfo != null)
                    {
                        partInfo.Name = "Part_1";
                        partInfo.Pos = 0;
                        partInfo.Dur = (int)notes[notes.Count() - 1]?["tickOff"] != null ? (int)notes[notes.Count() - 1]?["tickOff"] + 480 : int.MaxValue;

                        trackInfo.Parts.Add(partInfo);
                    }

                    projectInfo.Tracks.Add(trackInfo);

                    trackNum++;
                }

                return projectInfo;
            }
        }

        public static Stream Serialize(ProjectInfo projectInfo)
        {
            var project = new JObject
            {
                { "formatVersion", 1 }
            };

            var tracks = new JArray();
            var tempos = new JArray();
            var timeSignatures = new JArray();

            foreach (var trackInfo in projectInfo.Tracks)
            {
                var trackObj = new JObject
                {
                    ["name"] = trackInfo.Name
                };

                foreach (var partInfo in trackInfo.Parts)
                {
                    if (partInfo is MidiPartInfo midiPartInfo)
                    {
                        var notes = new JArray();
                        foreach (var noteInfo in midiPartInfo.Notes)
                        {
                            var note = new JObject();
                            note.Add("tickOn", (int)noteInfo.Pos);
                            note.Add("tickOff", (int)(noteInfo.Dur + noteInfo.Pos));
                            note.Add("key", noteInfo.Pitch);
                            note.Add("lyric", noteInfo.Lyric);
                            note.Add("phoneme", ToJson(noteInfo.Properties)["Phoneme"] ?? "a");

                            notes.Add(note);
                        }
                        trackObj.Add("notes", notes);

                        var pitch = new JObject();
                        var ticks = new JArray();
                        var values = new JArray();
                        foreach (var pointList in midiPartInfo.Pitch)
                        {
                            foreach (var point in pointList)
                            {
                                ticks.Add((int)point.X);
                                values.Add(point.Y);
                            }
                        }

                        pitch.Add("ticks", ticks);
                        pitch.Add("values", values);
                        pitch.Add("isAbsolute", true);

                        trackObj.Add("pitch", pitch);
                    }

                    tracks.Add(trackObj);
                }
            }

            foreach (var tempoInfo in projectInfo.Tempos)
            {
                var tempo = new JObject()
                        {
                            { "tickPosition", (int)tempoInfo.Pos },
                            { "bpm", (int)tempoInfo.Bpm }
                        };

                tempos.Add(tempo);
            }

            foreach (var timeSignatureInfo in projectInfo.TimeSignatures)
            {
                var timeSignature = new JObject()
                        {
                            { "measurePosition", timeSignatureInfo.BarIndex },
                            { "numerator", timeSignatureInfo.Numerator },
                            { "denominator", timeSignatureInfo.Denominator }
                        };

                timeSignatures.Add(timeSignature);
            }

            var ufProject = new JObject
                {
                    { "name", "Project" },
                    { "tracks", tracks },
                    { "tempos", tempos },
                    { "timeSignatures", timeSignatures },
                    { "measurePrefix", 4 }
                };

            project.Add("project", ufProject);

            return new MemoryStream(Encoding.UTF8.GetBytes(project.ToString(Formatting.None)));
        }
    }

    static PropertyObject FromJson(JToken jToken)
    {
        var map = new Map<string, IPropertyValue>();

        foreach (JProperty property in jToken.Children())
        {
            var key = property.Name;
            var value = property.Value;
            switch (value.Type)
            {
                case JTokenType.Boolean:
                    map.Add(key, new PropertyBoolean((bool)value));
                    break;
                case JTokenType.Integer:
                    map.Add(key, new PropertyNumber((int)value));
                    break;
                case JTokenType.Float:
                    map.Add(key, new PropertyNumber((double)value));
                    break;
                case JTokenType.String:
                    map.Add(key, new PropertyString((string)value!));
                    break;
                case JTokenType.Object:
                    map.Add(key, FromJson(value));
                    break;
            }
        }
        return new(map);
    }

    static JObject ToJson(IMap<string, IPropertyValue> properties)
    {
        var json = new JObject();
        foreach (var property in properties)
        {
            var key = property.Key;
            var value = property.Value;
            if (value.ToObject(out var propertyObject))
            {
                json.Add(key, ToJson(propertyObject));
            }
            else if (value.ToBoolean(out var boolValue))
            {
                json.Add(key, boolValue);
            }
            else if (value.ToNumber(out var doubleValue))
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
