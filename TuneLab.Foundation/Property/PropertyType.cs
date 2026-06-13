namespace TuneLab.Foundation;

// 值模型的类型标签。Array 段留待属性面板的树模型落地，当前不产出。
// Multiple 是多选编辑的聚合态（非具体值），瞬态、永不序列化。
public enum PropertyType
{
    Null,
    Boolean,
    Number,
    String,
    Array,
    Object,
    Multiple,
}
