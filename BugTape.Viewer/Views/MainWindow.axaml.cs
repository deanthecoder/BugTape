// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BugTape.Viewer.Models;
using BugTape.Viewer.ViewModels;

namespace BugTape.Viewer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnTimelineTreeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (sender is TreeView treeView && treeView.SelectedItem is TimelineTreeNode node)
        {
            viewModel.SelectedTreeNode = node;
            ScrollTimelineTo(node);
            ScrollLogExcerptToTop();
        }
    }

    private async void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open BugTape support package folder",
            AllowMultiple = false
        });
        if (folders.Count == 0)
            return;

        LoadSelectedPath(folders[0].Path.LocalPath);
    }

    private async void OnOpenZipClicked(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open BugTape support package zip",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Zip files")
                {
                    Patterns = new[] { "*.zip" }
                },
                FilePickerFileTypes.All
            }
        });
        if (files.Count == 0)
            return;

        LoadSelectedPath(files[0].Path.LocalPath);
    }

    private void OnMetricOverlayClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (sender is Button button && button.Tag is string key)
            viewModel.SelectMetric(key);
    }

    private void OnTimelineMarkerPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (sender is not Control control || control.DataContext is not TimelineMarker marker)
            return;

        if (!viewModel.SelectTimelineMarker(marker) || viewModel.SelectedTreeNode == null)
            return;

        ScrollTimelineTo(viewModel.SelectedTreeNode);
        ScrollLogExcerptToTop();
        e.Handled = true;
    }

    private void LoadSelectedPath(string path)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        viewModel.PackagePath = path;
        viewModel.LoadPackageCommand.Execute(null);
        if (viewModel.SelectedTreeNode != null)
        {
            ScrollTimelineTo(viewModel.SelectedTreeNode);
            ScrollLogExcerptToTop();
        }
    }

    private void ScrollTimelineTo(TimelineTreeNode node)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        var viewportWidth = TimelineScrollViewer.Bounds.Width;
        if (viewportWidth <= 0)
            return;

        var center = node.TimelineLeft + node.TimelineWidth / 2.0;
        var target = center - viewportWidth / 2.0;
        var maximum = Math.Max(0.0, viewModel.TimelineWidth - viewportWidth);
        target = Math.Max(0.0, Math.Min(maximum, target));

        TimelineScrollViewer.Offset = new Vector(target, TimelineScrollViewer.Offset.Y);
    }

    private void ScrollLogExcerptToTop()
    {
        Dispatcher.UIThread.Post(() =>
        {
            LogExcerptTextBox.CaretIndex = 0;
            LogExcerptTextBox.SelectionStart = 0;
            LogExcerptTextBox.SelectionEnd = 0;
        });
    }
}
