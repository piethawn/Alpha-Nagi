using System;
using System.ComponentModel;
using System.Numerics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Nagi.Core.Models.Lyrics;
using Nagi.WinUI.Helpers;
using Nagi.WinUI.ViewModels;

namespace Nagi.WinUI.Pages;

/// <summary>
///     The Now Playing page — three-column layout showing album art, synced lyrics, and
///     the upcoming queue. Lyrics logic is shared with <see cref="LyricsPage" />.
/// </summary>
public sealed partial class NowPlayingPage : Page
{
    private const double ScrollIntoViewRatio = 0.40;
    private const int AnimationDurationMs = 300;
    private const float ActiveScale = 1.03f;
    private const float InactiveScale = 1.0f;
    private static readonly double[] _opacityCurve;
    private static readonly SolidColorBrush _transparentBrush = new(Microsoft.UI.Colors.Transparent);

    private bool _isUnloaded;
    private bool _isInitialScroll = true;
    private int _lastActiveOverlayIndex = -1;
    private int _currentLineIndex = -1;

    static NowPlayingPage()
    {
        _opacityCurve = new double[20];
        for (var i = 0; i < _opacityCurve.Length; i++)
            _opacityCurve[i] = Math.Max(0.05, Math.Pow(0.55, i));
    }

    public NowPlayingPage()
    {
        ViewModel = App.Services!.GetRequiredService<NowPlayingViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
        Unloaded += OnPageUnloaded;
    }

