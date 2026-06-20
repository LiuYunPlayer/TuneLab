using Avalonia.Controls;
using Avalonia.Layout;
using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.GUI.Controllers;

// 配置驱动的属性面板：按 ObjectConfig 渲染控件，逐字段绑定到 IDataPropertyObject 的可绑定字段，
// 复用单属性的撤销/刷新/提交机制。控件类型经注册表分发，加新控件只需注册一项；嵌套对象经 Object(key) 逐层导航。
//
// 支持 keyed-diff reconcile（条件属性面板）：config 可随数据值变化，调用方在值 commit 后重算整棵 config 再调
// Reconcile，本控件按 key diff——同 key 同类型复用控件仅更参数（不闪、不丢焦点）、key 消失则 dispose、新 key 建、
// 同 key 换类型则换控件。纯参数变化不重排布局；仅结构（增删/换类型/顺序）变化时重排。订阅/重算时机由调用方管理。
internal class PropertyObjectController : StackPanel
{
    public PropertyObjectController()
    {
        Background = Style.INTERFACE.ToBrush();
    }

    // 绑定到（新的）数据对象并对齐到 config。数据对象切换时必须重建全部控件——复用的 creator 仍绑在旧对象的字段上，
    // 故先 ResetConfig 清空再全新建。静态用法（effect/嵌套）= 仅调一次；动态用法（条件面板）在数据对象不变时改调 Reconcile。
    public void SetConfig(ObjectConfig config, IDataPropertyObject dataObject)
    {
        ResetConfig();
        mDataObject = dataObject;
        Reconcile(config);
    }

    // 数据对象不变，把控件树 keyed-diff 到新 config。
    public void Reconcile(ObjectConfig config)
    {
        if (mDataObject == null)
            return;

        var nextOrder = new List<string>();
        var nextByKey = new Dictionary<string, Creator>();
        bool structureChanged = false;

        foreach (var kvp in config.Properties)
        {
            var key = kvp.Key;
            var cfg = kvp.Value;
            if (nextByKey.ContainsKey(key))
                continue; // ObjectConfig 是 map，key 本应唯一；防御重复

            if (mCreatorsByKey.TryGetValue(key, out var existing) && existing.ConfigType == cfg.GetType())
            {
                existing.Update(cfg);
                mCreatorsByKey.Remove(key);
                nextByKey.Add(key, existing);
            }
            else
            {
                if (!mFactories.TryGetValue(cfg.GetType(), out var factory))
                {
                    Log.Error($"No controller found for config type: {cfg.GetType()}");
                    continue;
                }
                nextByKey.Add(key, factory(this, key, cfg));
                structureChanged = true; // 新建（含同 key 换类型）
            }
            nextOrder.Add(key);
        }

        if (mCreatorsByKey.Count > 0)
            structureChanged = true; // 有 key 被删
        if (!mOrder.SequenceEqual(nextOrder))
            structureChanged = true; // 顺序变

        if (structureChanged)
        {
            // 先把被删 creator 的视图移出可视树，再 dispose（归还控件 / 分隔符到池）。
            foreach (var kvp in mCreatorsByKey)
            {
                foreach (var view in kvp.Value.LayoutViews)
                    Children.Remove(view);
                kvp.Value.Dispose();
            }
            mCreatorsByKey = nextByKey;
            mOrder = nextOrder;
            AlignChildren();
        }
        else
        {
            // 纯参数更新：控件原位不动（复用项已 Update），仅刷新内部簿记引用。
            mCreatorsByKey = nextByKey;
            mOrder = nextOrder;
        }
    }

    public void ResetConfig()
    {
        Children.Clear();
        foreach (var kvp in mCreatorsByKey)
            kvp.Value.Dispose();
        mCreatorsByKey = new();
        mOrder = new();
        mDataObject = null;
    }

    // 增量对齐 Children 到目标顺序（mOrder 各 creator 的 LayoutViews 拼接）：已在正确位置的复用控件**不动**
    //（留在可视树、保持焦点 / 编辑态），仅移动错位的、插入新建的；尾部多余兜底移除。
    void AlignChildren()
    {
        int index = 0;
        foreach (var key in mOrder)
        {
            foreach (var view in mCreatorsByKey[key].LayoutViews)
            {
                int current = Children.IndexOf(view);
                if (current == index)
                {
                    index++;
                    continue;
                }
                if (current >= 0)
                    Children.RemoveAt(current);
                Children.Insert(index, view);
                index++;
            }
        }
        while (Children.Count > index)
            Children.RemoveAt(Children.Count - 1);
    }

