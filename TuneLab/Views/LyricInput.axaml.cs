using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using System;
using TuneLab.Utils;
using TuneLab.GUI;

namespace TuneLab.Views
{
    public partial class LyricInput : Window
    {
        public LyricInput()
        {
            InitializeComponent();
            Focusable = true;
            CanResize = false;
            WindowState = WindowState.Normal;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;

            this.DataContext = this;
            this.Background = Style.INTERFACE.ToBrush();
            TitleBar.Background = Style.BACK.ToBrush();
        }
    }
}
