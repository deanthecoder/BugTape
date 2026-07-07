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
    private string _selectedMetricKey = "none";

    [ObservableProperty]
    private string _selectedMetricSummary = "No metric overlay selected.";

    [ObservableProperty]
    private string _selectedMetricBrush = "#0ea5e9";

    public double TimelineWidth => MarkerCanvasWidth;

    public ObservableCollection<MetricOverlayOption> MetricOptions { get; } = new ObservableCollection<MetricOverlayOption>
    {
        new MetricOverlayOption { Key = "none", Label = "None", Icon = "—" },
        new MetricOverlayOption { Key = "cpu", Label = "CPU", Icon = "⚙" },
        new MetricOverlayOption { Key = "working", Label = "Memory", Icon = "M" }
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
        SelectedMetricKey = string.IsNullOrWhiteSpace(key) ? "none" : key;
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
        if (SelectedMetricKey == "none")
        {
            SelectedMetricSegments.Clear();
            SelectedMetricSummary = "No metric overlay selected.";
            SelectedMetricBrush = "#0ea5e9";
            return;
        }

        var series = m_metricSeries.FirstOrDefault(item => item.Key == SelectedMetricKey);
        if (series == null || series.Segments.Count == 0)
        {
            SelectedMetricSegments.Clear();
            SelectedMetricSummary = "No data for selected metric in this support package.";
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
