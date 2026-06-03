using Avalonia.Controls;
using Avalonia.Layout;
using System;
using TuneLab.Foundation.Property;
using TuneLab.SDK.Base;
using TuneLab.Primitives.DataStructures;
using TuneLab.Foundation.Utils;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.GUI.Controllers;

// 配置驱动的属性面板：按 ObjectConfig 渲染控件，逐字段直接绑定到 IDataPropertyObject 的可绑定字段，
// 复用单属性的撤销/刷新/提交机制（不再走 PropertyPath 下发值 + 事件上抛 + 外部手工写回那套）。
// 控件类型经注册表分发，加新控件只需注册一项。嵌套对象用 PropertyPath 寻址，绑定仍打到同一数据源。
internal class PropertyObjectController : StackPanel
{
    public PropertyObjectController()
    {
        Background = Style.INTERFACE.ToBrush();
    }

    public void SetConfig(ObjectConfig config, IDataPropertyObject dataObject)
    {
        SetConfig(config, dataObject, new PropertyPath());
    }

    void SetConfig(ObjectConfig config, IDataPropertyObject dataObject, PropertyPath basePath)
    {
        mDataObject = dataObject;
        foreach (var kvp in config.Properties)
        {
            var propertyPath = basePath.Combine(kvp.Key);
            if (mCreators.TryGetValue(kvp.Value.GetType(), out var creator))
            {
                mDisposableManager.Add(creator(this, kvp.Key, propertyPath, kvp.Value));
            }
            else
            {
                Log.Error($"No controller found for config type: {kvp.Value.GetType()}");
                continue;
            }
            mDisposableManager.Add(new SeperatorCreator(this));
        }
    }

    public void ResetConfig()
    {
        mDisposableManager.DisposeAll();
        mDataObject = null;
    }

    IDataPropertyObject DataObject => mDataObject ?? throw new InvalidOperationException("PropertyObjectController has no data object.");

    abstract class Creator(PropertyObjectController parent) : IDisposable
    {
        protected PropertyObjectController Parent { get; } = parent;
        protected readonly DisposableManager s = new();
        public virtual void Dispose() => s.DisposeAll();
    }

    class ObjectCreator : Creator
    {
        public ObjectCreator(PropertyObjectController parent, string key, PropertyPath path, ObjectConfig config) : base(parent)
        {
            mTitle = CreateTitle(key, 26);

            mBorder = ObjectPoolManager.Get<Border>();
            mBorder.Margin = new(23, 12, 0, 0);
            mBorder.Width = 1;
            mBorder.Background = Style.BACK.ToBrush();

            mController = ObjectPoolManager.Get<PropertyObjectController>();
            mController.SetConfig(config, parent.DataObject, path);

            mDockPanel = ObjectPoolManager.Get<DockPanel>();
            mDockPanel.Margin = new(0, 0, 0, 0);
            mDockPanel.AddDock(mBorder, Dock.Left);
            mDockPanel.AddDock(mController);

            mCollapsiblePanel = ObjectPoolManager.Get<CollapsiblePanel>();
            mCollapsiblePanel.Margin = new(0, 0, 0, 12);
            mCollapsiblePanel.Title = mTitle;
            mCollapsiblePanel.Content = mDockPanel;

            Parent.Children.Add(mCollapsiblePanel);
        }

