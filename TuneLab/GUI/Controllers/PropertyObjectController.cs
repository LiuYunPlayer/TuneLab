using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Property;
using TuneLab.Foundation.Utils;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.GUI.Controllers;

internal class PropertyObjectController : StackPanel
{
    public PropertyObjectController()
    {
        Background = Style.INTERFACE.ToBrush();
    }

    public void SetConfig(ObjectConfig config, IDataPropertyObject dataPropertyObject)
    {
        foreach (var kvp in config.PropertyConfigs)
        {
            var key = kvp.Key;
            var propertyConfig = kvp.Value;
            var configType = propertyConfig.GetType();
            if (!mCreators.TryGetValue(configType, out var controllerType))
            {
                Log.Error($"No controller found for config type: {configType}");
                continue;
            }
            
            var field = dataPropertyObject.GetField(key);
            var creator = Activator.CreateInstance(controllerType, key, propertyConfig, field) as IControllerCreator;
            if (creator == null)
            {
                Log.Error($"Failed to create controller for config type: {configType}");
                continue;
            }

            mDisposableManager.Add(creator);
            mDisposableManager.Add(new SeperatorCreator(this));
        }
    }

    public void ResetConfig()
    {
        mDisposableManager.DisposeAll();
    }

    interface IControllerCreator : IDisposable
    {

    }
    
    abstract class ControllerCreator(PropertyObjectController parent, string key) : IControllerCreator
    {
        protected PropertyObjectController Parent { get; } = parent;
        protected string Key { get; } = key;

        public abstract void Dispose();
    }

    class ObjectControllerCreator : ControllerCreator
    {
        public ObjectControllerCreator(PropertyObjectController parent, string key, ObjectConfig config, IDataPropertyObjectField field) : base(parent, key)
        {
            mTitle = CreateTitle(key);
            mTitle.Height = 26;
            mBorder = CreateBorder();

            mController = ObjectPoolManager.Get<PropertyObjectController>();
            mController.SetConfig(config, field.ToObject());

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
            Parent.Children.Remove(mCollapsiblePanel);

            mCollapsiblePanel.Content = null;
            mCollapsiblePanel.Title = null;
            ObjectPoolManager.Return(mCollapsiblePanel);

            mDockPanel.Children.Clear();
            ObjectPoolManager.Return(mDockPanel);

            mController.ResetConfig();
            ObjectPoolManager.Return(mController);

            ObjectPoolManager.Return(mBorder);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly Border mBorder;
        readonly PropertyObjectController mController;
        readonly DockPanel mDockPanel;
        readonly CollapsiblePanel mCollapsiblePanel;
    }

    class SliderCreator : ControllerCreator
    {
        public SliderCreator(PropertyObjectController parent, string key, SliderConfig config, IDataPropertyObjectField field) : base(parent, key)
        {
            mTitle = CreateTitle(key);
            Parent.Children.Add(mTitle);

            mController = ObjectPoolManager.Get<SliderController>();
            mController.Margin = new(24, 12);
            mController.SetRange(config.MinValue, config.MaxValue);
            mController.SetDefaultValue(config.DefaultValue);
            mController.IsInterger = config.IsInteger; 
            mController.Display(config.DefaultValue);
            Parent.Children.Add(mController);

            mBinding = mController.BindDataProperty(field.ToNumber());
        }

        public override void Dispose()
        {
            mBinding.Dispose();

            Parent.Children.Remove(mController);
            ObjectPoolManager.Return(mController);

            Parent.Children.Remove(mTitle);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly SliderController mController;
        readonly IDisposable mBinding;
    }

    class SingleLineTextCreator : ControllerCreator
    {
        public SingleLineTextCreator(PropertyObjectController parent, string key, TextBoxConfig config, IDataPropertyObjectField field) : base(parent, key)
        {
            mTitle = CreateTitle(key);
            Parent.Children.Add(mTitle);

            mController = ObjectPoolManager.Get<SingleLineTextController>();
            mController.Margin = new(24, 12);
            mController.Display(config.DefaultValue);
            Parent.Children.Add(mController);

            mBinding = mController.BindDataProperty(field.ToString());
        }