    IDataPropertyObject DataObject => mDataObject ?? throw new InvalidOperationException("PropertyObjectController has no data object.");

    // creator 不自行加入 Parent.Children；创建控件、绑定数据、暴露 Views，布局由 AlignChildren 增量管理。
    // 每个 creator 自带一个尾随分隔符（生命周期内实例稳定），使增量对齐能把复用控件留在可视树原位、不失焦
    //（否则 reconcile 重排时正在编辑的 TextBox 被移出可视树即丢焦点，无法连续输入）。
    abstract class Creator : IDisposable
    {
        protected Creator(PropertyObjectController parent)
        {
            Parent = parent;
            mSeparator = ObjectPoolManager.Get<Border>();
            mSeparator.Height = 1;
            mSeparator.Background = Style.BACK.ToBrush();
        }

        protected PropertyObjectController Parent { get; }
        protected readonly DisposableManager s = new();
        public abstract Type ConfigType { get; }
        public abstract IEnumerable<Control> Views { get; }
        // 布局序列 = 内容控件 + 尾随分隔符。
        public IEnumerable<Control> LayoutViews
        {
            get
            {
                foreach (var view in Views)
                    yield return view;
                yield return mSeparator;
            }
        }
        public abstract void Update(IControllerConfig config);
        public virtual void Dispose()
        {
            s.DisposeAll();
            ObjectPoolManager.Return(mSeparator);
        }

        readonly Border mSeparator;
    }

    class ObjectCreator : Creator
    {
        public ObjectCreator(PropertyObjectController parent, string key, ObjectConfig config) : base(parent)
        {
            mTitle = CreateTitle(config.DisplayText ?? key, 26);

            mBorder = ObjectPoolManager.Get<Border>();
            mBorder.Margin = new(23, 12, 0, 0);
            mBorder.Width = 1;
            mBorder.Background = Style.BACK.ToBrush();

            mController = ObjectPoolManager.Get<PropertyObjectController>();
            mController.SetConfig(config, parent.DataObject.Object(key));

            mDockPanel = ObjectPoolManager.Get<DockPanel>();
            mDockPanel.Margin = new(0, 0, 0, 0);
            mDockPanel.AddDock(mBorder, Dock.Left);
            mDockPanel.AddDock(mController);

            mCollapsiblePanel = ObjectPoolManager.Get<CollapsiblePanel>();
            mCollapsiblePanel.Margin = new(0, 0, 0, 12);
            mCollapsiblePanel.Title = mTitle;
            mCollapsiblePanel.Content = mDockPanel;
        }

        public override Type ConfigType => typeof(ObjectConfig);
        public override IEnumerable<Control> Views => [mCollapsiblePanel];
        public override void Update(IControllerConfig config) => mController.Reconcile((ObjectConfig)config);

        public override void Dispose()
        {
            base.Dispose();
            mCollapsiblePanel.Title = null;
            mCollapsiblePanel.Content = null;
            ObjectPoolManager.Return(mCollapsiblePanel);
            mDockPanel.Children.Clear();
            ObjectPoolManager.Return(mDockPanel);
            ObjectPoolManager.Return(mBorder);
            mController.ResetConfig();
            ObjectPoolManager.Return(mController);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly Border mBorder;
        readonly DockPanel mDockPanel;
        readonly CollapsiblePanel mCollapsiblePanel;
        readonly PropertyObjectController mController;
    }

    class SliderCreator : Creator
    {
        public SliderCreator(PropertyObjectController parent, string key, SliderConfig config) : base(parent)
        {
            mTitle = CreateTitle(config.DisplayText ?? key, 30);

            mController = ObjectPoolManager.Get<SliderController>();
            mController.Margin = new(24, 12);
            Apply(config);

            // 先绑定（初次刷新即把真实值写入），Relayout 才加入可视树——否则池复用的控件会以残留旧值/旧量程
            // 先布局渲染一帧，thumb 随后才跳到正确位置（初次选中音符时可见的瞬间挪动）。
            mController.BindDataProperty(parent.DataObject.NumberField(key, config.DefaultValue), s);
        }