        public override void Dispose()
        {
            base.Dispose();
            Parent.Children.Remove(mCollapsiblePanel);
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
        public SliderCreator(PropertyObjectController parent, string key, PropertyPath path, SliderConfig config) : base(parent)
        {
            mTitle = CreateTitle(key, 30);
            Parent.Children.Add(mTitle);

            mController = ObjectPoolManager.Get<SliderController>();
            mController.Margin = new(24, 12);
            mController.SetRange(config.MinValue, config.MaxValue);
            mController.SetDefaultValue(config.DefaultValue);
            mController.IsInterger = config.IsInterger;
            Parent.Children.Add(mController);

            mController.BindDataProperty(parent.DataObject.NumberField(path.GetKey(), config.DefaultValue), s);
        }

        public override void Dispose()
        {
            base.Dispose();
            Parent.Children.Remove(mController);
            ObjectPoolManager.Return(mController);
            Parent.Children.Remove(mTitle);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly SliderController mController;
    }

    class SingleLineTextCreator : Creator
    {
        public SingleLineTextCreator(PropertyObjectController parent, string key, PropertyPath path, TextBoxConfig config) : base(parent)
        {
            mTitle = CreateTitle(key, 30);
            Parent.Children.Add(mTitle);

            mController = ObjectPoolManager.Get<SingleLineTextController>();
            mController.Margin = new(24, 12);
            Parent.Children.Add(mController);

            mController.BindDataProperty(parent.DataObject.StringField(path.GetKey(), config.DefaultValue), s);
        }

        public override void Dispose()
        {
            base.Dispose();
            Parent.Children.Remove(mController);
            ObjectPoolManager.Return(mController);
            Parent.Children.Remove(mTitle);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly SingleLineTextController mController;
    }

    class ComboBoxCreator : Creator
    {
        public ComboBoxCreator(PropertyObjectController parent, string key, PropertyPath path, ComboBoxConfig config) : base(parent)
        {
            mTitle = CreateTitle(key, 30);
            Parent.Children.Add(mTitle);

            mController = ObjectPoolManager.Get<ComboBoxController>();
            mController.Margin = new(24, 12);
            mController.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            mController.SetConfig(config);
            Parent.Children.Add(mController);

            mController.BindDataProperty(parent.DataObject.StringField(path.GetKey(), config.DefaultValue), s);
        }

        public override void Dispose()
        {
            base.Dispose();
            Parent.Children.Remove(mController);
            ObjectPoolManager.Return(mController);
            Parent.Children.Remove(mTitle);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly ComboBoxController mController;
    }

    class CheckBoxCreator : Creator
    {
        public CheckBoxCreator(PropertyObjectController parent, string key, PropertyPath path, CheckBoxConfig config) : base(parent)
        {
            mDockPanel = ObjectPoolManager.Get<DockPanel>();

            mController = ObjectPoolManager.Get<Components.CheckBox>();
            mController.Margin = new(24, 12);
            mDockPanel.Children.Add(mController);
            DockPanel.SetDock(mController, Dock.Right);

            mTitle = CreateTitle(key, 30);
            mTitle.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
            mDockPanel.Children.Add(mTitle);

            Parent.Children.Add(mDockPanel);

            mController.BindDataProperty(parent.DataObject.BoolField(path.GetKey(), config.DefaultValue), s);
        }

        public override void Dispose()
        {
            base.Dispose();
            Parent.Children.Remove(mDockPanel);
            mDockPanel.Children.Clear();
            ObjectPoolManager.Return(mTitle);
            ObjectPoolManager.Return(mController);
            ObjectPoolManager.Return(mDockPanel);
        }

        readonly Label mTitle;
        readonly DockPanel mDockPanel;
        readonly Components.CheckBox mController;
    }

    class SeperatorCreator : IDisposable
    {
        public SeperatorCreator(PropertyObjectController parent)
        {
            mParent = parent;
            mBorder = ObjectPoolManager.Get<Border>();
            mBorder.Height = 1;
            mBorder.Background = Style.BACK.ToBrush();
            mParent.Children.Add(mBorder);
        }

        public void Dispose()
        {
            mParent.Children.Remove(mBorder);
            ObjectPoolManager.Return(mBorder);
        }

        readonly PropertyObjectController mParent;
        readonly Border mBorder;
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

    static readonly Map<Type, Func<PropertyObjectController, string, PropertyPath, IControllerConfig, IDisposable>> mCreators = new()
    {
        { typeof(ObjectConfig), (parent, key, path, config) => new ObjectCreator(parent, key, path, (ObjectConfig)config) },
        { typeof(SliderConfig), (parent, key, path, config) => new SliderCreator(parent, key, path, (SliderConfig)config) },
        { typeof(TextBoxConfig), (parent, key, path, config) => new SingleLineTextCreator(parent, key, path, (TextBoxConfig)config) },
        { typeof(ComboBoxConfig), (parent, key, path, config) => new ComboBoxCreator(parent, key, path, (ComboBoxConfig)config) },
        { typeof(CheckBoxConfig), (parent, key, path, config) => new CheckBoxCreator(parent, key, path, (CheckBoxConfig)config) },
    };

    IDataPropertyObject? mDataObject;
    readonly DisposableManager mDisposableManager = new();
}
