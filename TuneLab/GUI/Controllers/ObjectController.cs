using Avalonia.Layout;
using Avalonia.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Buffers;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Base.Event;
using TuneLab.Base.Data;
using Microsoft.Extensions.ObjectPool;
using TuneLab.Utils;
using TuneLab.Base.Utils;
using TuneLab.I18N;

namespace TuneLab.GUI.Controllers;

internal class ObjectController : StackPanel
{
    public IActionEvent<PropertyPath> ValueWillChange => mValueWillChange;
    public IActionEvent<PropertyPath, PropertyValue> ValueChanged => mValueChanged;
    public IActionEvent<PropertyPath, PropertyValue> ValueCommited => mValueCommited;

    public ObjectController()
    {
        Background = Style.INTERFACE.ToBrush();
    }

    public void SetConfig(ObjectConfig config)
    {
        foreach (var kvp in config.Properties)
        {
            var key = kvp.Key;
            var value = kvp.Value;
            if (value is ObjectConfig objectConfig)
            {
                mDisposableManager += new ObjectCreator(this, key, objectConfig);
            }
            else if (value is NumberConfig numberConfig)
            {
                mDisposableManager += new SliderCreator(this, key, numberConfig);
            }
            else if (value is StringConfig stringConfig)
            {
                mDisposableManager += new SingleLineTextCreator(this, key, stringConfig);
            }
            else if (value is EnumConfig enumConfig)
            {
                mDisposableManager += new ComboBoxCreator(this, key, enumConfig);
            }
            else if (value is BooleanConfig booleanConfig)
            {
                mDisposableManager += new CheckBoxCreator(this, key, booleanConfig);
            }
            mDisposableManager += new BorderCreator(this);
        }
    }

    public void ResetConfig()
    {
        mDisposableManager.DisposeAll();
    }

    public void DisplayNull(PropertyPath.Key key)
    {
        if (mControllers.TryGetValue(key, out var controller))
            controller.DisplayNull(key);
    }

    public void Display(PropertyPath.Key key, PropertyValue value)
    {
        if (mControllers.TryGetValue(key, out var controller))
            controller.Display(key, value);
    }

    interface IController
    {
        void Display(PropertyPath.Key key, PropertyValue value);
        void DisplayNull(PropertyPath.Key key);
    }

    interface IValueController : IController
    {
        void IController.Display(PropertyPath.Key key, PropertyValue propertyValue)
        {
            if (key.IsObject)
                return;

            Display(propertyValue);
        }

        void IController.DisplayNull(PropertyPath.Key key)
        {
            if (key.IsObject)
                return;

            DisplayNull();
        }

        void Display(PropertyValue propertyValue);
        void DisplayNull();
    }

    class LabelCreator : IDisposable
    {
        public Label Label => label;

        public LabelCreator(Panel controller, string key)
        {
            mController = controller;

            label = ObjectPoolManager.Get<Label>();
            label.Height = 30;
            label.FontSize = 12;
            label.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            label.Foreground = Style.LIGHT_WHITE.ToBrush();
            label.Content = key.Tr(TC.Property);
            label.Padding = new(24, 0);
            controller.Children.Add(label);
        }

        public void Dispose()
        {
            mController.Children.Remove(label);
            ObjectPoolManager.Return(label);
        }

        readonly Panel mController;
        readonly Label label;
    }

    class BorderCreator : IDisposable
    {
        public BorderCreator(ObjectController controller)
        {
            mController = controller;
            border = ObjectPoolManager.Get<Border>();
            border.Height = 1;
            border.Background = Style.BACK.ToBrush();
            controller.Children.Add(border);
        }

        public void Dispose()
        {
            mController.Children.Remove(border);
            ObjectPoolManager.Return(border);
        }

        readonly ObjectController mController;
        readonly Border border;
    }

    class ObjectCreator : IDisposable, IController
    {
        public ObjectCreator(ObjectController controller, string key, ObjectConfig config)
        {
            mController = controller;
            mKey = key;
            mLabelCreator = new LabelCreator(controller, key);

            objectController = ObjectPoolManager.Get<ObjectController>();
            objectController.SetConfig(config);
            objectController.ValueWillChange.Subscribe(mController.mValueWillChange);
            objectController.ValueChanged.Subscribe(OnValueChanged);
            objectController.ValueCommited.Subscribe(OnValueCommited);
            mController.Children.Add(objectController);
            mController.mControllers.Add(mKey, this);
        }

        public void Dispose()
        {
            mLabelCreator.Dispose();
            mController.mControllers.Remove(mKey);
            mController.Children.Remove(objectController);
            objectController.ValueWillChange.Unsubscribe(mController.mValueWillChange);
            objectController.ValueChanged.Unsubscribe(OnValueChanged);
            objectController.ValueCommited.Unsubscribe(OnValueCommited);
            objectController.ResetConfig();
            ObjectPoolManager.Return(objectController);
        }

        public void Display(PropertyPath.Key key, PropertyValue value)
        {
            objectController.Display(key.Next, value);
        }