        void Apply(SliderConfig config)
        {
            mController.SetRange(config.MinValue, config.MaxValue);
            mController.SetDefaultValue(config.DefaultValue);
            mController.IsInteger = config.IsInteger;
        }

        public override Type ConfigType => typeof(SliderConfig);
        public override IEnumerable<Control> Views => [mTitle, mController];
        public override void Update(IControllerConfig config) => Apply((SliderConfig)config);

        public override void Dispose()
        {
            base.Dispose();
            ObjectPoolManager.Return(mController);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly SliderController mController;
    }

    class SingleLineTextCreator : Creator
    {
        public SingleLineTextCreator(PropertyObjectController parent, string key, TextBoxConfig config) : base(parent)
        {
            mTitle = CreateTitle(config.DisplayText ?? key, 30);

            mController = ObjectPoolManager.Get<SingleLineTextController>();
            mController.Margin = new(24, 12);
            mController.IsPassword = config.IsPassword;

            mController.BindDataProperty(parent.DataObject.StringField(key, config.DefaultValue), s);
        }

        public override Type ConfigType => typeof(TextBoxConfig);
        public override IEnumerable<Control> Views => [mTitle, mController];
        public override void Update(IControllerConfig config) { mController.IsPassword = ((TextBoxConfig)config).IsPassword; }

        public override void Dispose()
        {
            base.Dispose();
            ObjectPoolManager.Return(mController);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly SingleLineTextController mController;
    }

    class ComboBoxCreator : Creator
    {
        public ComboBoxCreator(PropertyObjectController parent, string key, ComboBoxConfig config) : base(parent)
        {
            mKey = key;
            mConfig = config;
            mTitle = CreateTitle(config.DisplayText ?? key, 30);

            mController = ObjectPoolManager.Get<ComboBoxController>();
            mController.Margin = new(24, 12);
            mController.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            BindWith(config);
        }

        // SetConfig（自洽显示 default）后紧接 bind（bind 内 Refresh 把当前数据值按三态显示出来）——复刻"SetConfig→bind"
        // 成对范式；reconcile 改选项时也走它，否则显示会停在 SetConfig 给的 default 而非数据值。
        void BindWith(ComboBoxConfig config)
        {
            mController.SetConfig(config);
            // 绑裸 PropertyValue 字段：option 值可为任意基础类型，存进数据的就是该值本身（非显示文本）。
            mController.BindDataProperty(Parent.DataObject.ValueField(mKey, config.DefaultOption.Value), s);
        }

        public override Type ConfigType => typeof(ComboBoxConfig);
        public override IEnumerable<Control> Views => [mTitle, mController];

        // 选项未变则跳过：SetConfig 会 Clear + 重填 Items 打断当前选中，无谓重建。选项变则解旧绑定 → BindWith
        //（SetConfig + 重新 bind），让控件显示回当前数据值。
        public override void Update(IControllerConfig config)
        {
            var combo = (ComboBoxConfig)config;
            if (combo.Options.SequenceEqual(mConfig.Options))
                return;
            mConfig = combo;
            s.DisposeAll();
            BindWith(combo);
        }

        public override void Dispose()
        {
            base.Dispose();
            ObjectPoolManager.Return(mController);
            ObjectPoolManager.Return(mTitle);
        }

        readonly string mKey;
        readonly Label mTitle;
        readonly ComboBoxController mController;
        ComboBoxConfig mConfig;
    }

    class CheckBoxCreator : Creator
    {
        public CheckBoxCreator(PropertyObjectController parent, string key, CheckBoxConfig config) : base(parent)
        {
            mDockPanel = ObjectPoolManager.Get<DockPanel>();

            mController = ObjectPoolManager.Get<Components.CheckBox>();
            mController.Margin = new(24, 12);
            mDockPanel.Children.Add(mController);
            DockPanel.SetDock(mController, Dock.Right);

            mTitle = CreateTitle(config.DisplayText ?? key, 30);
            mTitle.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
            mDockPanel.Children.Add(mTitle);

            mController.BindDataProperty(parent.DataObject.BoolField(key, config.DefaultValue), s);
        }

        public override Type ConfigType => typeof(CheckBoxConfig);
        public override IEnumerable<Control> Views => [mDockPanel];
        public override void Update(IControllerConfig config) { }

