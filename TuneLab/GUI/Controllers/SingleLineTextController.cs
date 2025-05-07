using TuneLab.Foundation.Event;
using TuneLab.GUI.Components;

namespace TuneLab.GUI.Controllers;

internal class SingleLineTextController : LayerPanel, IDataValueController<string>
{
    public IActionEvent ValueWillChange => mTextInput.EnterInput;
    public IActionEvent ValueChanged => mTextInput.TextChanged;
    public IActionEvent ValueCommited => mTextInput.EndInput;
    public string Value { get => mTextInput.Text; set => mTextInput.Text = value; }

    public SingleLineTextController()
    {
        mTextInput = new TextInput()
        {
            Height = 28,
            AcceptsReturn = false
        };

        Children.Add(mTextInput);
    }

    public void Display(string text)
    {
        mTextInput.Display(text);
    }

    public void DisplayNull()
    {
        mTextInput.Display("-");
    }

    public void DisplayMultiple()
    {
        mTextInput.Display("(Multiple)");
    }

    readonly TextInput mTextInput;
}