        public void DisplayNull(PropertyPath.Key key)
        {
            objectController.DisplayNull(key.Next);
        }

        void OnValueChanged(PropertyPath path, PropertyValue value)
        {
            mController.mValueChanged.Invoke(new PropertyPath(mKey).Combine(path), value);
        }

        void OnValueCommited(PropertyPath path, PropertyValue value)
        {
            mController.mValueCommited.Invoke(new PropertyPath(mKey).Combine(path), value);
        }

        readonly string mKey;
        readonly LabelCreator mLabelCreator;
        readonly ObjectController mController;
        readonly ObjectController objectController;
    }

    class SliderCreator : IDisposable, IValueController
    {
        public SliderCreator(ObjectController controller, string key, NumberConfig config)
        {
            mController = controller;
            mKey = key;
            mLabelCreator = new LabelCreator(controller, key);

            mSliderController = ObjectPoolManager.Get<SliderController>();
            mSliderController.SetRange(config.MinValue, config.MaxValue);
            mSliderController.SetDefaultValue(config.DefaultValue);
            mSliderController.IsInterger = config.IsInterger;
            mSliderController.Display(config.DefaultValue);
            mSliderController.ValueWillChange.Subscribe(OnValueWillChange);
            mSliderController.ValueChanged.Subscribe(OnValueChanged);
            mSliderController.ValueCommited.Subscribe(OnValueCommited);
            mController.Children.Add(mSliderController);
            mController.mControllers.Add(mKey, this);
        }

        public void Dispose()
        {
            mLabelCreator.Dispose();
            mController.mControllers.Remove(mKey);
            mController.Children.Remove(mSliderController);
            mSliderController.ValueWillChange.Unsubscribe(OnValueWillChange);
            mSliderController.ValueChanged.Unsubscribe(OnValueChanged);
            mSliderController.ValueCommited.Unsubscribe(OnValueCommited);
            ObjectPoolManager.Return(mSliderController);
        }

        public void Display(PropertyValue propertyValue)
        {
            if (propertyValue.ToDouble(out var value))
                mSliderController.Display(value);
            else
                mSliderController.Display(double.NaN);
        }

        public void DisplayNull()
        {
            mSliderController.Display(double.NaN);
        }

        void OnValueWillChange()
        {
            mController.mValueWillChange.Invoke(new PropertyPath(mKey));
        }

        void OnValueChanged()
        {
            mController.mValueChanged.Invoke(new PropertyPath(mKey), mSliderController.Value);
        }

        void OnValueCommited()
        {
            mController.mValueCommited.Invoke(new PropertyPath(mKey), mSliderController.Value);
        }

        readonly string mKey;
        readonly LabelCreator mLabelCreator;
        readonly ObjectController mController;
        readonly SliderController mSliderController;
    }

    class SingleLineTextCreator : IDisposable, IValueController
    {
        public SingleLineTextCreator(ObjectController controller, string key, StringConfig config)
        {
            mController = controller;
            mKey = key;
            mLabelCreator = new LabelCreator(controller, key);

            mSingleLineTextController = ObjectPoolManager.Get<SingleLineTextController>();
            mSingleLineTextController.Display(config.DefaultValue);
            mSingleLineTextController.ValueWillChange.Subscribe(OnValueWillChange);
            mSingleLineTextController.ValueChanged.Subscribe(OnValueChanged);
            mSingleLineTextController.ValueCommited.Subscribe(OnValueCommited);
            mController.Children.Add(mSingleLineTextController);
            mController.mControllers.Add(mKey, this);
        }

        public void Dispose()
        {
            mLabelCreator.Dispose();
            mController.mControllers.Remove(mKey);
            mController.Children.Remove(mSingleLineTextController);
            mSingleLineTextController.ValueWillChange.Unsubscribe(OnValueWillChange);
            mSingleLineTextController.ValueChanged.Unsubscribe(OnValueChanged);
            mSingleLineTextController.ValueCommited.Unsubscribe(OnValueCommited);
            ObjectPoolManager.Return(mSingleLineTextController);
        }

        public void Display(PropertyValue propertyValue)
        {
            if (propertyValue.ToString(out var value))
                mSingleLineTextController.Display(value);
            else
                mSingleLineTextController.Display("-");
        }

        public void DisplayNull()
        {
            mSingleLineTextController.Display("-");
        }

        void OnValueWillChange()
        {
            mController.mValueWillChange.Invoke(new PropertyPath(mKey));
        }

        void OnValueChanged()
        {
            mController.mValueChanged.Invoke(new PropertyPath(mKey), mSingleLineTextController.Value);
        }

        void OnValueCommited()
        {
            mController.mValueCommited.Invoke(new PropertyPath(mKey), mSingleLineTextController.Value);
        }

        readonly string mKey;
        readonly LabelCreator mLabelCreator;
        readonly ObjectController mController;
        readonly SingleLineTextController mSingleLineTextController;
    }

