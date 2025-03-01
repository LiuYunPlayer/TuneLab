using DynamicData;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TuneLab.Base.Properties;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Foundation.DataStructures;
using ZstdSharp;

namespace TuneLab.Extensions.Formats.ACEP;

[ImportFormat("acep")]
[ExportFormat("acep")]
internal class ACEStudioProject : IImportFormat, IExportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        return (ProjectInfo)ACEPUtility.Deserialize(stream);
    }

    public Stream Serialize(ProjectInfo projectInfo)
    {
        return ACEPUtility.Serialize(projectInfo);
    }

    internal static class ACEPUtility
    {
        public static ProjectInfo Deserialize(Stream stream)
        {
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                string content = reader.ReadToEnd();

                var projectInfo = new ProjectInfo();
                JObject? project = JsonConvert.DeserializeObject<JObject>(content);
                if (project == null)
                    return null;

                if ((string)project["compressMethod"] == "zstd")
                {
                    byte[] src = Convert.FromBase64String((string)project["content"]);
                    using var decompressor = new Decompressor();
                    var decompressed = decompressor.Unwrap(src);
                    JObject? acepJson = JsonConvert.DeserializeObject<JObject>(Encoding.UTF8.GetString(decompressed));

                    var tempos = acepJson["tempos"]?.ToArray();
                    foreach (JObject tempo in tempos)
                    {
                        var tempoInfo = new TempoInfo()
                        {
                            Pos = (double)tempo["position"],
                            Bpm = (double)tempo["bpm"]
                        };

                        projectInfo.Tempos.Add(tempoInfo);
                    }

                    var timeSignatures = acepJson["timeSignatures"]?.ToArray();
                    if (timeSignatures != null)
                    {
                        foreach (JObject timeSignature in timeSignatures)
                        {
                            var timeSignatureInfo = new TimeSignatureInfo()
                            {
                                BarIndex = (int)timeSignature["barPos"],
                                Numerator = (int)timeSignature["numerator"],
                                Denominator = (int)timeSignature["denominator"],
                            };

                            projectInfo.TimeSignatures.Add(timeSignatureInfo);
                        }
                    }

                    var trackNum = 1;
                    var tracks = acepJson["tracks"]?.ToArray();
                    if (tracks != null)
                    {
                        foreach (JObject track in tracks)
                        {
                            if ((string)track["type"] == "audio")
                            {
                                continue;
                            }

                            var trackInfo = new TrackInfo()
                            {
                                Name = (string)track["name"] ?? $"Track_{trackNum}",
                                Gain = (double)track["gain"],
                                Pan = (double)track["pan"],
                                Mute = (bool)track["mute"],
                                Solo = (bool)track["solo"],
                            };

                            var partNum = 1;
                            var parts = ((JArray?)track["patterns"] ?? []);
                            foreach (JObject part in parts)
                            {
                                PartInfo? partInfo = null;
                                var midiPartInfo = new MidiPartInfo();
                                midiPartInfo.Properties = PropertyObject.Empty;
                                midiPartInfo.Voice.Type = "";
                                midiPartInfo.Voice.ID = "";

                                partInfo = midiPartInfo;

                                var notes = ((JArray?)part["notes"] ?? []);
                                foreach (JObject note in notes)
                                {
                                    var noteInfo = new NoteInfo()
                                    {
                                        Pos = ((int?)note["pos"] ?? 0),
                                        Dur = ((int?)note["dur"] ?? 0),
                                        Pitch = ((int?)note["pitch"] ?? 0),
                                        Lyric = ((string?)note["lyric"] ?? "a"),
                                        Properties = PropertyObject.Empty,
                                    };

                                    midiPartInfo.Notes.Add(noteInfo);
                                }

                                var pitchDelta = (JArray?)part["parameters"]?["pitchDelta"] ?? new JArray();
                                foreach (JObject pd in pitchDelta)
                                {
                                    var pitch = pd["points"] ?? new JArray();
                                    var line = new List<Point>();
                                    bool flag = false;
                                    double x = 0;
                                    foreach (double value in pitch)
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

                                if (partInfo != null)
                                {
                                    partInfo.Name = ((string?)part["name"] ?? "Part_" + partNum);
                                    partInfo.Pos = ((int?)part["pos"] ?? 0);
                                    partInfo.Dur = ((int?)part["dur"] ?? 0);
                                    trackInfo.Parts.Add(partInfo);
                                }

                                partNum++;
                            }
                            projectInfo.Tracks.Add(trackInfo);
                            trackNum++;
                        }
                    }
                }

                return projectInfo;
            }
        }

        public static Stream Serialize(ProjectInfo projectInfo)
        {
            var project = new JObject
            {
                { "compressMethod", "zstd" },
                { "salt", "" },
                { "version", 1000 }
            };

            var acepData = new JObject()
            {
                { "colorIndex", 4 },
                { "extraInfo", new JObject() },
                { "loop", false },
                { "loopEnd", 0 },
                { "loopStart", 0 },
                { "master", new JObject() { { "gain", 0 } } },
                { "mergedPatternIndex", 0 },
                { "patternIndividualColorIndex", 7 },
                { "pianoCells", 2147483646 },
                { "recordPatternIndex", 0 },
                { "singer_library_id", "1200047389" },
                { "trackCells", 2147483646 },
                { "trackControlPanelW", 0 },
                { "version", 6 }
            };

            var tempos = new JArray();
            foreach (var tempoInfo in projectInfo.Tempos)
            {
                var tempo = new JObject()
                {
                    { "bpm", (int)tempoInfo.Bpm },
                    { "isLerp", false },
                    { "position", (int)tempoInfo.Pos }
                };

                tempos.Add(tempo);
            }
            acepData.Add("tempos", tempos);

            var timeSignatures = new JArray();
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
            acepData.Add("timeSignatures", timeSignatures);

            var maxTrackLength = 0;
            var trackNum = 0;
            var tracks = new JArray();
            foreach (var trackInfo in projectInfo.Tracks)
            {
                var track = new JObject()
                {
                    { "channel", trackNum },
                    { "color", "#c0cf63" },
                    { "extraInfo", new JObject() },
                    { "gain", (int)trackInfo.Gain },
                    { "language", "CHN" },
                    { "listen", false },
                    { "mute", trackInfo.Mute },
                    { "name", trackInfo.Name },
                    { "pan", (int)trackInfo.Pan },
                    { "record", false },
                    { "solo", trackInfo.Solo },
                    { "type", "sing" },
                };

                var patterns = new JArray();
                foreach (var partInfo in trackInfo.Parts)
                {
                    if (partInfo is MidiPartInfo midiPartInfo)
                    {
                        var pattern = new JObject()
                        {
                            { "clipDur", partInfo.Dur },
                            { "clipPos", 0 },
                            { "color", "" },
                            { "dur", partInfo.Dur },
                            { "pos", partInfo.Pos },
                            { "enabled", true },
                            { "extraInfo", new JObject() },
                            { "name", partInfo.Name },
                        };

                        var notes = new JArray();
                        foreach (var noteInfo in midiPartInfo.Notes)
                        {
                            var note = new JObject()
                            {
                                { "brLen", 0 },
                                { "dur", noteInfo.Dur },
                                { "extraInfo", new JObject() },
                                { "headConsonants", new JArray() },
                                { "language", "CHN" },
                                { "lyric", noteInfo.Lyric },
                                { "pitch", noteInfo.Pitch },
                                { "pos", noteInfo.Pos },
                                { "syllable", "" },
                                { "tailConsonants", new JArray() },
                            };
                            notes.Add(note);
                        }
                        pattern.Add("notes", notes);

                        var parameters = new JObject()
                        {
                            { "breathiness", new JArray() },
                            { "energy", new JArray() },
                            { "falsetto", new JArray() },
                            { "gender", new JArray() },
                            { "realBreathiness", new JArray() },
                            { "realEnergy", new JArray() },
                            { "realFalsetto", new JArray() },
                            { "realTension", new JArray() },
                            { "tension", new JArray() },
                            { "vuv", new JArray() },
                        };

                        var pitchDelta = new JArray();
                        foreach (var line in midiPartInfo.Pitch)
                        {
                            var pitchDataItem = new JObject()
                            {
                                { "type", "anchor" }
                            };

                            var points = new JArray();
                            foreach (var point in line)
                            {
                                points.Add(point.X.ToString("#0.000"));
                                points.Add(point.Y.ToString("#0.000"));
                            }
                            pitchDataItem.Add("points", points);

                            JArray pointsVUV = [0, 0, 0, 0, 0];
                            pitchDataItem.Add("pointsVUV", pointsVUV);


                            pitchDelta.Add(pitchDataItem);
                        }
                        parameters.Add("pitchDelta", pitchDelta);

                        pattern.Add("parameters", parameters);
                        patterns.Add(pattern);
                        track.Add("patterns", patterns);
                    }

                    maxTrackLength = (int)(partInfo.Pos + partInfo.Dur + 480 > maxTrackLength ? partInfo.Pos + partInfo.Dur + 480 : maxTrackLength);
                }

                tracks.Add(track);
                trackNum++;
            }
            acepData.Add("duration", maxTrackLength);
            acepData.Add("tracks", tracks);

            string acepDataJson = JsonConvert.SerializeObject(acepData, Formatting.None);
            byte[] acepDataJsonBytes = Encoding.UTF8.GetBytes(acepDataJson);

            using var compressor = new Compressor(22);
            var compressed = compressor.Wrap(acepDataJsonBytes);

            project.Add("content", Convert.ToBase64String(compressed));

            return new MemoryStream(Encoding.UTF8.GetBytes(project.ToString(Formatting.None)));
        }
    }
}
