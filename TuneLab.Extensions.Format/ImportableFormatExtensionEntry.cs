using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TuneLab.Extensions.Format;

public struct ImportableFormatExtensionEntry
{
    public string FileExtension { get; set; }
    public IImportableFormat ImportableFormat { get; set; }
}
