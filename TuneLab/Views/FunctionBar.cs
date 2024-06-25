using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Event;
using TuneLab.GUI;
using TuneLab.GUI.Components;
using TuneLab.Utils;

namespace TuneLab.Views;

internal class FunctionBar : LayerPanel
{
    public event Action<double>? Moved;
    
    public interface IDependency
    {
        public IActionEvent PianoToolChanged { get; }
        public IActionEvent IsAutoPageChanged { get; }
        public PianoTool PianoTool { get; set; }
        public bool IsAutoPage { get; set; }
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

            var audioControlPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, Margin = new(12, 0) };
            {
                var playButton = new Toggle() { Width = 36, Height = 36 }
                    .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                    .AddContent(new() { Item = new IconItem() { Icon = Assets.Play }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });

                playButton.Switched += () => { if (playButton.IsChecked) AudioEngine.Play(); else AudioEngine.Pause(); };
                AudioEngine.PlayStateChanged += () => { playButton.Display(AudioEngine.IsPlaying); };
                audioControlPanel.Children.Add(playButton);

                var autoPageButton = new Toggle() { Width = 36, Height = 36 }
                    .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                    .AddContent(new() { Item = new IconItem() { Icon = Assets.AutoPage }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });

                autoPageButton.Switched += () => mDependency.IsAutoPage = autoPageButton.IsChecked;
                mDependency.IsAutoPageChanged.Subscribe(() => { autoPageButton.Display(mDependency.IsAutoPage); });
                audioControlPanel.Children.Add(autoPageButton);
            }
            DockPanel.SetDock(audioControlPanel, Dock.Left);
            dockPanel.Children.Add(audioControlPanel);  

            var pianoToolPanel = new StackPanel() { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            {
                void AddButton(PianoTool tool, SvgIcon icon)
                {
                    var toggle = new Toggle() { Width = 36, Height = 36 }
                        .AddContent(new() { Item = new BorderItem() { CornerRadius = 4 }, CheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack }, UncheckedColorSet = new() { HoveredColor = hoverBack, PressedColor = hoverBack } })
                        .AddContent(new() { Item = new IconItem() { Icon = icon }, CheckedColorSet = new() { Color = Colors.White }, UncheckedColorSet = new() { Color = Style.LIGHT_WHITE.Opacity(0.5) } });
                    void OnPianoToolChanged()
                    {
                        toggle.Display(mDependency.PianoTool == tool);
                    }
                    toggle.AllowSwitch += () => !toggle.IsChecked;
                    toggle.Switched += () => mDependency.PianoTool = tool;
                    mDependency.PianoToolChanged.Subscribe(OnPianoToolChanged);
                    pianoToolPanel.Children.Add(toggle);
                    OnPianoToolChanged();
                }
                AddButton(PianoTool.Note, Assets.Pointer);
                AddButton(PianoTool.Pitch, Assets.Pitch);
                AddButton(PianoTool.Anchor, Assets.Anchor);
                AddButton(PianoTool.Lock, Assets.Brush);
                AddButton(PianoTool.Vibrato, Assets.Vibrato);
                AddButton(PianoTool.Select, Assets.Select);
            }
            dockPanel.Children.Add(pianoToolPanel);
        }
        Children.Add(dockPanel);



        Height = 64;
        Background = Style.BACK.ToBrush();
    }

    class Mover : MovableComponent
    {
        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Style.INTERFACE.ToBrush(), this.Rect());
        }
    }

    readonly IDependency mDependency;
}
