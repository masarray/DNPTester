using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Dnp3MasterTester.ViewModels;

namespace Dnp3MasterTester;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private const int ContentTransitionMs = 120;
    private const int UiTransitionHoldMs = 210;

    private readonly HashSet<DataGrid> _pendingAutoScrollGrids = new();
    private bool _segmentIndicatorInitialized;
    private bool _isAutoScrollDrainScheduled;
    private int _lastSelectedTabIndex = -1;
    private DispatcherTimer? _postTransitionScrollTimer;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        _viewModel.EventLogs.CollectionChanged += (_, e) => OnLiveLogCollectionChanged(e, MissionEventLogGrid, ScadaEventsGrid);
        _viewModel.SoeAudit.CollectionChanged += (_, e) => OnLiveLogCollectionChanged(e, SoeAuditGrid);
        _viewModel.LinkTrace.CollectionChanged += (_, e) => OnLiveLogCollectionChanged(e, LinkTraceGrid);
        Dispatcher.BeginInvoke(NavigateReportPreview, DispatcherPriority.Loaded);
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ReportPreviewPath))
        {
            Dispatcher.BeginInvoke(NavigateReportPreview, DispatcherPriority.Background);
        }
    }

    private void NavigateReportPreview()
    {
        if (string.IsNullOrWhiteSpace(_viewModel.ReportPreviewPath) || !File.Exists(_viewModel.ReportPreviewPath))
        {
            return;
        }

        ReportPdfViewer.Source = new Uri(_viewModel.ReportPreviewPath);
    }

    private void OpenConnectionSetup_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConnectionSettingsWindow(_viewModel)
        {
            Owner = this
        };

        dialog.ShowDialog();
    }

    private void OpenAbout_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedItem = AboutTab;
    }

    private void MainTabs_OnLoaded(object sender, RoutedEventArgs e)
    {
        _lastSelectedTabIndex = MainTabs.SelectedIndex;
        Dispatcher.BeginInvoke(UpdateSegmentIndicator, DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(PrepareSelectedContentHost, DispatcherPriority.Loaded);
        Dispatcher.BeginInvoke(ScrollSelectedLiveLogToLatest, DispatcherPriority.ContextIdle);
    }

    private void MainTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source != MainTabs)
        {
            return;
        }

        var selectedIndex = MainTabs.SelectedIndex;
        var direction = _lastSelectedTabIndex >= 0 && selectedIndex < _lastSelectedTabIndex ? -1 : 1;
        _lastSelectedTabIndex = selectedIndex;

        _viewModel.SuspendHeavyUiFlush(TimeSpan.FromMilliseconds(UiTransitionHoldMs));
        Dispatcher.BeginInvoke(UpdateSegmentIndicator, DispatcherPriority.Render);
        AnimateSelectedContent(direction, isInitial: false);
        SchedulePostTransitionLiveScroll();
    }

    private void MainTabs_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateSegmentIndicator, DispatcherPriority.Background);
    }

    private void UpdateSegmentIndicator()
    {
        if (!IsLoaded)
        {
            return;
        }

        if (MainTabs.Template.FindName("SegmentIndicator", MainTabs) is not Border indicator ||
            MainTabs.Template.FindName("HeaderPanel", MainTabs) is not Panel headerPanel ||
            MainTabs.ItemContainerGenerator.ContainerFromItem(MainTabs.SelectedItem) is not TabItem selectedTab ||
            selectedTab.ActualWidth <= 0)
        {
            return;
        }

        var target = selectedTab.TranslatePoint(new Point(0, 0), headerPanel);
        var transforms = indicator.RenderTransform as TransformGroup;
        if (transforms is null || transforms.IsFrozen)
        {
            transforms = new TransformGroup();
            transforms.Children.Add(new ScaleTransform(1, 1));
            transforms.Children.Add(new TranslateTransform());
            indicator.RenderTransform = transforms;
        }

        var scale = transforms.Children.OfType<ScaleTransform>().FirstOrDefault();
        var transform = transforms.Children.OfType<TranslateTransform>().FirstOrDefault();
        if (scale is null || transform is null)
        {
            transforms.Children.Clear();
            scale = new ScaleTransform(1, 1);
            transform = new TranslateTransform();
            transforms.Children.Add(scale);
            transforms.Children.Add(transform);
        }

        var targetWidth = Math.Max(0, selectedTab.ActualWidth);
        var targetX = target.X;

        if (!_segmentIndicatorInitialized)
        {
            indicator.Width = targetWidth;
            transform.X = targetX;
            _segmentIndicatorInitialized = true;
            return;
        }

        var settleEase = new QuarticEase { EasingMode = EasingMode.EaseOut };
        var ballisticEase = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.26 };
        var duration = TimeSpan.FromMilliseconds(210);

        indicator.BeginAnimation(WidthProperty, new DoubleAnimation
        {
            To = targetWidth,
            Duration = duration,
            EasingFunction = settleEase
        });

        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            To = targetX,
            Duration = TimeSpan.FromMilliseconds(235),
            EasingFunction = ballisticEase
        });

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
        {
            From = 1.045,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(230),
            EasingFunction = settleEase
        });

        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
        {
            From = 0.965,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(230),
            EasingFunction = settleEase
        });
    }

    private void PrepareSelectedContentHost()
    {
        if (MainTabs.Template.FindName("PART_SelectedContentHost", MainTabs) is not ContentPresenter host)
        {
            return;
        }

        host.Opacity = 1;
        host.RenderTransformOrigin = new Point(0.5, 0.5);
        if (host.CacheMode is null)
        {
            host.CacheMode = new BitmapCache { RenderAtScale = 1 };
        }

        if (host.RenderTransform is not TranslateTransform transform || host.RenderTransform.IsFrozen)
        {
            transform = new TranslateTransform();
            host.RenderTransform = transform;
        }

        // Workspace pages should not slide or spring. Keep the transform available
        // only as a safe reset point for older patches and template reuse.
        transform.X = 0;
    }

    private void AnimateSelectedContent(int direction, bool isInitial)
    {
        if (MainTabs.Template.FindName("PART_SelectedContentHost", MainTabs) is not ContentPresenter host)
        {
            return;
        }

        PrepareSelectedContentHost();

        if (host.RenderTransform is TranslateTransform transform)
        {
            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
        }

        host.BeginAnimation(OpacityProperty, null);

        // Workspace page transition: dissolve only.
        // No ballistic easing, no horizontal slide, no layout motion.
        // Keep the opacity range shallow so DataGrid/WebView content does not flash blank.
        var duration = TimeSpan.FromMilliseconds(isInitial ? 80 : ContentTransitionMs);
        var easing = new SineEase { EasingMode = EasingMode.EaseOut };
        var fromOpacity = isInitial ? 1.0 : 0.94;

        host.Opacity = fromOpacity;

        host.BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = fromOpacity,
            To = 1.0,
            Duration = duration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        });

        Dispatcher.BeginInvoke(() =>
        {
            host.Opacity = 1.0;
            if (host.RenderTransform is TranslateTransform resetTransform)
            {
                resetTransform.X = 0;
            }
        }, DispatcherPriority.ContextIdle);
    }

    private void OnLiveLogCollectionChanged(NotifyCollectionChangedEventArgs e, params DataGrid[] grids)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || e.NewItems.Count == 0)
        {
            return;
        }

        foreach (var grid in grids)
        {
            QueueAutoScrollToLatest(grid, force: false);
        }
    }

    private void SchedulePostTransitionLiveScroll()
    {
        _postTransitionScrollTimer?.Stop();
        _postTransitionScrollTimer = new DispatcherTimer(DispatcherPriority.ContextIdle, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(UiTransitionHoldMs + 20)
        };
        _postTransitionScrollTimer.Tick += (_, _) =>
        {
            _postTransitionScrollTimer?.Stop();
            _postTransitionScrollTimer = null;
            ScrollSelectedLiveLogToLatest();
        };
        _postTransitionScrollTimer.Start();
    }

    private void ScrollSelectedLiveLogToLatest()
    {
        if (!IsLoaded)
        {
            return;
        }

        if (MainTabs.SelectedItem is TabItem { Header: string header })
        {
            if (string.Equals(header, "Mission Control", StringComparison.Ordinal))
            {
                QueueAutoScrollToLatest(MissionEventLogGrid, force: true);
            }
            else if (string.Equals(header, "SCADA Events", StringComparison.Ordinal))
            {
                QueueAutoScrollToLatest(ScadaEventsGrid, force: true);
            }
            else if (string.Equals(header, "SOE Audit", StringComparison.Ordinal))
            {
                QueueAutoScrollToLatest(SoeAuditGrid, force: true);
            }
            else if (string.Equals(header, "Link Trace", StringComparison.Ordinal))
            {
                QueueAutoScrollToLatest(LinkTraceGrid, force: true);
            }
        }
    }

    private void QueueAutoScrollToLatest(DataGrid grid, bool force)
    {
        if (grid.Items.Count == 0)
        {
            return;
        }

        if (!force && !ShouldFollowLive(grid))
        {
            return;
        }

        _pendingAutoScrollGrids.Add(grid);
        if (_isAutoScrollDrainScheduled)
        {
            return;
        }

        _isAutoScrollDrainScheduled = true;
        Dispatcher.BeginInvoke(DrainAutoScrollQueue, DispatcherPriority.ContextIdle);
    }

    private void DrainAutoScrollQueue()
    {
        _isAutoScrollDrainScheduled = false;
        var grids = _pendingAutoScrollGrids.ToArray();
        _pendingAutoScrollGrids.Clear();

        foreach (var grid in grids)
        {
            ScrollGridToLatest(grid);
        }
    }

    private static void ScrollGridToLatest(DataGrid grid)
    {
        if (!grid.IsVisible || grid.Items.Count == 0)
        {
            return;
        }

        var latestItem = grid.Items[grid.Items.Count - 1];
        if (latestItem is null)
        {
            return;
        }

        grid.ScrollIntoView(latestItem);
    }

    private static bool ShouldFollowLive(DataGrid grid)
    {
        if (!grid.IsVisible)
        {
            return false;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(grid);
        if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0)
        {
            return true;
        }

        return scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 1.5;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var nestedChild = FindVisualChild<T>(child);
            if (nestedChild is not null)
            {
                return nestedChild;
            }
        }

        return null;
    }
}