        public override void Dispose()
        {
            mBinding.Dispose();

            Parent.Children.Remove(mController);
            ObjectPoolManager.Return(mController);

            Parent.Children.Remove(mTitle);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly SingleLineTextController mController;
        readonly IDisposable mBinding;
    }

    class ComboBoxCreator : ControllerCreator
    {
        public ComboBoxCreator(PropertyObjectController parent, string key, ComboBoxConfig config, IDataPropertyObjectField field) : base(parent, key)
        {
            mTitle = CreateTitle(key);
            Parent.Children.Add(mTitle);

            mController = ObjectPoolManager.Get<ComboBoxController>();
            mController.Margin = new(24, 12);
            mController.SetConfig(config);
            Parent.Children.Add(mController);

            mBinding = mController.BindDataProperty(field.ToPrimitive());
        }

        public override void Dispose()
        {
            mBinding.Dispose();

            Parent.Children.Remove(mController);
            ObjectPoolManager.Return(mController);

            Parent.Children.Remove(mTitle);
            ObjectPoolManager.Return(mTitle);
        }

        readonly Label mTitle;
        readonly ComboBoxController mController;
        readonly IDisposable mBinding;
    }

    class CheckBoxCreator : ControllerCreator
    {
        public CheckBoxCreator(PropertyObjectController parent, string key, CheckBoxConfig config, IDataPropertyObjectField field) : base(parent, key)
        {
            mDockPanel = ObjectPoolManager.Get<DockPanel>();

            mController = ObjectPoolManager.Get<Components.CheckBox>();
            mController.Margin = new(24, 12);
            mController.Display(config.DefaultValue);

            mTitle = CreateTitle(key);

            mDockPanel.AddDock(mController, Dock.Right);
            mDockPanel.AddDock(mTitle);

            Parent.Children.Add(mDockPanel);

            mBinding = mController.BindDataProperty(field.ToBoolean());
        }

        public override void Dispose()
        {
            mBinding.Dispose();

            Parent.Children.Remove(mDockPanel);

            mDockPanel.Children.Clear();

            ObjectPoolManager.Return(mTitle);

            ObjectPoolManager.Return(mController);

            ObjectPoolManager.Return(mDockPanel);
        }

        readonly Label mTitle;
        readonly DockPanel mDockPanel;
        readonly Components.CheckBox mController;
        readonly IDisposable mBinding;
    }

    class SeperatorCreator : IDisposable
    {
        public SeperatorCreator(PropertyObjectController parent)
        {
            Parent = parent;
            mBorder = ObjectPoolManager.Get<Border>();
            mBorder.Height = 1;
            mBorder.Background = Style.BACK.ToBrush();
            Parent.Children.Add(mBorder);
        }

        public void Dispose()
        {
            Parent.Children.Remove(mBorder);
            ObjectPoolManager.Return(mBorder);
        }

        readonly PropertyObjectController Parent;
        readonly Border mBorder;
    }

    static Label CreateTitle(string title)
    {
        var label = ObjectPoolManager.Get<Label>();
        label.Height = 30;
        label.FontSize = 12;
        label.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
        label.Foreground = Style.LIGHT_WHITE.ToBrush();
        label.Content = title;
        label.Padding = new(24, 0);
        return label;
    }

    static Border CreateBorder()
    {
        var border = ObjectPoolManager.Get<Border>();
        border.Margin = new(23, 12, 0, 0);
        border.Width = 1;
        border.Background = Style.BACK.ToBrush();
        return border;
    }

    static Map<Type, Type> mCreators = new()
    {
        { typeof(ObjectConfig), typeof(ObjectControllerCreator) }
    };

    DisposableManager mDisposableManager = new();
}
