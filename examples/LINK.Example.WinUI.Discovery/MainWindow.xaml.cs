using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using LINK.Example.WinUI.Discovery.ViewModels;

namespace LINK.Example.WinUI.Discovery;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ((Window)this).Content = new Frame
        {
            DataContext = new MainViewModel()
        };
    }
}
