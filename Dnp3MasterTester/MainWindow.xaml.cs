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
    private bool _segmentIndicatorInitialized;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
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

    private void MainTabs_OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateSegmentIndicator, DispatcherPriority.Loaded);
    }

    private void MainTabs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == MainTabs)
        {
            Dispatcher.BeginInvoke(UpdateSegmentIndicator, DispatcherPriority.Background);
        }
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

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(190);

        indicator.BeginAnimation(WidthProperty, new DoubleAnimation
        {
            To = targetWidth,
            Duration = duration,
            EasingFunction = easing
        });

        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation
        {
            To = targetX,
            Duration = duration,
            EasingFunction = easing
        });

        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation
        {
            From = 1.035,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = easing
        });

        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation
        {
            From = 0.94,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = easing
        });
    }
}
