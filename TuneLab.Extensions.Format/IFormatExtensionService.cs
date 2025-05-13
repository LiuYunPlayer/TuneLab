using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation.DataStructures;

namespace TuneLab.Extensions.Format;

public interface IFormatExtensionService
{
    IReadOnlyOrderedMap<string, IImportableFormat> ImportableFormats { get; }
    IReadOnlyOrderedMap<string, IExportableFormat> ExportableFormats { get; }
    void Load();
}