        public override void Dispose()
        {
            base.Dispose();
            mDockPanel.Children.Clear();
            ObjectPoolManager.Return(mTitle);
            ObjectPoolManager.Return(mController);
            ObjectPoolManager.Return(mDockPanel);
        }

        readonly Label mTitle;
        readonly DockPanel mDockPanel;
        readonly Components.CheckBox mController;
    }

    // 音素编辑器控件
    class PhonemeEditorCreator : Creator
    {
        public PhonemeEditorCreator(PropertyObjectController parent, string key, PhonemeEditorConfig config) : base(parent)
        {
            mKey = key;
            mPanel = ObjectPoolManager.Get<StackPanel>();
            mPanel.Margin = new(0, 8);
            mDataProp = Parent.DataObject.StringField(config.DataKey, "[]");

            // 监听语言属性变化：当语言被用户（而非音素编辑器）修改时，清除自定义音素以强制 G2P 重生
            if (!string.IsNullOrEmpty(config.LanguageDataKey))
            {
                mLangField = Parent.DataObject.ValueField(config.LanguageDataKey, PropertyValue.Create(string.Empty));
                mLangField.Modified.Subscribe(OnLanguageFieldChanged, s);
            }

            Apply(config);
        }

        // 语言属性被外部（语言下拉）修改时清除自定义音素
        void OnLanguageFieldChanged()
        {
            if (mSettingLanguage) return; // 音素编辑器自身提交导致的语言变更，不理
            if (mLangField == null) return;
            var curVal = mLangField.Value.ToString(out var lang) ? lang : string.Empty;
            if (string.IsNullOrEmpty(curVal) || curVal == "multi")
                return; // 空或 multi 不清除
            // 用户主动选择了具体语言 → 清除自定义音素，强制 G2P 重生
            mDataProp.Set("[]");
        }

        static string Serialize(IReadOnlyList<PhonemeEntry> phonemes)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            for (int i = 0; i < phonemes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"s\":\"");
                sb.Append(phonemes[i].Symbol.Replace("\\", "\\\\").Replace("\"", "\\\""));
                sb.Append("\",\"v\":");
                sb.Append(phonemes[i].IsVowel ? "true" : "false");
                sb.Append('}');
            }
            sb.Append(']');
            return sb.ToString();
        }

        void Commit(IReadOnlyList<PhonemeEntry> phonemes)
        {
            var json = Serialize(phonemes);
            mDataProp.Set(json);
            // 从音素符号中检测语言，回写语言属性
            DetectAndSetLanguage(phonemes);
            if (mConfig.OnChanged != null)
                mConfig.OnChanged(json);
        }

        // 从音素符号中检测语言，回写语言属性。设 mSettingLanguage 标志以阻止 OnLanguageFieldChanged 清除音素。
        void DetectAndSetLanguage(IReadOnlyList<PhonemeEntry> phonemes)
        {
            string? detectedLang = null;
            bool mixed = false;
            foreach (var ph in phonemes)
            {
                int idx = ph.Symbol.IndexOf('/');
                if (idx < 0) continue;
                string lang = ph.Symbol.Substring(0, idx);
                if (detectedLang == null)
                    detectedLang = lang;
                else if (detectedLang != lang)
                {
                    mixed = true;
                    break;
                }
            }
            if (detectedLang == null) return;

            string langKey = mConfig.LanguageDataKey;
            string newLang = mixed ? "multi" : detectedLang;
            var current = Parent.DataObject.GetValue(langKey, PropertyValue.Create(string.Empty));
            if (current.ToString(out var curStr) && curStr == newLang)
                return;
            mSettingLanguage = true;
            try { Parent.DataObject.SetValue(langKey, PropertyValue.Create(newLang)); }
            finally { mSettingLanguage = false; }
        }

        // 字段
        bool mSettingLanguage;
        IDataProperty<PropertyValue>? mLangField;

        void Apply(PhonemeEditorConfig config)
        {
            mPanel.Children.Clear();
            mConfig = config;

            var title = CreateTitle(config.DisplayText ?? mKey, 26);
            mPanel.Children.Add(title);

            RenderGroup("  Consonant", config.Phonemes.Where(p => !p.IsVowel).ToList(), isVowel: false);
            RenderGroup("  Vowel", config.Phonemes.Where(p => p.IsVowel).ToList(), isVowel: true);
        }

