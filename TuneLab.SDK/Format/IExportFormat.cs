using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.SDK;

namespace TuneLab.SDK;

// 加性约定（插件实现面）：将来在本面新增成员一律用默认接口方法（DIM）给兜底体，使增补不破已装插件。
public interface IExportFormat
{
    // 把工程序列化写入宿主提供的 output 流。宿主拥有并负责 output 的生命周期（创建、定位、关闭）——
    // 插件只从当前位置顺序写入，不得 Dispose / Close / Seek / 重置 Position。与 IImportFormat.Deserialize
    // 对称（宿主给流、插件读/写），省去插件全量缓冲进 MemoryStream 再返回、以及流所有权/Position 的歧义。
    void Serialize(Stream output, ProjectInfo info);
}
