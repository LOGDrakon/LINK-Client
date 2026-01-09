using LINK.Example.WPF.Discovery.ViewModels;
using System.Windows;

namespace LINK.Example.WPF.Discovery;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
