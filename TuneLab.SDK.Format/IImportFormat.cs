using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK.Format.DataInfo;

namespace TuneLab.SDK.Format;

public interface IImportFormat
{
    ProjectInfo Deserialize(Stream stream);
}
