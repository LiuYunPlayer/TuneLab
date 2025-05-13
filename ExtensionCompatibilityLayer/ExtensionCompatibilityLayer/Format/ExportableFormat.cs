using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Core.DataInfo;
using TuneLab.Extensions.Format;
using TuneLab.Extensions.Formats;

namespace ExtensionCompatibilityLayer.Format;

internal class ExportableFormat(IExportFormat exportFormat) : IExportableFormat
{
    public Stream Serialize(ProjectInfo info)
    {
        throw new NotImplementedException();
    }
}
