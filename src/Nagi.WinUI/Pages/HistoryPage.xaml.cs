using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nagi.WinUI.Models;
using Nagi.WinUI.Resources;
using Nagi.WinUI.ViewModels;

namespace Nagi.WinUI.Pages;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        ViewModel = App.Services!.GetRequiredService<HistoryViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
    }

    public HistoryViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.LoadAsync();
    }

    private async void ListView_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel.SelectedEntry != null)
            await ViewModel.PlayEntryCommand.ExecuteAsync(ViewModel.SelectedEntry);
    }

    private async void ListView_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ViewModel.SelectedEntry != null)
        {
            await ViewModel.PlayEntryCommand.ExecuteAsync(ViewModel.SelectedEntry);
            e.Handled = true;
        }
    }

    private async void PlaySelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedEntry != null)
            await ViewModel.PlayEntryCommand.ExecuteAsync(ViewModel.SelectedEntry);
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = Strings.History_ClearConfirmTitle,
            Content = Strings.History_ClearConfirmMessage,
            PrimaryButtonText = Strings.History_ClearConfirmButton,
            CloseButtonText = Strings.Generic_Cancel,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await ViewModel.ClearHistoryCommand.ExecuteAsync(null);
    }
}
