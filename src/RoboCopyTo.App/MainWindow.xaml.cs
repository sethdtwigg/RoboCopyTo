using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using RoboCopyTo.App.Services;
using RoboCopyTo.App.ViewModels;
using RoboCopyTo.Core.Planning;

namespace RoboCopyTo.App;

public partial class MainWindow : Window, IUserPrompts
{
    private MainViewModel _vm = null!;
    private bool _closingConfirmed;

    /// <summary>Parses the XAML only; call <see cref="Initialize"/> with the selection before showing.</summary>
    /// <remarks>Split so the window can be built during the multi-select quiet period.</remarks>
    public MainWindow()
    {
        InitializeComponent();
    }

    public void Initialize(IReadOnlyList<string> paths, ElevationJob? elevatedJob = null)
    {
        _vm = new MainViewModel(paths, this, elevatedJob);
        DataContext = _vm;
        _vm.OutputAppended += OnOutputAppended;
        Loaded += async (_, _) => await _vm.OnLoadedAsync();
    }

    public MainViewModel ViewModel => _vm;

    private ScrollViewer? _outputScroller;
    private bool _followOutput = true;

    /// <summary>Keeps the newest output in view, unless the user has scrolled up to read.</summary>
    private void OnOutputAppended()
    {
        if (_outputScroller is null)
        {
            _outputScroller = FindChild<ScrollViewer>(OutputList);
            if (_outputScroller is not null)
                _outputScroller.ScrollChanged += (_, e) =>
                {
                    // Only a user scroll (no extent change) decides whether to keep following.
                    if (e.ExtentHeightChange == 0)
                        _followOutput = _outputScroller.VerticalOffset >= _outputScroller.ScrollableHeight - 1;
                };
        }
        if (_followOutput)
            _outputScroller?.ScrollToEnd();
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            if (FindChild<T>(child) is { } nested)
                return nested;
        }
        return null;
    }

    private void RecentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.RecentDestinations.Count == 0)
        {
            ShowInfo("Recent destinations", "There are no recent destinations yet. Destinations are remembered after you click Start or Dry run.");
            return;
        }
        RecentMenu.DataContext = _vm;
        RecentMenu.PlacementTarget = RecentButton;
        RecentMenu.Placement = PlacementMode.Bottom;
        RecentMenu.IsOpen = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_closingConfirmed && !_vm.ConfirmClose())
            e.Cancel = true;
        base.OnClosing(e);
    }

    // ───────────── IUserPrompts ─────────────

    public void ShowError(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowInfo(string title, string message)
        => MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public bool Confirm(string title, string message, bool warning = false)
        => MessageBox.Show(this, message, title, MessageBoxButton.YesNo, warning ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No)
           == MessageBoxResult.Yes;

    public string? AskText(string title, string prompt, string initial)
    {
        var dialog = new TextPromptWindow(title, prompt, initial) { Owner = this };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public string? BrowseFolder(string? initial)
    {
        var dialog = new OpenFolderDialog { Title = "Choose the destination folder", Multiselect = false };
        if (initial is not null)
            dialog.InitialDirectory = initial;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    public void CloseWindow()
    {
        _closingConfirmed = true;
        Close();
    }
}
