using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Format;

public struct ExportableFormatExtensionEntry
{
    public string FileExtension { get; set; }
    public IExportableFormat ExportableFormat { get; set; }
}
