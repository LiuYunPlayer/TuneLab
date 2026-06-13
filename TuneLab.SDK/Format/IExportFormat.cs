using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK;

namespace TuneLab.SDK;

public interface IExportFormat
{
    Stream Serialize(ProjectInfo info);
}
