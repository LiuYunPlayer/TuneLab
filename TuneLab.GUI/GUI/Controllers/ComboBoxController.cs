using System.Collections.Generic;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.GUI.Components;
using ToolTip = Avalonia.Controls.ToolTip;
using TuneLab.I18N;
using TuneLab.Utils;

namespace TuneLab.GUI.Controllers;

// 值模型为单一 PropertyValue box：option 值可为任意基础类型，显示用 option.ShowText()（DisplayText 缺省回退值字面量）。
// 数据驱动的 Display(value) 按值在展平叶子里反查下标以高亮对应项；用户选择则反向把该项的值发为数据改动。
// 底层是自造 DropDown（支持二级子菜单）：config 的 ComboBoxItem.SubItems 递归建成 DropDownItem.Children。
internal class ComboBoxController : DropDown, IDataValueController<PropertyValue>, IValueController<string>
{
    public IActionEvent ValueWillChange => mValueWillChange;
    public IActionEvent ValueChanged => mValueChanged;
    public IActionEvent ValueCommitted => mValueCommitted;
    public PropertyValue Value => mValue;
    // 当前选中位置（展平叶子序；扁平配置即与 Options 同序，供 FunctionBar 量化下拉等按 Index 联动业务值）。
    public int Index => SelectedIndex;

    // string 外观：供按字符串绑定的场景（Settings 的语言/驱动下拉、Select(int.Parse) 桥到 int 设置）。
    string IValueController<string>.Value => mValue.ToString() ?? string.Empty;
    void IValueController<string>.Display(string value) => Display(PropertyValue.Create(value));

    public ComboBoxController()
    {
        Height = 28;
        SelectionChanged += OnSelectionChanged;
    }

    public void SetConfig(ComboBoxConfig config)
    {
        mConfig = config;
        mLeaves.Clear();
        CollectLeaves(config.Items, mLeaves);
        // 重建选项不应走「用户改动」路径（reconcile 高频 SetConfig），屏蔽随后的选中变化。
        mAcceptSelectionChanged = false;
        SetItems(BuildItems(config.Items));
        mAcceptSelectionChanged = true;
        Display(config.DefaultOption.Value);
    }

    public void Display(PropertyValue value)
    {
        mValue = value;
        mAcceptSelectionChanged = false;
        int index = IndexOfValue(value);
        if (index >= 0)
        {
            SelectedIndex = index;
            PlaceholderForeground = null;
            WarningMarker = false;
            ToolTip.SetTip(this, null);
        }
        else
        {
            // 值不在选项内（不真正选中任何项——保持 -1 才能让点选任意项都发出选中、写进数据）：
            // 命中默认值时按默认项文本等价呈现（无差异不示警）；其余以警示色如实显示原值字面量 + 徽标。
            // 不显示成默认项、也不标「不可用」——「未知值如何生效」只有插件知道（按默认 / 认识旧值按原义 / 近似降级），
            // 宿主替它宣称任何一种都可能撒谎；数据与喂入同样保留原值不动，重选才是唯一的覆盖路径。
            bool isDefault = value.Equals(mConfig.DefaultOption.Value);
            if (isDefault)
            {
                PlaceholderText = mConfig.DefaultOption.ShowText();
                PlaceholderForeground = null;
                WarningMarker = false;
                ToolTip.SetTip(this, null);
            }
            else
            {
                string literal = value.ToString() ?? string.Empty;
                PlaceholderText = literal;
                PlaceholderForeground = Style.SYNTHESIS_DEGRADED.ToBrush();
                WarningMarker = true;
                this.SetupToolTip(string.Format("Stored value \"{0}\" is not among the current options.\nThe plugin decides how to treat it (usually as default).\nSelect an option to overwrite it.".Tr(this), literal));
            }
            SelectedIndex = -1;
        }
        mAcceptSelectionChanged = true;
    }

    public void DisplayNull()
    {
        mValue = PropertyValue.Null;
        mAcceptSelectionChanged = false;
        PlaceholderText = string.Empty;
        PlaceholderForeground = null;
        WarningMarker = false;
        ToolTip.SetTip(this, null);
        SelectedIndex = -1;
        mAcceptSelectionChanged = true;
    }

    public void DisplayMultiple()
    {
        mValue = PropertyValue.Null;
        mAcceptSelectionChanged = false;
        PlaceholderText = "(Multiple)";
        PlaceholderForeground = null;
        WarningMarker = false;
        ToolTip.SetTip(this, null);
        SelectedIndex = -1;
        mAcceptSelectionChanged = true;
    }

    static List<DropDownItem> BuildItems(IReadOnlyList<ComboBoxItem> options)
    {
        var items = new List<DropDownItem>(options.Count);
        foreach (var option in options)
        {
            if (option.IsSeparator)
                items.Add(new DropDownItem() { Text = option.DisplayText ?? string.Empty, IsSeparator = true });
            else if (option.IsGroup)
                items.Add(new DropDownItem() { Text = option.ShowText(), Children = BuildItems(option.SubItems!) });
            else
                items.Add(new DropDownItem() { Text = option.ShowText(), Tag = option.Value });
        }
        return items;
    }

    // 展平叶子（DFS，分组展开、跳过分隔线）——与 DropDown 内部展平同序，故 SelectedIndex 在两侧一致。
    static void CollectLeaves(IReadOnlyList<ComboBoxItem> options, List<ComboBoxItem> leaves)
    {
        foreach (var option in options)
        {
            if (option.IsSeparator)
                continue;
            else if (option.IsGroup)
                CollectLeaves(option.SubItems!, leaves);
            else
                leaves.Add(option);
        }
    }

    int IndexOfValue(PropertyValue value)
    {
        for (int i = 0; i < mLeaves.Count; i++)
        {
            if (mLeaves[i].Value.Equals(value))
                return i;
        }
        return -1;
    }

    bool mAcceptSelectionChanged = true;
    void OnSelectionChanged(object? sender, System.EventArgs e)
    {
        if (!mAcceptSelectionChanged)
            return;

        SetValue(SelectedTag is PropertyValue value ? value : mConfig.DefaultOption.Value);
    }

    void SetValue(PropertyValue value)
    {
        if (value.Equals(mValue))
            return;

        mValueWillChange.Invoke();
        mValue = value;
        mValueChanged.Invoke();
        mValueCommitted.Invoke();
    }

    readonly ActionEvent mValueWillChange = new();
    readonly ActionEvent mValueChanged = new();
    readonly ActionEvent mValueCommitted = new();

    ComboBoxConfig mConfig = ComboBoxConfig.Create([]);
    readonly List<ComboBoxItem> mLeaves = new();
    PropertyValue mValue = PropertyValue.Null;
}
