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
            mDisposableManager += new LabelCreator(this, key);
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
            mDisposableManager += new BorderCreator(this);
        }
    }

    public void ResetConfig()
    {
        mDisposableManager.DisposeAll();
    }

    public void Display(PropertyPath.Key key, PropertyValue value)
    {
        if (mControllers.TryGetValue(key, out var controller))
            controller.Display(key, value);
    }

    interface IController
    {
        void Display(PropertyPath.Key key, PropertyValue value);
    }

    interface IValueController : IController
    {
        void IController.Display(PropertyPath.Key key, PropertyValue propertyValue)
        {
            if (key.IsObject)
                return;

            Display(propertyValue);
        }

        void Display(PropertyValue propertyValue);
    }

    class LabelCreator : IDisposable
    {
        public LabelCreator(ObjectController controller, string key)
        {
            mController = controller;

            label = ObjectPoolManager.Get<Label>();
            label.Height = 30;
            label.FontSize = 12;
            label.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom;
            label.Foreground = Style.LIGHT_WHITE.ToBrush();
            label.Content = key;
            label.Padding = new(24, 0);
            controller.Children.Add(label);
        }

        public void Dispose()
        {
            mController.Children.Remove(label);
            ObjectPoolManager.Return(label);
        }

        readonly ObjectController mController;
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

        void OnValueChanged(PropertyPath path, PropertyValue value)
        {
            mController.mValueChanged.Invoke(new PropertyPath(mKey).Combine(path), value);
        }

        void OnValueCommited(PropertyPath path, PropertyValue value)
        {
            mController.mValueCommited.Invoke(new PropertyPath(mKey).Combine(path), value);
        }

        readonly string mKey;
        readonly ObjectController mController;
        readonly ObjectController objectController;
    }

    class SliderCreator : IDisposable, IValueController
    {
        public SliderCreator(ObjectController controller, string key, NumberConfig config)
        {
            mController = controller;
            mKey = key;

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
        readonly ObjectController mController;
        readonly SliderController mSliderController;
    }

    class SingleLineTextCreator : IDisposable, IValueController
    {
        public string? Invalid => null;
        public SingleLineTextCreator(ObjectController controller, string key, StringConfig config)
        {
            mController = controller;
            mKey = key;

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
        readonly ObjectController mController;
        readonly SingleLineTextController mSingleLineTextController;
    }

    Map<string, IController> mControllers = new();
    readonly ActionEvent<PropertyPath> mValueWillChange = new();
    readonly ActionEvent<PropertyPath, PropertyValue> mValueChanged = new();
    readonly ActionEvent<PropertyPath, PropertyValue> mValueCommited = new();
    DisposableManager mDisposableManager = new();
}