    class ComboBoxCreator : IDisposable, IValueController
    {
        public ComboBoxCreator(ObjectController controller, string key, EnumConfig config)
        {
            mController = controller;
            mKey = key;
            mLabelCreator = new LabelCreator(controller, key);

            mComboBoxController = ObjectPoolManager.Get<ComboBoxController>();
            mComboBoxController.SetConfig(config);
            mComboBoxController.Display(config.DefaultValue);
            mComboBoxController.ValueWillChange.Subscribe(OnValueWillChange);
            mComboBoxController.ValueChanged.Subscribe(OnValueChanged);
            mComboBoxController.ValueCommited.Subscribe(OnValueCommited);
            mController.Children.Add(mComboBoxController);
            mController.mControllers.Add(mKey, this);
        }

        public void Dispose()
        {
            mLabelCreator.Dispose();
            mController.mControllers.Remove(mKey);
            mController.Children.Remove(mComboBoxController);
            mComboBoxController.ValueWillChange.Unsubscribe(OnValueWillChange);
            mComboBoxController.ValueChanged.Unsubscribe(OnValueChanged);
            mComboBoxController.ValueCommited.Unsubscribe(OnValueCommited);
            ObjectPoolManager.Return(mComboBoxController);
        }

        public void Display(PropertyValue propertyValue)
        {
            if (propertyValue.ToString(out var value))
                mComboBoxController.Display(value);
            else
                mComboBoxController.Display("-");
        }

        public void DisplayNull()
        {
            mComboBoxController.Display("-");
        }

        void OnValueWillChange()
        {
            mController.mValueWillChange.Invoke(new PropertyPath(mKey));
        }

        void OnValueChanged()
        {
            mController.mValueChanged.Invoke(new PropertyPath(mKey), mComboBoxController.Value);
        }

        void OnValueCommited()
        {
            mController.mValueCommited.Invoke(new PropertyPath(mKey), mComboBoxController.Value);
        }

        readonly string mKey;
        readonly LabelCreator mLabelCreator;
        readonly ObjectController mController;
        readonly ComboBoxController mComboBoxController;
    }

    class CheckBoxCreator : IDisposable, IValueController
    {
        public CheckBoxCreator(ObjectController controller, string key, BooleanConfig config)
        {
            mController = controller;
            mKey = key;
            mDockPanel = ObjectPoolManager.Get<DockPanel>();
            mCheckBoxController = ObjectPoolManager.Get<CheckBoxController>();
            mCheckBoxController.Margin = new(24, 12);
            mCheckBoxController.Display(config.DefaultValue);
            mCheckBoxController.ValueWillChange.Subscribe(OnValueWillChange);
            mCheckBoxController.ValueChanged.Subscribe(OnValueChanged);
            mCheckBoxController.ValueCommited.Subscribe(OnValueCommited);
            mDockPanel.Children.Add(mCheckBoxController);
            DockPanel.SetDock(mCheckBoxController, Dock.Right);
            mLabelCreator = new LabelCreator(mDockPanel, key);
            mLabelCreator.Label.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center;
            mController.Children.Add(mDockPanel);
            mController.mControllers.Add(mKey, this);
        }

        public void Dispose()
        {
            mController.mControllers.Remove(mKey);
            mController.Children.Remove(mDockPanel);
            mLabelCreator.Dispose();
            mDockPanel.Children.Clear();
            mCheckBoxController.ValueWillChange.Unsubscribe(OnValueWillChange);
            mCheckBoxController.ValueChanged.Unsubscribe(OnValueChanged);
            mCheckBoxController.ValueCommited.Unsubscribe(OnValueCommited);
            ObjectPoolManager.Return(mCheckBoxController);
            ObjectPoolManager.Return(mDockPanel);
        }

        public void Display(PropertyValue propertyValue)
        {
            if (propertyValue.ToBool(out var value))
                mCheckBoxController.Display(value);
            else
                mCheckBoxController.Display(new object());
        }

        public void DisplayNull()
        {
            mCheckBoxController.Display(null);
        }

        void OnValueWillChange()
        {
            mController.mValueWillChange.Invoke(new PropertyPath(mKey));
        }

        void OnValueChanged()
        {
            mController.mValueChanged.Invoke(new PropertyPath(mKey), mCheckBoxController.Value);
        }

        void OnValueCommited()
        {
            mController.mValueCommited.Invoke(new PropertyPath(mKey), mCheckBoxController.Value);
        }

        readonly string mKey;
        readonly DockPanel mDockPanel;
        readonly LabelCreator mLabelCreator;
        readonly ObjectController mController;
        readonly CheckBoxController mCheckBoxController;
    }

    Map<string, IController> mControllers = new();
    readonly ActionEvent<PropertyPath> mValueWillChange = new();
    readonly ActionEvent<PropertyPath, PropertyValue> mValueChanged = new();
    readonly ActionEvent<PropertyPath, PropertyValue> mValueCommited = new();
    DisposableManager mDisposableManager = new();
}
