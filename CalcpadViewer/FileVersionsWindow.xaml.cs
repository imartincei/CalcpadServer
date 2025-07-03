using System.IO;
using System.Windows;
using Minio;
using Minio.DataModel.Args;
using CalcpadViewer.Models;
using Microsoft.Win32;

namespace CalcpadViewer;

public partial class FileVersionsWindow : Window
{
    private readonly string _fileName;
    private readonly IMinioClient _minioClient;
    private readonly string _workingBucketName;
    private readonly string _stableBucketName;
    private List<FileVersionDisplay> _versions = [];
    private FileVersionDisplay? _selectedVersion;

    public FileVersionsWindow(string fileName, IMinioClient minioClient, string workingBucketName, string stableBucketName)
    {
        InitializeComponent();
        _fileName = fileName;
        _minioClient = minioClient;
        _workingBucketName = workingBucketName;
        _stableBucketName = stableBucketName;
        
        FileNameText.Text = $"File: {fileName}";
        
        // Load versions when window opens
        Loaded += async (s, e) => await LoadVersions();
    }

    private async Task LoadVersions()
    {
        try
        {
            _versions.Clear();
            
            // Determine which bucket the file is in
            var targetBucket = await DetermineBucketForFile(_fileName);
            
            var listObjectsArgs = new ListObjectsArgs()
                .WithBucket(targetBucket)
                .WithPrefix(_fileName)
                .WithVersions(true);

            await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs))
            {
                if (item.Key == _fileName) // Exact match only
                {
                    var version = new FileVersionDisplay
                    {
                        VersionId = item.VersionId ?? "null",
                        LastModified = item.LastModifiedDateTime ?? DateTime.MinValue,
                        Size = (long)item.Size,
                        IsLatest = item.IsLatest,
                        ETag = item.ETag ?? string.Empty,
                        BucketName = targetBucket
                    };
                    
                    _versions.Add(version);
                }
            }
            
            // Sort by LastModified descending (newest first)
            _versions = [.. _versions.OrderByDescending(v => v.LastModified)];
            
            VersionsDataGrid.ItemsSource = _versions;
            
            if (_versions.Count == 0)
            {
                MessageBox.Show("No versions found for this file.", "No Versions", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load file versions: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task<string> DetermineBucketForFile(string fileName)
    {
        // Check working bucket first
        try
        {
            var workingBucketExistsArgs = new StatObjectArgs()
                .WithBucket(_workingBucketName)
                .WithObject(fileName);
            
            await _minioClient.StatObjectAsync(workingBucketExistsArgs);
            return _workingBucketName;
        }
        catch
        {
            // File not in working bucket, assume it's in stable bucket
            return _stableBucketName;
        }
    }

    private void VersionsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selectedVersion = VersionsDataGrid.SelectedItem as FileVersionDisplay;
        DownloadVersionButton.IsEnabled = _selectedVersion != null;
    }

    private async void DownloadVersionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedVersion == null)
        {
            MessageBox.Show("Please select a version to download.", "No Version Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            DownloadVersionButton.IsEnabled = false;

            // Show save file dialog
            var saveFileDialog = new SaveFileDialog
            {
                FileName = $"{Path.GetFileNameWithoutExtension(_fileName)}_v{_selectedVersion.VersionId}{Path.GetExtension(_fileName)}",
                Title = "Save File Version As",
                Filter = "All Files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                await DownloadFileVersion(_selectedVersion, saveFileDialog.FileName);
                MessageBox.Show($"Version downloaded successfully to:\n{saveFileDialog.FileName}", "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to download version: {ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DownloadVersionButton.IsEnabled = true;
        }
    }

    private async Task DownloadFileVersion(FileVersionDisplay version, string localFilePath)
    {
        var getObjectArgs = new GetObjectArgs()
            .WithBucket(version.BucketName)
            .WithObject(_fileName)
            .WithVersionId(version.VersionId)
            .WithCallbackStream(stream =>
            {
                using var fileStream = File.Create(localFilePath);
                stream.CopyTo(fileStream);
            });

        await _minioClient.GetObjectAsync(getObjectArgs);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public class FileVersionDisplay : FileVersion
{
    public string BucketName { get; set; } = string.Empty;
    
    public string SizeFormatted => FormatFileSize(Size);
    
    private static string FormatFileSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        var order = 0;
        var size = (double)bytes;
        
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        
        return $"{size:0.##} {sizes[order]}";
    }
}