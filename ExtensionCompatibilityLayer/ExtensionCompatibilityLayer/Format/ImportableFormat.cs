using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Core.DataInfo;
using TuneLab.Extensions.Format;
using TuneLab.Extensions.Formats;

namespace ExtensionCompatibilityLayer.Format;

internal class ImportableFormat(IImportFormat importFormat) : IImportableFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        throw new NotImplementedException();
    }
}
