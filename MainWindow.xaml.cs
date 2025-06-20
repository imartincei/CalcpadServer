using System.Windows;
using System.Windows.Controls;
using Minio;
using Minio.DataModel.Args;
using CalcpadViewer.Models;
using System.Globalization;

namespace CalcpadViewer;

public partial class MainWindow : Window
{
    private IMinioClient? _minioClient;
    private string _bucketName = "calcpad-storage";
    private List<BlobMetadata> _files = new();

    public MainWindow()
    {
        InitializeComponent();
        SecretKeyBox.Password = "calcpad-password-123"; // Default password
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Connecting...";
            ConnectButton.IsEnabled = false;

            var endpoint = EndpointTextBox.Text.Trim();
            var accessKey = AccessKeyTextBox.Text.Trim();
            var secretKey = SecretKeyBox.Password.Trim();
            var useSSL = UseSSLCheckBox.IsChecked == true;
            _bucketName = BucketTextBox.Text.Trim();

            if (string.IsNullOrEmpty(endpoint) || string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            {
                MessageBox.Show("Please fill in all connection fields.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _minioClient = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(useSSL)
                .Build();

            // Test connection by checking if bucket exists
            var bucketExistsArgs = new BucketExistsArgs().WithBucket(_bucketName);
            var bucketExists = await _minioClient.BucketExistsAsync(bucketExistsArgs);

            if (!bucketExists)
            {
                MessageBox.Show($"Bucket '{_bucketName}' does not exist.", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = "Connected successfully";
            RefreshButton.IsEnabled = true;
            await LoadFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Connection failed: {ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Connection failed";
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadFiles();
    }

    private async Task LoadFiles()
    {
        if (_minioClient == null) return;

        try
        {
            StatusText.Text = "Loading files...";
            RefreshButton.IsEnabled = false;
            FilesListBox.Items.Clear();
            _files.Clear();

            var listObjectsArgs = new ListObjectsArgs()
                .WithBucket(_bucketName)
                .WithRecursive(true);

            await foreach (var item in _minioClient.ListObjectsEnumAsync(listObjectsArgs))
            {
                var metadata = await GetFileMetadata(item.Key);
                _files.Add(metadata);
                FilesListBox.Items.Add(item.Key);
            }

            StatusText.Text = $"Loaded {_files.Count} files";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load files: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to load files";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async Task<BlobMetadata> GetFileMetadata(string fileName)
    {
        if (_minioClient == null) 
            return new BlobMetadata { FileName = fileName };

        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName);

            var objectStat = await _minioClient.StatObjectAsync(statObjectArgs);
            
            var customMetadata = new Dictionary<string, string>();
            var structuredMetadata = new StructuredMetadata();
            
            if (objectStat.MetaData != null)
            {
                foreach (var kvp in objectStat.MetaData)
                {
                    if (kvp.Key.StartsWith("x-amz-meta-", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = kvp.Key.Substring("x-amz-meta-".Length);
                        
                        // Parse structured metadata fields
                        switch (key.ToLower())
                        {
                            case "original-filename":
                                structuredMetadata.OriginalFileName = kvp.Value;
                                break;
                            case "date-created":
                                if (DateTime.TryParse(kvp.Value, out var dateCreated))
                                    structuredMetadata.DateCreated = dateCreated;
                                break;
                            case "date-updated":
                                if (DateTime.TryParse(kvp.Value, out var dateUpdated))
                                    structuredMetadata.DateUpdated = dateUpdated;
                                break;
                            case "version":
                                structuredMetadata.Version = kvp.Value;
                                break;
                            case "created-by":
                                structuredMetadata.CreatedBy = kvp.Value;
                                break;
                            case "updated-by":
                                structuredMetadata.UpdatedBy = kvp.Value;
                                break;
                            case "date-reviewed":
                                if (DateTime.TryParse(kvp.Value, out var dateReviewed))
                                    structuredMetadata.DateReviewed = dateReviewed;
                                break;
                            case "reviewed-by":
                                structuredMetadata.ReviewedBy = kvp.Value;
                                break;
                            case "tested-by":
                                structuredMetadata.TestedBy = kvp.Value;
                                break;
                            case "date-tested":
                                if (DateTime.TryParse(kvp.Value, out var dateTested))
                                    structuredMetadata.DateTested = dateTested;
                                break;
                            default:
                                // Add to custom metadata if not a structured field
                                customMetadata[key] = kvp.Value;
                                break;
                        }
                    }
                }
            }

            var tags = await GetFileTags(fileName);

            return new BlobMetadata
            {
                FileName = fileName,
                Size = objectStat.Size,
                LastModified = objectStat.LastModifiedDateTime ?? DateTime.MinValue,
                ContentType = objectStat.ContentType ?? "application/octet-stream",
                ETag = objectStat.ETag ?? string.Empty,
                CustomMetadata = customMetadata,
                Tags = tags,
                Structured = structuredMetadata
            };
        }
        catch (Exception ex)
        {
            // Return basic metadata if detailed fetch fails
            return new BlobMetadata 
            { 
                FileName = fileName,
                CustomMetadata = new Dictionary<string, string> { { "error", ex.Message } }
            };
        }
    }

    private async Task<Dictionary<string, string>> GetFileTags(string fileName)
    {
        if (_minioClient == null) return new Dictionary<string, string>();

        try
        {
            var getObjectTagsArgs = new GetObjectTagsArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName);

            var tagging = await _minioClient.GetObjectTagsAsync(getObjectTagsArgs);
            
            return tagging.Tags.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void FilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FilesListBox.SelectedItem is string selectedFileName)
        {
            var fileMetadata = _files.FirstOrDefault(f => f.FileName == selectedFileName);
            if (fileMetadata != null)
            {
                DisplayFileMetadata(fileMetadata);
            }
        }
        else
        {
            ClearMetadataDisplay();
        }
    }

    private void DisplayFileMetadata(BlobMetadata metadata)
    {
        NoSelectionText.Visibility = Visibility.Collapsed;
        
        // File Info
        FileInfoGroup.Visibility = Visibility.Visible;
        FileNameText.Text = metadata.FileName;
        FileSizeText.Text = FormatFileSize(metadata.Size);
        LastModifiedText.Text = metadata.LastModified.ToString("yyyy-MM-dd HH:mm:ss");
        ContentTypeText.Text = metadata.ContentType;
        ETagText.Text = metadata.ETag;

        // Structured Metadata
        var structuredItems = new List<KeyValueDisplay>();
        if (!string.IsNullOrEmpty(metadata.Structured.OriginalFileName))
            structuredItems.Add(new KeyValueDisplay { Key = "Original Filename", Value = metadata.Structured.OriginalFileName });
        if (metadata.Structured.DateCreated.HasValue)
            structuredItems.Add(new KeyValueDisplay { Key = "Date Created", Value = metadata.Structured.DateCreated.Value.ToString("yyyy-MM-dd HH:mm:ss") });
        if (metadata.Structured.DateUpdated.HasValue)
            structuredItems.Add(new KeyValueDisplay { Key = "Date Updated", Value = metadata.Structured.DateUpdated.Value.ToString("yyyy-MM-dd HH:mm:ss") });
        if (!string.IsNullOrEmpty(metadata.Structured.Version))
            structuredItems.Add(new KeyValueDisplay { Key = "Version", Value = metadata.Structured.Version });
        if (!string.IsNullOrEmpty(metadata.Structured.CreatedBy))
            structuredItems.Add(new KeyValueDisplay { Key = "Created By", Value = metadata.Structured.CreatedBy });
        if (!string.IsNullOrEmpty(metadata.Structured.UpdatedBy))
            structuredItems.Add(new KeyValueDisplay { Key = "Updated By", Value = metadata.Structured.UpdatedBy });
        if (metadata.Structured.DateReviewed.HasValue)
            structuredItems.Add(new KeyValueDisplay { Key = "Date Reviewed", Value = metadata.Structured.DateReviewed.Value.ToString("yyyy-MM-dd HH:mm:ss") });
        if (!string.IsNullOrEmpty(metadata.Structured.ReviewedBy))
            structuredItems.Add(new KeyValueDisplay { Key = "Reviewed By", Value = metadata.Structured.ReviewedBy });
        if (!string.IsNullOrEmpty(metadata.Structured.TestedBy))
            structuredItems.Add(new KeyValueDisplay { Key = "Tested By", Value = metadata.Structured.TestedBy });
        if (metadata.Structured.DateTested.HasValue)
            structuredItems.Add(new KeyValueDisplay { Key = "Date Tested", Value = metadata.Structured.DateTested.Value.ToString("yyyy-MM-dd HH:mm:ss") });

        if (structuredItems.Any())
        {
            StructuredMetadataGroup.Visibility = Visibility.Visible;
            StructuredMetadataItems.ItemsSource = structuredItems;
        }
        else
        {
            StructuredMetadataGroup.Visibility = Visibility.Collapsed;
        }

        // Custom Metadata
        if (metadata.CustomMetadata.Any())
        {
            CustomMetadataGroup.Visibility = Visibility.Visible;
            var customItems = metadata.CustomMetadata.Select(kvp => new KeyValueDisplay { Key = kvp.Key, Value = kvp.Value }).ToList();
            CustomMetadataItems.ItemsSource = customItems;
        }
        else
        {
            CustomMetadataGroup.Visibility = Visibility.Collapsed;
        }

        // Tags
        if (metadata.Tags.Any())
        {
            TagsGroup.Visibility = Visibility.Visible;
            var tagItems = metadata.Tags.Select(kvp => new KeyValueDisplay { Key = kvp.Key, Value = kvp.Value }).ToList();
            TagsItems.ItemsSource = tagItems;
        }
        else
        {
            TagsGroup.Visibility = Visibility.Collapsed;
        }
    }

    private void ClearMetadataDisplay()
    {
        NoSelectionText.Visibility = Visibility.Visible;
        FileInfoGroup.Visibility = Visibility.Collapsed;
        StructuredMetadataGroup.Visibility = Visibility.Collapsed;
        CustomMetadataGroup.Visibility = Visibility.Collapsed;
        TagsGroup.Visibility = Visibility.Collapsed;
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes == 0) return "0 B";
        
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
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