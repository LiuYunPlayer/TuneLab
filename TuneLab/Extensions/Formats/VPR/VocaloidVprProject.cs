using DynamicData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Extensions.Formats.DataInfo;
using Point = TuneLab.Base.Structures.Point;

namespace TuneLab.Extensions.Formats.VPR
{
    [ImportFormat("vpr")]
    internal class VprWithExtension : IImportFormat
    {
        public ProjectInfo Deserialize(Stream stream)
        {
            return VprUtility.Deserialize(stream);
        }
    }

    internal static class VprUtility
    {
        public static double RangeMapper(double x, double minIn, double maxIn, double minOut, double maxOut)
        {
            return (x - minIn) * (maxOut - minOut) / (maxIn - minIn) + minOut;
        }

        static JToken? GetMatchingNote(int pos, JToken notes)
        {
            foreach (var note in notes)
            {
                if (note != null && note["pos"] != null && note["duration"] != null)
                {
                    int notePos = (int)note["pos"];
                    int noteDur = (int)note["duration"];
                    if (notePos <= pos && notePos + noteDur >= pos)
                    {
                        return note;
                    }
                }
            }
            return null;
        }

        public static ProjectInfo Deserialize(Stream stream)
        {
            JObject? vprSequence = null;

            using var archive = new System.IO.Compression.ZipArchive(stream);

            foreach (var entry in archive.Entries)
            {
                if (entry.FullName == "Project\\sequence.json" || entry.FullName == "Project/sequence.json")
                {
                    using var entryStream = entry.Open();
                    using var reader = new StreamReader(entryStream);

                    vprSequence = JsonConvert.DeserializeObject<JObject>(reader.ReadToEnd());

                    break;
                }
            }

            if (vprSequence == null)
            {
                throw new Exception("VPR file exception");
            }

            var projectInfo = new ProjectInfo();

            var masterTrack = vprSequence["masterTrack"];
            if (masterTrack != null)
            {
                var tempos = ((JArray?)masterTrack["tempo"]?["events"] ?? []);
                foreach (JObject tempo in tempos)
                {
                    var tempoInfo = new TempoInfo()
                    {
                        Pos = ((int?)tempo["pos"] ?? 0),
                        Bpm = ((int?)tempo["value"] ?? 0) / 100d,
                    };

                    projectInfo.Tempos.Add(tempoInfo);
                }

                var timeSig = ((JArray?)masterTrack["timeSig"]?["events"] ?? []);

                foreach (JObject timeSignature in timeSig)
                {
                    var timeSignatureInfo = new TimeSignatureInfo()
                    {
                        BarIndex = ((int?)timeSignature["bar"] ?? 0),
                        Numerator = ((int?)timeSignature["numer"] ?? 4),
                        Denominator = ((int?)timeSignature["denom"] ?? 4),
                    };

                    projectInfo.TimeSignatures.Add(timeSignatureInfo);
                }
            }

            var trackNum = 1;
            var tracks = ((JArray?)vprSequence["tracks"] ?? []);
            foreach (JObject track in tracks)
            {
                var importGain = ((int?)track["volume"]?["events"]?[0]?["value"] ?? 0) / 10.0d;
                var importPan = (double)((int?)track["panpot"]?["events"]?[0]?["value"] ?? 0);
                var trackInfo = new TrackInfo()
                {
                    Name = ((string?)track["name"] ?? "Track_" + trackNum.ToString()),
                    Gain = RangeMapper(importGain, -89.8, 6.0, -24.0, 24.0),
                    Pan = RangeMapper(importPan, -64.0, 64.0, -1.0, 1.0),
                    Mute = ((bool?)track["isMuted"] ?? false),
                    Solo = ((bool?)track["isSoloMode"] ?? false),
                };

                var partNum = 1;
                var parts = ((JArray?)track["parts"] ?? []);
                foreach (JObject part in parts)
                {
                    PartInfo? partInfo = null;

                    var midiPartInfo = new MidiPartInfo();

                    midiPartInfo.Properties = PropertyObject.Empty;
                    // midiPartInfo.Voice.Type = "VOCALOID" + ((string?)vprSequence["version"]?["major"] ?? "5");
                    midiPartInfo.Voice.Type = "VOCALOID5";
                    midiPartInfo.Voice.ID = ((string?)part["voice"]?["compID"] ?? "");

                    var midiEffects = ((JArray?)part["midiEffects"] ?? []);
                    var parameters = midiEffects.Cast<JObject>().FirstOrDefault(c => (string?)c["id"] == "VoiceColor")?["parameters"] ?? new JArray();

                    var notes = ((JArray?)part["notes"] ?? []);
                    foreach (JObject note in notes)
                    {
                        var properties = new JObject
                        {
                            ["Phoneme"] = ((string?)note["phoneme"] ?? "")
                        };

                        var noteInfo = new NoteInfo()
                        {
                            Pos = ((int?)note["pos"] ?? 0),
                            Dur = ((int?)note["duration"] ?? 0),
                            Pitch = ((int?)note["number"] ?? 0),
                            Lyric = ((string?)note["lyric"] ?? "a"),
                            Properties = FromJson(properties),
                        };


                        midiPartInfo.Notes.Add(noteInfo);
                    }

                    var controllers = ((JArray?)part["controllers"] ?? []);
                    var pitchBendSens = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "pitchBendSens")?["events"] ?? new JArray();
                    var pitchBend = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "pitchBend")?["events"] ?? new JArray();

                    var newPitchBend = new AutomationInfo();
                    if (pitchBend.Count() > 0 && (int)pitchBend[0]["pos"] != 0)
                    {
                        newPitchBend.Points.Add(new Point(0, 0));
                        newPitchBend.Points.Add(new Point((int)pitchBend[0]["pos"] - 1, 0));
                    }

                    for (int i = 0; pitchBend.Count() > 0 && i < pitchBend.Count(); i++)
                    {
                        if ((int)pitchBend[i]["value"] > 0)
                        {
                            if (i != 0 && (double)pitchBend[i]["pos"] - 1 != (double)pitchBend[i - 1]["pos"])
                                newPitchBend.Points.Add(new Point((double)pitchBend[i]["pos"] - 1, (double)pitchBend[i - 1]["value"] / 8191.0d));
                            newPitchBend.Points.Add(new Point((double)pitchBend[i]["pos"], (double)pitchBend[i]["value"] / 8191.0d));
                        }
                        else
                        {
                            if (i != 0 && (double)pitchBend[i]["pos"] - 1 != (double)pitchBend[i - 1]["pos"])
                                newPitchBend.Points.Add(new Point((double)pitchBend[i]["pos"] - 1, (double)pitchBend[i - 1]["value"] / 8192.0d));
                            newPitchBend.Points.Add(new Point((double)pitchBend[i]["pos"], (double)pitchBend[i]["value"] / 8192.0d));
                        }
                    }
                    midiPartInfo.Automations.Add("PitchBend", newPitchBend);

                    var newPitchBendSens = new AutomationInfo();
                    if (pitchBendSens.Count() > 0 && (int)pitchBendSens[0]["pos"] != 0)
                    {
                        newPitchBendSens.Points.Add(new Point(0, 2));
                        newPitchBendSens.Points.Add(new Point((int)pitchBendSens[0]["pos"] - 1, 2));
                    }

                    for (int i = 0; pitchBendSens.Count() > 0 && i < pitchBendSens.Count(); i++)
                    {
                        if (i != 0 && (int)pitchBendSens[i]["pos"] - 1 != (int)pitchBendSens[i - 1]["pos"])
                        {
                            newPitchBendSens.Points.Add(new Point((double)pitchBendSens[i]["pos"] - 1, (double)pitchBendSens[i - 1]["value"]));
                        }
                        newPitchBendSens.Points.Add(new Point((double)pitchBendSens[i]["pos"], (double)pitchBendSens[i]["value"]));
                    }

                    midiPartInfo.Automations.Add("PitchBendSensitive", newPitchBendSens);

                    var newDynamicsList = new AutomationInfo();
                    var dynamics = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "dynamics")?["events"] ?? new JArray();
                    if (dynamics.Count() > 0 && (int)dynamics[0]["pos"] != 0)
                    {
                        newDynamicsList.Points.Add(new Point(0, 0));
                        newDynamicsList.Points.Add(new Point((int)dynamics[0]["pos"] - 1, 0));
                    }

