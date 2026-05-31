using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Nagi.WinUI.ViewModels;

namespace Nagi.WinUI.Pages;

public sealed partial class DownloadsPage : Page
{
    public DownloadsPage()
    {
        ViewModel = App.Services!.GetRequiredService<DownloadsViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
    }

    public DownloadsViewModel ViewModel { get; }
}