        void RenderGroup(string label, List<PhonemeEntry> entries, bool isVowel)
        {
            var lbl = new Label { Content = label, FontSize = 11, Foreground = Style.LIGHT_WHITE.ToBrush(), Margin = new(24, 4, 0, 2) };
            mPanel.Children.Add(lbl);

            foreach (var phoneme in entries)
            {
                var row = CreatePhonemeRow(phoneme, isVowel);
                mPanel.Children.Add(row);
            }
        }

        // 布局：[lang↓(70px左)] [音素(弹性拉伸)] [+(22px)] [-(22px最右)]
        // 注意 DockPanel LastChildFill=true，最后一个添加的 child 填充剩余空间
        DockPanel CreatePhonemeRow(PhonemeEntry phoneme, bool isVowel)
        {
            int rowH = 24;
            var dock = new DockPanel { Height = rowH + 4, Margin = new(24, 2, 24, 2) };
            int phIdx = mConfig.Phonemes.IndexOf(phoneme);

            // 1. 左端：语言前缀下拉（70px，无前缀时显示「??」）
            string langPrefix = phoneme.Symbol.Contains('/') ? phoneme.Symbol.Split('/')[0] : "??";
            var langCombo = new DropDown { Height = rowH, Width = 70, FontSize = 11, MinHeight = rowH, MaxHeight = rowH, Padding = new(6, 0) };
            langCombo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            foreach (var l in mConfig.AvailableLanguages) langCombo.Items.Add(l);
            langCombo.SelectedItem = langPrefix;
            langCombo.PlaceholderText = "??";
            int capIdx2 = phIdx;
            langCombo.SelectionChanged += (_, _) =>
            {
                if (capIdx2 < mConfig.Phonemes.Count && langCombo.SelectedItem is string newLang)
                {
                    var list = mConfig.Phonemes.ToList();
                    if (capIdx2 < list.Count)
                    {
                        var entry = list[capIdx2];
                        string p = entry.Symbol.Contains('/') ? entry.Symbol.Split('/')[1] : entry.Symbol;
                        list[capIdx2] = new PhonemeEntry { Symbol = $"{newLang}/{p}", IsVowel = isVowel };
                        Commit(list);
                    }
                }
            };
            DockPanel.SetDock(langCombo, Dock.Left);
            dock.Children.Add(langCombo);

            // 2. 右端：删除按钮（-）始终显示，仅一个时半透明
            bool canDelete = isVowel ? mConfig.CanDeleteVowel : mConfig.CanDeleteConsonant;
            var delBtn = new TuneLab.GUI.Components.Button { Width = 22, Height = rowH, Opacity = canDelete ? 1.0 : 0.5 };
            int cap = phIdx;
            delBtn.Clicked += () =>
            {
                if (!canDelete) return;
                var list = mConfig.Phonemes.ToList();
                if (cap < list.Count) list.RemoveAt(cap);
                Commit(list);
            };
            delBtn.AddContent(new() { Item = new IconItem() { Icon = Assets.PhonemeDelete }, ColorSet = new() { Color = Style.WHITE } });
            delBtn.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            DockPanel.SetDock(delBtn, Dock.Right);
            dock.Children.Add(delBtn);

            // 3. 右2：添加按钮（+）
            var addBtn = new TuneLab.GUI.Components.Button { Width = 22, Height = rowH };
            int capAdd = phIdx;
            addBtn.Clicked += () =>
            {
                var list = mConfig.Phonemes.ToList();
                var newEntry = new PhonemeEntry { Symbol = mConfig.AvailableLanguages.FirstOrDefault() ?? "??", IsVowel = isVowel };
                list.Insert(capAdd + 1, newEntry);
                Commit(list);
            };
            addBtn.AddContent(new() { Item = new IconItem() { Icon = Assets.PhonemeAdd }, ColorSet = new() { Color = Style.WHITE } });
            addBtn.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            DockPanel.SetDock(addBtn, Dock.Right);
            dock.Children.Add(addBtn);

            // 4. 最后：音素文本输入（弹性拉伸填充剩余空间，高度匹配下拉栏）
            string plain = phoneme.Symbol.Contains('/') ? phoneme.Symbol.Split('/')[1] : phoneme.Symbol;
            var textInput = new TextInput { Height = rowH, MinHeight = rowH, MaxHeight = rowH, FontSize = 12, Text = plain, Padding = new(6, 0) };
            textInput.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            textInput.Margin = new(4, 0, 4, 0);
            int capIdx = phIdx;
            textInput.ValueCommitted.Subscribe(() =>
            {
                var list = mConfig.Phonemes.ToList();
                if (capIdx < list.Count)
                {
                    var entry = list[capIdx];
                    string lang = entry.Symbol.Contains('/') ? entry.Symbol.Split('/')[0] : "";
                    list[capIdx] = new PhonemeEntry { Symbol = string.IsNullOrEmpty(lang) ? textInput.Text : $"{lang}/{textInput.Text}", IsVowel = isVowel };
                    Commit(list);
                }
            }, s);
            // 作为最后一个 child，自动填充
            dock.Children.Add(textInput);

            return dock;
        }