                    for (int i = 0; dynamics.Count() > 0 && i < dynamics.Count(); i++)
                    {
                        if (i != 0 && (int)dynamics[i]["pos"] - 1 != (int)dynamics[i - 1]["pos"])
                        {
                            newDynamicsList.Points.Add(new Point((int)dynamics[i]["pos"] - 1, RangeMapper((int)dynamics[i - 1]["value"], 0, 127, -1.0, 1.0)));
                        }
                        newDynamicsList.Points.Add(new Point((int)dynamics[i]["pos"], RangeMapper((int)dynamics[i]["value"], 0, 127, -1.0, 1.0)));
                    }
                    newDynamicsList.DefaultValue = 0.0;
                    midiPartInfo.Automations.Add("Dynamics", newDynamicsList);

                    var newBrightness = new AutomationInfo();
                    var brightness = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "brightness")?["events"] ?? new JArray();

                    if (brightness.Count() > 0 && (int)brightness[0]["pos"] != 0)
                    {
                        newBrightness.Points.Add(new Point(0, 0));
                        newBrightness.Points.Add(new Point((int)brightness[0]["pos"] - 1, 0));
                    }

                    for (int i = 0; brightness.Count() > 0 && i < brightness.Count(); i++)
                    {
                        if (i != 0 && (int)brightness[i]["pos"] - 1 != (int)brightness[i - 1]["pos"])
                        {
                            newBrightness.Points.Add(new Point((int)brightness[i]["pos"] - 1, RangeMapper((int)brightness[i - 1]["value"], 0, 127, -1.0, 1.0)));
                        }
                        newBrightness.Points.Add(new Point((int)brightness[i]["pos"], RangeMapper((int)brightness[i]["value"], 0, 127, -1.0, 1.0)));
                    }
                    newBrightness.DefaultValue = 0.0;
                    midiPartInfo.Automations.Add("Brightness", newBrightness);

