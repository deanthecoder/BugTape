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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using BugTape.Viewer.Models;
using BugTape.Viewer.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BugTape.Viewer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private const double MarkerCanvasWidth = 1800.0;
    private readonly List<MetricSeries> m_metricSeries = new List<MetricSeries>();

    [ObservableProperty]
    private string _packagePath = GetDefaultPackagePath();

    [ObservableProperty]
    private string _status = "Load an unzipped BugTape support package folder.";

    [ObservableProperty]
    private string _manifestSummary = "No package loaded.";

    [ObservableProperty]
    private string _stateSummary = string.Empty;

    [ObservableProperty]
    private TimelineTreeNode _selectedTreeNode;

    [ObservableProperty]
    private string _selectedJson = string.Empty;

    [ObservableProperty]
    private string _selectedLogExcerpt = "Select a timeline item to see nearby packaged log lines.";

    [ObservableProperty]
    private TimelineHighlight _selectedHighlight;

    [ObservableProperty]
    private string _selectedMetricKey = "cpu";

    [ObservableProperty]
    private string _selectedMetricSummary = "No metric overlay selected.";

    [ObservableProperty]
    private string _selectedMetricBrush = "#0ea5e9";

    public double TimelineWidth => MarkerCanvasWidth;

    public ObservableCollection<MetricOverlayOption> MetricOptions { get; } = new ObservableCollection<MetricOverlayOption>
    {
        new MetricOverlayOption
        {
            Key = "cpu",
            Label = "CPU",
            Brush = "#f97316",
            IconPath = "M12,3 A9,9 0 0,0 3,12 A9,9 0 0,0 5.64,18.36 L7.05,16.95 A7,7 0 1,1 16.95,16.95 L18.36,18.36 A9,9 0 0,0 12,3 Z M11,6 L13,6 L13,8 L11,8 Z M6,11 L8,11 L8,13 L6,13 Z M16,11 L18,11 L18,13 L16,13 Z M8.2,7.1 L9.6,8.5 L8.2,9.9 L6.8,8.5 Z M14.4,8.5 L15.8,7.1 L17.2,8.5 L15.8,9.9 Z M12,12 L16,8 L17.2,9.2 L13.2,13.2 A2,2 0 1,1 12,12 Z"
        },
        new MetricOverlayOption
        {
            Key = "working",
            Label = "Memory",
            Brush = "#16a34a",
            IconPath = "M7,7 L17,7 L17,17 L7,17 Z M9,9 L9,15 L15,15 L15,9 Z M4,9 L6,9 L6,11 L4,11 Z M4,13 L6,13 L6,15 L4,15 Z M18,9 L20,9 L20,11 L18,11 Z M18,13 L20,13 L20,15 L18,15 Z M9,4 L11,4 L11,6 L9,6 Z M13,4 L15,4 L15,6 L13,6 Z M9,18 L11,18 L11,20 L9,20 Z M13,18 L15,18 L15,20 L13,20 Z"
        }
    };

    public ObservableCollection<BugTapeRecord> Records { get; } = new();

    public ObservableCollection<TimelineMarker> TimelineMarkers { get; } = new();

    public ObservableCollection<TimelineTick> TimelineTicks { get; } = new();

    public ObservableCollection<TimelineTreeNode> TreeNodes { get; } = new();

    public ObservableCollection<MetricSegment> SelectedMetricSegments { get; } = new();

    public MainWindowViewModel()
    {
        if (!string.IsNullOrWhiteSpace(PackagePath))
            LoadPackage();
    }

    partial void OnSelectedTreeNodeChanged(TimelineTreeNode value)
    {
        SelectedJson = value?.Json ?? string.Empty;
        SelectedLogExcerpt = value?.LogExcerpt ?? "Select a timeline item to see nearby packaged log lines.";
        SelectedHighlight = value == null
            ? null
            : new TimelineHighlight
            {
                Left = value.TimelineLeft,
                Width = Math.Max(8.0, value.TimelineWidth)
            };
    }

    partial void OnSelectedMetricKeyChanged(string value)
    {
        UpdateSelectedMetric();
    }

    public void SelectMetric(string key)
    {
        SelectedMetricKey = string.IsNullOrWhiteSpace(key) ? "cpu" : key;
        UpdateSelectedMetric();
    }

    [RelayCommand]
    private void LoadPackage()
    {
        try
        {
            var session = BugTapePackageReader.Load(PackagePath);

            Records.Clear();
            foreach (var record in session.Records)
                Records.Add(record);

            TimelineMarkers.Clear();
            foreach (var marker in session.Markers)
                TimelineMarkers.Add(marker);

            TimelineTicks.Clear();
            foreach (var tick in session.Ticks)
                TimelineTicks.Add(tick);

            m_metricSeries.Clear();
            m_metricSeries.AddRange(session.MetricSeries);
            UpdateSelectedMetric();

            TreeNodes.Clear();
            foreach (var node in session.Tree)
                TreeNodes.Add(node);

            ManifestSummary = session.ManifestSummary;
            StateSummary = session.StateSummary;
            SelectedTreeNode = TreeNodes.Count > 0 ? TreeNodes[0] : null;
            Status = $"Loaded {Records.Count} timeline records from {session.PackagePath}";
        }
        catch (Exception ex)
        {
            Records.Clear();
            TimelineMarkers.Clear();
            TimelineTicks.Clear();
            m_metricSeries.Clear();
            UpdateSelectedMetric();
            TreeNodes.Clear();
            SelectedTreeNode = null;
            ManifestSummary = "No package loaded.";
            StateSummary = string.Empty;
            SelectedLogExcerpt = "Select a timeline item to see nearby packaged log lines.";
            Status = ex.Message;
        }
    }

    private static string GetDefaultPackagePath()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "SupportPackage");
        return Directory.Exists(downloads) ? downloads : string.Empty;
    }

    private void UpdateSelectedMetric()
    {
        var series = m_metricSeries.FirstOrDefault(item => item.Key == SelectedMetricKey);
        if (series == null || series.Segments.Count == 0)
        {
            SelectedMetricSegments.Clear();
            SelectedMetricSummary = $"No {SelectedMetricKey} metric data in this support package.";
            SelectedMetricBrush = "#0ea5e9";
            return;
        }

        SelectedMetricSegments.Clear();
        foreach (var segment in series.Segments)
            SelectedMetricSegments.Add(segment);
        SelectedMetricSummary = series.Summary;
        SelectedMetricBrush = series.Brush;
    }
}