        public override Type ConfigType => typeof(PhonemeEditorConfig);
        public override IEnumerable<Control> Views => [mPanel];

        public override void Update(IControllerConfig config) => Apply((PhonemeEditorConfig)config);

        public override void Dispose()
        {
            base.Dispose();
            mPanel.Children.Clear();
            ObjectPoolManager.Return(mPanel);
        }

        readonly string mKey;
        readonly StackPanel mPanel;
        PhonemeEditorConfig mConfig = null!;
        readonly IDataProperty<string> mDataProp;
    }

    // 按钮控件：渲染为可点击的按钮，触发 ButtonConfig.Action（在 UI 线程）。不绑定数据。
    class ButtonCreator : Creator
    {
        public ButtonCreator(PropertyObjectController parent, string key, ButtonConfig config) : base(parent)
        {
            var text = config.DisplayText ?? key;

            mButton = ObjectPoolManager.Get<Components.Button>();
            mButton.Height = 32;
            mButton.Margin = new(24, 8, 24, 8);
            mButton.AddContent(new()
            {
                Item = new BorderItem() { CornerRadius = 4 },
                ColorSet = new() { Color = Style.BUTTON_NORMAL, HoveredColor = Style.BUTTON_NORMAL_HOVER, PressedColor = Style.INTERFACE },
            });
            mButton.AddContent(new()
            {
                Item = new TextItem() { Text = text, FontSize = 13 },
                ColorSet = new() { Color = Style.WHITE },
            });

            mAction = config.Action;
            mButton.Clicked += OnClicked;
        }

        void OnClicked()
        {
            mAction?.Invoke();
        }

        public override Type ConfigType => typeof(ButtonConfig);
        public override IEnumerable<Control> Views => [mButton];

        public override void Update(IControllerConfig config)
        {
            var btn = (ButtonConfig)config;
            mAction = btn.Action;
        }

        public override void Dispose()
        {
            base.Dispose();
            mButton.Clicked -= OnClicked;
            ObjectPoolManager.Return(mButton);
        }

        readonly Components.Button mButton;
        Action? mAction;
    }

    static Label CreateTitle(string title, double height)
    {
        var label = ObjectPoolManager.Get<Label>();
        label.Height = height;
        label.FontSize = 12;
        label.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        label.Foreground = Style.LIGHT_WHITE.ToBrush();
        label.Content = title;
        label.Padding = new(24, 0);
        return label;
    }

    static readonly Map<Type, Func<PropertyObjectController, string, IControllerConfig, Creator>> mFactories = new()
    {
        { typeof(ObjectConfig), (parent, key, config) => new ObjectCreator(parent, key, (ObjectConfig)config) },
        { typeof(SliderConfig), (parent, key, config) => new SliderCreator(parent, key, (SliderConfig)config) },
        { typeof(TextBoxConfig), (parent, key, config) => new SingleLineTextCreator(parent, key, (TextBoxConfig)config) },
        { typeof(ComboBoxConfig), (parent, key, config) => new ComboBoxCreator(parent, key, (ComboBoxConfig)config) },
        { typeof(CheckBoxConfig), (parent, key, config) => new CheckBoxCreator(parent, key, (CheckBoxConfig)config) },
        { typeof(ButtonConfig), (parent, key, config) => new ButtonCreator(parent, key, (ButtonConfig)config) },
        { typeof(PhonemeEditorConfig), (parent, key, config) => new PhonemeEditorCreator(parent, key, (PhonemeEditorConfig)config) },
    };

    IDataPropertyObject? mDataObject;
    Dictionary<string, Creator> mCreatorsByKey = new();
    List<string> mOrder = new();
}