    public NowPlayingViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.LyricsViewModel.PropertyChanged += OnLyricsViewModelPropertyChanged;
        _isInitialScroll = true;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.LyricsViewModel.PropertyChanged -= OnLyricsViewModelPropertyChanged;
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        _isUnloaded = true;
        ViewModel.LyricsViewModel.PropertyChanged -= OnLyricsViewModelPropertyChanged;
        ViewModel.Dispose();
    }

    private void OnLyricsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LyricsPageViewModel.CurrentLine))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isUnloaded) return;
                _currentLineIndex = ViewModel.LyricsViewModel.CurrentLine != null
                    ? ViewModel.LyricsViewModel.LyricLines.IndexOf(ViewModel.LyricsViewModel.CurrentLine)
                    : -1;

                if (_isInitialScroll)
                {
                    _isInitialScroll = false;
                    ApplyAllLineVisuals();
                    ScrollToCurrentLine(disableAnimation: true);
                }
                else
                {
                    AnimateLineTransition();
                    ScrollToCurrentLine(disableAnimation: false);
                }
            });
        }
        else if (e.PropertyName == nameof(LyricsPageViewModel.HasLyrics))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isUnloaded) return;
                if (ViewModel.LyricsViewModel.HasLyrics)
                {
                    _isInitialScroll = true;
                    _lastActiveOverlayIndex = -1;
                    _currentLineIndex = -1;
                }
            });
        }
    }

    private void LyricsRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is not Grid grid) return;

        var currentIndex = _currentLineIndex;
        var distance = currentIndex >= 0 ? Math.Abs(args.Index - currentIndex) : int.MaxValue;
        var targetOpacity = GetOpacityForDistance(distance);
        var isActive = args.Index == currentIndex;

        var visual = ElementCompositionPreview.GetElementVisual(grid);
        var scale = isActive ? ActiveScale : InactiveScale;
        visual.Scale = new Vector3(scale, scale, 1.0f);

        if (grid.Children.Count >= 2)
        {
            if (grid.Children[0] is TextBlock baseText)
                CompositionAnimationHelper.SetOpacityImmediate(baseText, (float)targetOpacity);
            if (grid.Children[1] is TextBlock overlayText)
                CompositionAnimationHelper.SetOpacityImmediate(overlayText, isActive ? 1.0f : 0.0f);
        }
    }

    private void LyricsRepeater_ElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is not Grid grid) return;

        var visual = ElementCompositionPreview.GetElementVisual(grid);
        visual.StopAnimation("Scale");
        visual.Scale = new Vector3(1.0f, 1.0f, 1.0f);

        if (grid.Children.Count >= 2)
        {
            if (grid.Children[0] is TextBlock baseText)
            {
                var bv = ElementCompositionPreview.GetElementVisual(baseText);
                bv.StopAnimation("Opacity");
                bv.Opacity = 1.0f;
            }
            if (grid.Children[1] is TextBlock overlayText)
            {
                var ov = ElementCompositionPreview.GetElementVisual(overlayText);
                ov.StopAnimation("Opacity");
                ov.Opacity = 0.0f;
            }
        }
    }

    private void ScrollToCurrentLine(bool disableAnimation)
    {
        var lineIndex = _currentLineIndex;
        if (lineIndex < 0) return;

        var element = LyricsRepeater.TryGetElement(lineIndex);
        if (element == null) return;

        var transform = element.TransformToVisual(LyricsRepeater);
        var position = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
        var targetOffset = position.Y;
        var maxOffset = LyricsScrollViewer.ScrollableHeight;
        targetOffset = Math.Max(0, Math.Min(targetOffset, maxOffset));
        LyricsScrollViewer.ChangeView(null, targetOffset, null, disableAnimation);
    }

    private void AnimateLineTransition()
    {
        var currentIndex = _currentLineIndex;
        var hasLast = _lastActiveOverlayIndex >= 0;
        var hasCurrent = currentIndex >= 0;
        if (!hasLast && !hasCurrent) return;

        var windowStart = hasLast && hasCurrent
            ? Math.Min(_lastActiveOverlayIndex, currentIndex) - 6
            : hasLast ? _lastActiveOverlayIndex - 6 : currentIndex - 6;
        var windowEnd = hasLast && hasCurrent
            ? Math.Max(_lastActiveOverlayIndex, currentIndex) + 6
            : hasLast ? _lastActiveOverlayIndex + 6 : currentIndex + 6;

        UpdateLinesInRange(
            Math.Max(0, windowStart),
            Math.Min(ViewModel.LyricsViewModel.LyricLines.Count - 1, windowEnd),
            currentIndex, true);

        _lastActiveOverlayIndex = currentIndex;
    }

    private void ApplyAllLineVisuals()
    {
        var currentIndex = _currentLineIndex;
        if (ViewModel.LyricsViewModel.LyricLines.Count > 0)
            UpdateLinesInRange(0, ViewModel.LyricsViewModel.LyricLines.Count - 1, currentIndex, false);
        _lastActiveOverlayIndex = currentIndex;
    }

    private void UpdateLinesInRange(int minIndex, int maxIndex, int currentIndex, bool animate)
    {
        for (var i = minIndex; i <= maxIndex; i++)
        {
            var element = LyricsRepeater.TryGetElement(i);
            if (element is not Grid grid) continue;

            var distance = currentIndex >= 0 ? Math.Abs(i - currentIndex) : int.MaxValue;
            var targetOpacity = GetOpacityForDistance(distance);
            var isActive = i == currentIndex;

            if (grid.Children.Count >= 2)
            {
                if (grid.Children[0] is TextBlock baseText)
                {
                    if (animate) CompositionAnimationHelper.AnimateOpacity(baseText, (float)targetOpacity, AnimationDurationMs);
                    else CompositionAnimationHelper.SetOpacityImmediate(baseText, (float)targetOpacity);
                }
                if (grid.Children[1] is TextBlock overlayText)
                {
                    if (animate) CompositionAnimationHelper.AnimateOpacity(overlayText, isActive ? 1.0f : 0.0f, AnimationDurationMs);
                    else CompositionAnimationHelper.SetOpacityImmediate(overlayText, isActive ? 1.0f : 0.0f);
                }
            }

            var targetScale = isActive ? ActiveScale : InactiveScale;
            if (animate)
            {
                CompositionAnimationHelper.AnimateScale(grid, targetScale, AnimationDurationMs);
            }
            else
            {
                var visual = ElementCompositionPreview.GetElementVisual(grid);
                visual.Scale = new Vector3(targetScale, targetScale, 1.0f);
            }
        }
    }

    private static double GetOpacityForDistance(int distance) =>
        distance < _opacityCurve.Length ? _opacityCurve[distance] : 0.05;

    private void LyricItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid)
            grid.Background = Application.Current.Resources["SubtleFillColorSecondaryBrush"] as Brush ?? _transparentBrush;
    }

    private void LyricItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid) grid.Background = _transparentBrush;
    }

    private void LyricItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.Tag is LyricLine clickedLine)
            _ = ViewModel.LyricsViewModel.SeekToLineCommand.ExecuteAsync(clickedLine);
    }

    private async void QueueItem_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is QueueEntry entry)
            await ViewModel.JumpToQueueItemCommand.ExecuteAsync(entry);
    }
}
