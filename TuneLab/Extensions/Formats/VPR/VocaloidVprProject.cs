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
                        Pos = ((int?)tempo["pos"] ?? 0) / 100d,
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
                    foreach (JObject pb in pitchBend)
                    { 
                        newPitchBend.Points.Add(new Point((double)pb["pos"], (double)pb["value"] / 8192.0d));
                    }
                    midiPartInfo.Automations.Add("PitchBend", newPitchBend);

                    var newPitchBendSens = new AutomationInfo();
                    foreach (JObject pbs in pitchBendSens)
                    { 
                        newPitchBendSens.Points.Add(new Point((double)pbs["pos"], (double)pbs["value"]));
                    }
                    midiPartInfo.Automations.Add("PitchBendSensitive", newPitchBendSens);

                    var newDynamicsList = new AutomationInfo();
                    var dynamics = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "dynamics")?["events"] ?? new JArray();
                    foreach (JObject dyn in dynamics)
                    {
                        newDynamicsList.Points.Add(new Point((int)dyn["pos"], RangeMapper((int)dyn["value"], 0, 127, -1.0, 1.0)));
                    }
                    newDynamicsList.DefaultValue = 0.0;

                    if (newDynamicsList.Points.Count > 0)
                        midiPartInfo.Automations.Add("Dynamics", newDynamicsList);

                    var newBrightness = new AutomationInfo();
                    var brightness = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "brightness")?["events"] ?? new JArray();

                    foreach (JObject bri in brightness)
                    {
                        newBrightness.Points.Add(new Point((int)bri["pos"], RangeMapper((int)bri["value"], 0, 127, -1.0, 1.0)));
                    }
                    var defaultBrightness = parameters.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "Breathiness")?["value"];
                    newBrightness.DefaultValue = defaultBrightness != null ? RangeMapper((double)defaultBrightness, 0, 127, -1.0, 1.0) : 0.0;

                    if (newBrightness.Points.Count > 0)
                        midiPartInfo.Automations.Add("Brightness", newBrightness);

                    var newCharacter = new AutomationInfo();
                    var character = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "character")?["events"] ?? new JArray();

                    foreach (JObject chr in character)
                    {
                        newCharacter.Points.Add(new Point((int)chr["pos"], RangeMapper((int)chr["value"], -64, 64, -1.0, 1.0)));
                    }
                    newCharacter.DefaultValue = 0.0;

                    if (newCharacter.Points.Count > 0)
                        midiPartInfo.Automations.Add("Gender", newCharacter);

                    var newGrowl = new AutomationInfo();
                    var growl = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "growl")?["events"] ?? new JArray();
                    foreach (JObject chr in growl)
                    {
                        newGrowl.Points.Add(new Point((int)chr["pos"], RangeMapper((int)chr["value"], 0, 127, -1.0, 1.0)));
                    }
                    var defaultGrowl = parameters.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "Growl")?["value"];
                    newGrowl.DefaultValue = defaultGrowl != null ? RangeMapper((double)defaultGrowl, 0, 127, -1.0, 1.0) : 0.0;

                    if (newGrowl.Points.Count > 0)
                        midiPartInfo.Automations.Add("Growl", newGrowl);

                    var newClearness = new AutomationInfo();
                    var clearness = controllers.Cast<JObject>().FirstOrDefault(c => (string?)c["name"] == "clearness")?["events"] ?? new JArray();
                    foreach (JObject chr in clearness)
                    {
                        newClearness.Points.Add(new Point((int)chr["pos"], RangeMapper((int)chr["value"], 0, 127, -1.0, 1.0)));
                    }
                    newClearness.DefaultValue = 0.0;

                    if (newClearness.Points.Count > 0)
                        midiPartInfo.Automations.Add("Clearness", newClearness);

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
