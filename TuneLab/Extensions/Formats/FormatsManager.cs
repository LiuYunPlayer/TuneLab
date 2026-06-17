using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

using TuneLab.Extensions.Formats;
namespace TuneLab.Extensions.Formats;

internal static class FormatsManager
{
    // 内建格式显式注册（编进宿主、无 description.json，故不走 manifest，直接登记）。
    public static void LoadBuiltIn()
    {
        RegisterImporter("tlp", "TuneLab Project", () => new TLP.TuneLabProject());
        RegisterExporter("tlp", "TuneLab Project", () => new TLP.TuneLabProject());
        RegisterImporter("tlpx", "TuneLab Project (CBOR)", () => new TLP.TuneLabProjectCbor());
        RegisterExporter("tlpx", "TuneLab Project (CBOR)", () => new TLP.TuneLabProjectCbor());
        RegisterImporter("acep", "ACE Studio Project", () => new ACEP.ACEStudioProject());
        RegisterExporter("acep", "ACE Studio Project", () => new ACEP.ACEStudioProject());
        RegisterImporter("ufdata", "UtaFormatix Data", () => new UFData.UtaFormatixV1Data());
        RegisterExporter("ufdata", "UtaFormatix Data", () => new UFData.UtaFormatixV1Data());
        RegisterImporter("mid", "MIDI", () => new Midi.MidiWithExtension_mid());
        RegisterImporter("midi", "MIDI", () => new Midi.MidiWithExtension_midi());
        RegisterImporter("vpr", "VOCALOID Project", () => new VPR.VprWithExtension());
    }

    // 工厂注册导入器：内建（LoadBuiltIn）、V1（ExtensionManager 按 manifest class 实例化）、
    // Compat.Legacy（经 LegacyLoadHook → LegacyCompatLoader 包装老插件）三条路径共用。
    // fileExtension 是不可变身份 id（路由 + 工程序列化引用）；displayName 仅供 UI 展示、可本地化。
    // 内建/先到优先：扩展名已存在则跳过（不让 Legacy 覆盖内建格式）。
    public static void RegisterImporter(string fileExtension, string displayName, Func<IImportFormat> factory)
    {
        if (!mImportFormats.ContainsKey(fileExtension))
            mImportFormats.Add(fileExtension, factory);
        RecordDisplayName(fileExtension, displayName);
    }

    public static void RegisterExporter(string fileExtension, string displayName, Func<IExportFormat> factory)
    {
        if (!mExportFormats.ContainsKey(fileExtension))
            mExportFormats.Add(fileExtension, factory);
        RecordDisplayName(fileExtension, displayName);
    }

    // 显示名按扩展名记录（import/export 同扩展名共一个名）；先到优先、空名不覆盖已有。
    static void RecordDisplayName(string fileExtension, string displayName)
    {
        if (!string.IsNullOrEmpty(displayName) && !mFormatNames.ContainsKey(fileExtension))
            mFormatNames[fileExtension] = displayName;
    }

    // UI 展示名（本地化，注册时按当前语言定）；未登记回退到扩展名本身。
    public static string GetDisplayName(string fileExtension)
        => mFormatNames.TryGetValue(fileExtension, out var name) ? name : fileExtension;

    public static IReadOnlyList<string> GetAllImportFormats()
    {
        return mImportFormats.Keys;
    }

    public static IReadOnlyList<string> GetAllExportFormats()
    {
        return mExportFormats.Keys;
    }

    public static bool Deserialize(string filePath, [NotNullWhen(true)] out ProjectInfo? projectInfo, [NotNullWhen(false)] out string? error)
    {
        projectInfo = null;
        error = null;
       
        try
        {
            var fileInfo = new FileInfo(filePath);

            var format = fileInfo.Extension.TrimStart('.');
            if (!mImportFormats.ContainsKey(format))
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            var stream = File.OpenRead(filePath);
            IImportFormat importFormat = mImportFormats[format].Invoke();
            projectInfo = importFormat.Deserialize(stream);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    public static bool Serialize(ProjectInfo info, string format, [NotNullWhen(true)] out Stream? stream, [NotNullWhen(false)] out string? error)
    {
        stream = null;
        error = null;
        
        try
        {
            if (!mExportFormats.ContainsKey(format))
            {
                throw new Exception(string.Format("Format {0} is not support!", format));
            }

            IExportFormat exportFormat = mExportFormats[format].Invoke();
            stream = exportFormat.Serialize(info);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    static OrderedMap<string, Func<IImportFormat>> mImportFormats = new();
    static OrderedMap<string, Func<IExportFormat>> mExportFormats = new();
    static readonly Dictionary<string, string> mFormatNames = new();
}
