using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DiscordPurger.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set the TopLevel immediately when the content is available
        SetTopLevelOnMainView();

        // Handle when the window opens
        Opened += (s, e) => SetTopLevelOnMainView();

        // Handle when DataContext changes
        DataContextChanged += (s, e) => SetTopLevelOnMainView();
        
        // Also monitor the MainView's DataContext changes
        if (Content is MainView mainView)
        {
            mainView.DataContextChanged += (s, e) => SetTopLevelOnMainView();
        }
    }

    private void SetTopLevelOnMainView()
    {
        if (Content is MainView mainView)
        {
            // Try to get the MainViewModel from the MainView's DataContext
            if (mainView.DataContext is ViewModels.MainViewModel viewModel)
            {
                viewModel.TopLevel = this;
            }
            // Also try to get it from the Window's DataContext
            else if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.TopLevel = this;
            }
        }
        // Fallback: try to get it directly from Window's DataContext
        else if (DataContext is ViewModels.MainViewModel windowVm)
        {
            windowVm.TopLevel = this;
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}