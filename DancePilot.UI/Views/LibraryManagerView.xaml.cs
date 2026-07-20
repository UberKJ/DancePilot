using DancePilot.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;

namespace DancePilot.UI.Views;

public sealed partial class LibraryManagerView : UserControl
{
    public LibraryManagerView()
    {
        InitializeComponent();
    }

    private MainPageViewModel? ViewModel => DataContext as MainPageViewModel;

    private void LiveEventWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ShowLiveEventWorkspace();
    }

    private void LibraryManagerWorkspaceButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel?.ShowLibraryManagerWorkspace();
    }

    private async void BrowseLocalMusicFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.MusicLibrary
        };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return;
        }

        await viewModel.LoadLocalMusicFromFolderAsync(folder.Path);
    }

    private async void LibraryMusicSearchBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || ViewModel is null)
        {
            return;
        }

        e.Handled = true;
        await ViewModel.SearchLocalMusicCommand.ExecuteAsync(null);
    }
}
