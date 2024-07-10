using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Event;
using TuneLab.Data;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.GUI.Controllers;
using TuneLab.I18N;
using TuneLab.Utils;
using static TuneLab.Base.Science.MusicTheory;

namespace TuneLab.Views;

internal class FunctionBar : LayerPanel
{
    public event Action<double>? Moved;
    public IActionEvent IsAutoPageChanged => mIsAutoPageChanged;
    public bool IsAutoPage { get => mIsAutoPage; set { if (mIsAutoPage == value) return; mIsAutoPage = value; mIsAutoPageChanged.Invoke(); } }

    public IActionEvent<QuantizationBase, QuantizationDivision> QuantizationChanged => mQuantizationChanged;

    public interface IDependency
    {
        public IActionEvent PianoToolChanged { get; }
        public PianoTool PianoTool { get; set; }
    }

    public FunctionBar(IDependency dependency)
    {
        mDependency = dependency;

        var mover = new Mover() { Margin = new(0, 1) };
        mover.Moved.Subscribe(p => Moved?.Invoke(p.Y + Bounds.Y));
        Children.Add(mover);

        var dockPanel = new DockPanel() { Margin = new(64, 0, 360, 0) };
        {
            var hoverBack = Colors.White.Opacity(0.05);

            void SetupToolTip(Toggle toggleButton,string ToolTipText)
            {
                ToolTip.SetPlacement(toggleButton, PlacementMode.Top);
                ToolTip.SetVerticalOffset(toggleButton, -8);
                ToolTip.SetShowDelay(toggleButton, 0);
                ToolTip.SetTip(toggleButton, ToolTipText);
            }

            var audioControlPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new(12, 0) };
            {
                var playButton = new Toggle() { Width = 36, Height = 36 }
                    .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                    .AddContent(new() { Item = new IconItem() { Icon = Assets.Play }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });
                
                SetupToolTip(playButton,"Play");
                playButton.Switched += () => { if (playButton.IsChecked) AudioEngine.Play(); else AudioEngine.Pause(); SetupToolTip(playButton, AudioEngine.IsPlaying ? "Pause" : "Play"); };
                AudioEngine.PlayStateChanged += () => { playButton.Display(AudioEngine.IsPlaying);SetupToolTip(playButton, AudioEngine.IsPlaying?"Pause":"Play");};
                audioControlPanel.Children.Add(playButton);

                var autoPageButton = new Toggle() { Width = 36, Height = 36 }
                    .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                    .AddContent(new() { Item = new IconItem() { Icon = Assets.AutoPage }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });

                SetupToolTip(autoPageButton, "Auto Scroll");
                autoPageButton.Switched += () => mIsAutoPage = autoPageButton.IsChecked;
                mIsAutoPageChanged.Subscribe(() => { autoPageButton.Display(mIsAutoPage); });
                audioControlPanel.Children.Add(autoPageButton);
            }
            dockPanel.AddDock(audioControlPanel, Dock.Left);

            var quantizationPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            {
                var quantizationLabel = new TextBlock() { Text = ("Quantization: ").Tr(), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
                quantizationPanel.Children.Add(quantizationLabel);
                var quantizationComboBox = new ComboBoxController() { Width = 96 };
                (string option, QuantizationBase quantizationBase, QuantizationDivision quantizationDivision)[] options = 
                [
                    ("1/1", QuantizationBase.Base_1, QuantizationDivision.Division_1),
                    ("1/2", QuantizationBase.Base_1, QuantizationDivision.Division_2),
                    ("1/4", QuantizationBase.Base_1, QuantizationDivision.Division_4),
                    ("1/8", QuantizationBase.Base_1, QuantizationDivision.Division_8),
                    ("1/16", QuantizationBase.Base_1, QuantizationDivision.Division_16),
                    ("1/32", QuantizationBase.Base_1, QuantizationDivision.Division_32),
                    ("1/3", QuantizationBase.Base_3, QuantizationDivision.Division_1),
                    ("1/6", QuantizationBase.Base_3, QuantizationDivision.Division_2),
                    ("1/12", QuantizationBase.Base_3, QuantizationDivision.Division_4),
                    ("1/24", QuantizationBase.Base_3, QuantizationDivision.Division_8),
                    ("1/48", QuantizationBase.Base_3, QuantizationDivision.Division_16),
                    ("1/96", QuantizationBase.Base_3, QuantizationDivision.Division_32),
                    ("1/5", QuantizationBase.Base_5, QuantizationDivision.Division_1),
                    ("1/10", QuantizationBase.Base_5, QuantizationDivision.Division_2),
                    ("1/20", QuantizationBase.Base_5, QuantizationDivision.Division_4),
                    ("1/40", QuantizationBase.Base_5, QuantizationDivision.Division_8),
                    ("1/80", QuantizationBase.Base_5, QuantizationDivision.Division_16),
                    ("1/160", QuantizationBase.Base_5, QuantizationDivision.Division_32),
                ];
                quantizationComboBox.SetConfig(new(options.Select(option => option.option).ToList(), 3));
                quantizationComboBox.ValueCommited.Subscribe(() => { var index = quantizationComboBox.Index; if ((uint)index >= options.Length) return; mQuantizationChanged.Invoke(options[index].quantizationBase, options[index].quantizationDivision); });
                quantizationPanel.Children.Add(quantizationComboBox);
            }
            dockPanel.AddDock(quantizationPanel, Dock.Right);

            var pianoToolPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            {
                void AddButton(PianoTool tool, SvgIcon icon, string tipText)
                {
                    var toggle = new Toggle() { Width = 36, Height = 36 }
                        .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                        .AddContent(new() { Item = new IconItem() { Icon = icon }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });
                    void OnPianoToolChanged()
                    {
                        toggle.Display(mDependency.PianoTool == tool);
                    }
                    SetupToolTip(toggle, tipText);
                    toggle.AllowSwitch += () => !toggle.IsChecked;
                    toggle.Switched += () => mDependency.PianoTool = tool;
                    mDependency.PianoToolChanged.Subscribe(OnPianoToolChanged);
                    pianoToolPanel.Children.Add(toggle);
                    OnPianoToolChanged();
                }
                AddButton(PianoTool.Note, Assets.Pointer, "Note Tool");
                AddButton(PianoTool.Pitch, Assets.Pitch, "Pitch Pen");
                AddButton(PianoTool.Anchor, Assets.Anchor, "Anchor Tool");
                AddButton(PianoTool.Lock, Assets.Brush, "Pitch Lock Brush");
                AddButton(PianoTool.Vibrato, Assets.Vibrato, "Vibrato Tool");
                AddButton(PianoTool.Select, Assets.Select, "Selection Tool");
            }
            dockPanel.AddDock(pianoToolPanel);
        }
        Children.Add(dockPanel);

        Height = 60;
        Background = Style.BACK.ToBrush();
    }

    class Mover : MovableComponent
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Style.INTERFACE.ToBrush(), this.Rect());
        }
    }

    bool mIsAutoPage = false;
    readonly ActionEvent mIsAutoPageChanged = new();
    readonly ActionEvent<QuantizationBase, QuantizationDivision> mQuantizationChanged = new();

    readonly IDependency mDependency;
}
