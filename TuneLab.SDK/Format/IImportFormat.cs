using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK;

namespace TuneLab.SDK;

// 加性约定（插件实现面）：将来在本面新增成员一律用默认接口方法（DIM）给兜底体，使增补不破已装插件。
public interface IImportFormat
{
    // 从宿主提供的 stream 读出并反序列化工程。宿主拥有 stream 生命周期，插件只读、不 Dispose。
    ProjectInfo Deserialize(Stream stream);
}