                    var newCharacter = new AutomationInfo();
                    var character = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "character")?["events"] ?? new JArray();

                    if (character.Count() > 0 && (int)character[0]["pos"] != 0)
                    {
                        newCharacter.Points.Add(new Point(0, 0));
                        newCharacter.Points.Add(new Point((int)character[0]["pos"] - 1, 0));
                    }

                    for (int i = 0; character.Count() > 0 && i < character.Count(); i++)
                    {
                        if (i != 0 && (int)character[i]["pos"] - 1 != (int)character[i - 1]["pos"])
                        {
                            newCharacter.Points.Add(new Point((int)character[i]["pos"] - 1, RangeMapper((int)character[i - 1]["value"], -64, 64, -1.0, 1.0)));
                        }
                        newCharacter.Points.Add(new Point((int)character[i]["pos"], RangeMapper((int)character[i]["value"], -64, 64, -1.0, 1.0)));
                    }
                    newCharacter.DefaultValue = 0.0;
                    midiPartInfo.Automations.Add("Gender", newCharacter);

                    var newGrowl = new AutomationInfo();
                    var growl = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "growl")?["events"] ?? new JArray();
                    if (growl.Count() > 0 && (int)growl[0]["pos"] != 0)
                    {
                        newGrowl.Points.Add(new Point(0, 0));
                        newGrowl.Points.Add(new Point((int)growl[0]["pos"] - 1, 0));
                    }

                    for (int i = 0; growl.Count() > 0 && i < growl.Count(); i++)
                    {
                        if (i != 0 && (int)growl[i]["pos"] - 1 != (int)growl[i - 1]["pos"])
                        {
                            newGrowl.Points.Add(new Point((int)growl[i]["pos"] - 1, RangeMapper((int)growl[i - 1]["value"], 0, 127, 0, 1.0)));
                        }
                        newGrowl.Points.Add(new Point((int)growl[i]["pos"], RangeMapper((int)growl[i]["value"], 0, 127, 0, 1.0)));
                    }
                    newGrowl.DefaultValue = 0.0;
                    midiPartInfo.Automations.Add("Growl", newGrowl);

                    var newClearness = new AutomationInfo();
                    var clearness = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "clearness")?["events"] ?? new JArray();

                    if (clearness.Count() > 0 && (int)clearness[0]["pos"] != 0)
                    {
                        newClearness.Points.Add(new Point(0, 0));
                        newClearness.Points.Add(new Point((int)clearness[0]["pos"] - 1, 0));
                    }

                    for (int i = 0; clearness.Count() > 0 && i < clearness.Count(); i++)
                    {
                        if (i != 0 && (int)clearness[i]["pos"] - 1 != (int)clearness[i - 1]["pos"])
                        {
                            newClearness.Points.Add(new Point((int)clearness[i]["pos"] - 1, RangeMapper((int)clearness[i - 1]["value"], 0, 127, 0, 1.0)));
                        }
                        newClearness.Points.Add(new Point((int)clearness[i]["pos"], RangeMapper((int)clearness[i]["value"], 0, 127, 0, 1.0)));
                    }
                    newClearness.DefaultValue = 0.0;
                    midiPartInfo.Automations.Add("Clearness", newClearness);

                    var newExciter = new AutomationInfo();
                    var exciter = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "exciter")?["events"] ?? new JArray();

                    if (exciter.Count() > 0 && (int)exciter[0]["pos"] != 0)
                    {
                        newExciter.Points.Add(new Point(0, 0));
                        newExciter.Points.Add(new Point((int)exciter[0]["pos"] - 1, 0));
                    }

                    for (int i = 0; exciter.Count() > 0 && i < exciter.Count(); i++)
                    {
                        if (i != 0 && (int)exciter[i]["pos"] - 1 != (int)exciter[i - 1]["pos"])
                        {
                            newExciter.Points.Add(new Point((int)exciter[i]["pos"] - 1, RangeMapper((int)exciter[i - 1]["value"], -64, 63, -1.0, 1.0)));
                        }
                        newExciter.Points.Add(new Point((int)exciter[i]["pos"], RangeMapper((int)exciter[i]["value"], -64, 63, -1.0, 1.0)));
                    }
                    newExciter.DefaultValue = 0.0;
                    midiPartInfo.Automations.Add("Exciter", newExciter);

                    var newBreathiness = new AutomationInfo();
                    var breathiness = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "breathiness")?["events"] ?? new JArray();

                    if (breathiness.Count() > 0 && (int)breathiness[0]["pos"] != 0)
                    {
                        newBreathiness.Points.Add(new Point(0, 0));
                        newBreathiness.Points.Add(new Point((int)breathiness[0]["pos"] - 1, 0));
                    }

                    for (int i = 0; breathiness.Count() > 0 && i < breathiness.Count(); i++)
                    {
                        if (i != 0 && (int)breathiness[i]["pos"] - 1 != (int)breathiness[i - 1]["pos"])
                        {
                            newBreathiness.Points.Add(new Point((int)breathiness[i]["pos"] - 1, RangeMapper((int)breathiness[i - 1]["value"], 0, 127, 0, 1.0)));
                        }
                        newBreathiness.Points.Add(new Point((int)breathiness[i]["pos"], RangeMapper((int)breathiness[i]["value"], 0, 127, 0, 1.0)));
                    }
                    newBreathiness.DefaultValue = 0.0;
                    midiPartInfo.Automations.Add("Breathiness", newBreathiness);

                    var newAir = new AutomationInfo();
                    var air = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "air")?["events"] ?? new JArray();

                    if (air.Count() > 0 && (int)air[0]["pos"] != 0)
                    {
                        newAir.Points.Add(new Point(0, 0));
                        newAir.Points.Add(new Point((int)air[0]["pos"] - 1, 0));
                    }

                    for (int i = 0; air.Count() > 0 && i < air.Count(); i++)
                    {
                        if (i != 0 && (int)air[i]["pos"] - 1 != (int)air[i - 1]["pos"])
                        {
                            newAir.Points.Add(new Point((int)air[i]["pos"] - 1, RangeMapper((int)air[i - 1]["value"], 0, 127, 0, 1.0)));
                        }
                        newAir.Points.Add(new Point((int)air[i]["pos"], RangeMapper((int)air[i]["value"], 0, 127, 0, 1.0)));
                    }
                    newAir.DefaultValue = 0.0;
                    midiPartInfo.Automations.Add("Air", newAir);

                    partInfo = midiPartInfo;

                    if (partInfo != null)
                    {
                        partInfo.Name = ((string?)part["name"] ?? "Part_" + partNum);
                        partInfo.Pos = ((int?)part["pos"] ?? 0);
                        partInfo.Dur = ((int?)part["duration"] ?? 0);

                        trackInfo.Parts.Add(partInfo);
                    }

                    partNum++;
                }

                projectInfo.Tracks.Add(trackInfo);
                trackNum++;
            }

            return projectInfo;
        }

        static PropertyObject FromJson(JToken jToken)
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
    }
}
