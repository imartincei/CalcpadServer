using System.IO;
using System.Windows;
using System.Windows.Controls;
using Minio;
using Minio.DataModel.Args;
using Minio.DataModel.Tags;
using CalcpadViewer.Models;
using CalcpadViewer.Services;
using System.Globalization;

namespace CalcpadViewer;

public partial class MainWindow : Window
{
    private IMinioClient? _minioClient;
    private string _workingBucketName = "calcpad-storage-working";
    private string _stableBucketName = "calcpad-storage-stable";
    private List<BlobMetadata> _files = new();
    private List<BlobMetadata> _allFiles = new(); // Store all files for filtering
    private IUserService? _userService;
    private List<User> _users = new();
    private User? _selectedUser;
    private User? _currentUser;
    private string _currentCategoryFilter = "All";

    public MainWindow()
    {
        InitializeComponent();
        SecretKeyBox.Password = "calcpad-password-123"; // Default password
        AdminPasswordBox.Password = "admin123"; // Default admin password
        InitializeUserRoleComboBox();
    }

    private void InitializeUserRoleComboBox()
    {
        UserRoleComboBox.Items.Add(new ComboBoxItem { Content = "Viewer", Tag = UserRole.Viewer });
        UserRoleComboBox.Items.Add(new ComboBoxItem { Content = "Contributor", Tag = UserRole.Contributor });
        UserRoleComboBox.Items.Add(new ComboBoxItem { Content = "Admin", Tag = UserRole.Admin });
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
            
            // Use fixed bucket names
            _workingBucketName = "calcpad-storage-working";
            _stableBucketName = "calcpad-storage-stable";

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

            // Test connection by checking if buckets exist
            var workingBucketExistsArgs = new BucketExistsArgs().WithBucket(_workingBucketName);
            var stableBucketExistsArgs = new BucketExistsArgs().WithBucket(_stableBucketName);
            
            var workingBucketExists = await _minioClient.BucketExistsAsync(workingBucketExistsArgs);
            var stableBucketExists = await _minioClient.BucketExistsAsync(stableBucketExistsArgs);

            if (!workingBucketExists || !stableBucketExists)
            {
                var missingBuckets = new List<string>();
                if (!workingBucketExists) missingBuckets.Add(_workingBucketName);
                if (!stableBucketExists) missingBuckets.Add(_stableBucketName);
                
                MessageBox.Show($"Missing buckets: {string.Join(", ", missingBuckets)}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = "Connected successfully";
            RefreshButton.IsEnabled = true;
            UploadButton.IsEnabled = true;
            DownloadButton.IsEnabled = true;
            ViewVersionsButton.IsEnabled = true;
            DeleteButton.IsEnabled = true;
            
            // Initialize user service and try admin login
            var apiBaseUrl = $"http{(useSSL ? "s" : "")}://{endpoint.Replace(":9000", ":5159")}"; // API is on port 5159
            _userService = new UserService(apiBaseUrl);
            
            // Attempt admin login if credentials provided
            var adminUsername = AdminUsernameTextBox.Text.Trim();
            var adminPassword = AdminPasswordBox.Password.Trim();
            
            if (!string.IsNullOrEmpty(adminUsername) && !string.IsNullOrEmpty(adminPassword))
            {
                try
                {
                    StatusText.Text = "Logging in admin user...";
                    var authResponse = await _userService.LoginAsync(adminUsername, adminPassword);
                    _currentUser = authResponse.User;
                    StatusText.Text = $"Admin logged in: {authResponse.User.Username}";
                    AdminTab.IsEnabled = true;
                }
                catch (Exception authEx)
                {
                    MessageBox.Show($"Admin login failed: {authEx.Message}", "Login Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StatusText.Text = "Connected (no admin access)";
                }
            }
            else
            {
                StatusText.Text = "Connected (no admin credentials)";
            }
            
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

    private void CategoryTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl tabControl && tabControl.SelectedItem is TabItem selectedTab)
        {
            _currentCategoryFilter = selectedTab.Header.ToString() ?? "All";
            FilterFilesByCategory();
        }
    }

    private async Task LoadFiles()
    {
        if (_minioClient == null) return;

        try
        {
            StatusText.Text = "Loading files...";
            RefreshButton.IsEnabled = false;
            _allFiles.Clear();

            // Load files from working bucket
            var workingListObjectsArgs = new ListObjectsArgs()
                .WithBucket(_workingBucketName)
                .WithRecursive(true);

            await foreach (var item in _minioClient.ListObjectsEnumAsync(workingListObjectsArgs))
            {
                var metadata = await GetFileMetadata(item.Key, _workingBucketName);
                _allFiles.Add(metadata);
            }

            // Load files from stable bucket
            var stableListObjectsArgs = new ListObjectsArgs()
                .WithBucket(_stableBucketName)
                .WithRecursive(true);

            await foreach (var item in _minioClient.ListObjectsEnumAsync(stableListObjectsArgs))
            {
                var metadata = await GetFileMetadata(item.Key, _stableBucketName);
                _allFiles.Add(metadata);
            }

            StatusText.Text = $"Loaded {_allFiles.Count} files";
            FilterFilesByCategory();
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

    private void FilterFilesByCategory()
    {
        FilesListBox.Items.Clear();
        _files.Clear();

        if (_currentCategoryFilter == "All")
        {
            _files.AddRange(_allFiles);
        }
        else
        {
            _files.AddRange(_allFiles.Where(f => 
            {
                var category = f.Metadata.ContainsKey("file-category") ? f.Metadata["file-category"] : "Unknown";
                return category.Equals(_currentCategoryFilter, StringComparison.OrdinalIgnoreCase);
            }));
        }

        // Add files to display without category brackets
        foreach (var file in _files)
        {
            FilesListBox.Items.Add(file.FileName);
        }

        StatusText.Text = $"Showing {_files.Count} files ({_currentCategoryFilter})";
    }

    private async Task<BlobMetadata> GetFileMetadata(string fileName, string bucketName)
    {
        if (_minioClient == null) 
            return new BlobMetadata { FileName = fileName };

        try
        {
            var statObjectArgs = new StatObjectArgs()
                .WithBucket(bucketName)
                .WithObject(fileName);

            var objectStat = await _minioClient.StatObjectAsync(statObjectArgs);
            
            var Metadata = new Dictionary<string, string>();
            
            if (objectStat.MetaData != null)
            {
                foreach (var kvp in objectStat.MetaData)
                {
                    Metadata[kvp.Key] = kvp.Value;
                }
            }

            var tags = await GetFileTags(fileName, bucketName);

            return new BlobMetadata
            {
                FileName = fileName,
                Size = objectStat.Size,
                LastModified = objectStat.LastModified,
                ContentType = objectStat.ContentType ?? "application/octet-stream",
                ETag = objectStat.ETag ?? string.Empty,
                Tags = tags,
                Metadata = Metadata
            };
        }
        catch (Exception ex)
        {
            // Return basic metadata if detailed fetch fails
            return new BlobMetadata 
            { 
                FileName = fileName
            };
        }
    }

    private async Task<Dictionary<string, string>> GetFileTags(string fileName, string bucketName)
    {
        if (_minioClient == null) return new Dictionary<string, string>();

        try
        {
            var getObjectTagsArgs = new GetObjectTagsArgs()
                .WithBucket(bucketName)
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

    private (string fileName, string bucketName) ExtractFileInfoFromDisplayName(string fileName)
    {
        // Find the file metadata to determine the bucket
        var fileMetadata = _files.FirstOrDefault(f => f.FileName == fileName);
        if (fileMetadata != null)
        {
            var category = fileMetadata.Metadata.ContainsKey("file-category") ? fileMetadata.Metadata["file-category"] : "Unknown";
            var bucketName = category.ToLower() == "working" ? _workingBucketName : _stableBucketName;
            return (fileName, bucketName);
        }
        
        // Fallback - default to stable bucket
        return (fileName, _stableBucketName);
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

        // All Metadata - Combined structured and custom metadata
        var allMetadataItems = new List<KeyValueDisplay>();
        foreach (var kvp in metadata.Metadata)
        {
            allMetadataItems.Add(new KeyValueDisplay { Key = kvp.Key, Value = kvp.Value });
        }

        MetadataGroup.Visibility = Visibility.Visible;
        if (allMetadataItems.Any())
        {
            MetadataItems.ItemsSource = allMetadataItems;
        }
        else
        {
            MetadataItems.ItemsSource = new List<KeyValueDisplay> 
            { 
                new KeyValueDisplay { Key = "Status", Value = "No metadata found" } 
            };
        }

        // Tags - Always show section, display "None" if empty
        TagsGroup.Visibility = Visibility.Visible;
        if (metadata.Tags.Any())
        {
            var tagItems = metadata.Tags.Select(kvp => new KeyValueDisplay { Key = kvp.Key, Value = kvp.Value }).ToList();
            TagsItems.ItemsSource = tagItems;
        }
        else
        {
            TagsItems.ItemsSource = new List<KeyValueDisplay> 
            { 
                new KeyValueDisplay { Key = "Status", Value = "No tags found" } 
            };
        }
    }

    private void ClearMetadataDisplay()
    {
        NoSelectionText.Visibility = Visibility.Visible;
        FileInfoGroup.Visibility = Visibility.Collapsed;
        MetadataGroup.Visibility = Visibility.Visible;
        TagsGroup.Visibility = Visibility.Visible;
        
        // Clear the content of metadata sections
        MetadataItems.ItemsSource = null;
        TagsItems.ItemsSource = null;
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

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_minioClient == null)
        {
            MessageBox.Show("Please connect to MinIO first.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var uploadDialog = new UploadDialog();
        uploadDialog.Owner = this;

        if (uploadDialog.ShowDialog() == true)
        {
            await UploadFile(uploadDialog.SelectedFilePath!, uploadDialog.Filename!, uploadDialog.Tags, uploadDialog.Metadata);
        }
    }

    private async Task UploadFile(string filePath, string fileName, Dictionary<string, string> tags, MetadataRequest metadata)
    {
        if (_minioClient == null) return;

        try
        {
            StatusText.Text = "Uploading file...";
            UploadButton.IsEnabled = false;
            
            // Determine bucket based on FileCategory - only "Working" goes to working bucket, all others go to stable
            var targetBucket = metadata.FileCategory?.ToLower() == "working" ? _workingBucketName : _stableBucketName;
            
            using var fileStream = File.OpenRead(filePath);
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(targetBucket)
                .WithObject(fileName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(GetContentType(fileName));

            // Add structured metadata
            var headers = new Dictionary<string, string>();
            
            // Auto-assign current date and user for created/updated fields
            var currentDate = DateTime.UtcNow;
            var currentUserEmail = _currentUser?.Email ?? "unknown";
            
            headers["date-created"] = currentDate.ToString("O");
            headers["date-updated"] = currentDate.ToString("O");
            headers["created-by"] = currentUserEmail;
            headers["updated-by"] = currentUserEmail;
            if (!string.IsNullOrEmpty(metadata.FileCategory))
                headers["file-category"] = metadata.FileCategory;
            if (metadata.DateReviewed.HasValue)
                headers["date-reviewed"] = metadata.DateReviewed.Value.ToString("O");
            if (!string.IsNullOrEmpty(metadata.ReviewedBy))
                headers["reviewed-by"] = metadata.ReviewedBy;
            if (!string.IsNullOrEmpty(metadata.TestedBy))
                headers["tested-by"] = metadata.TestedBy;
            if (metadata.DateTested.HasValue)
                headers["date-tested"] = metadata.DateTested.Value.ToString("O");

            if (headers.Any())
            {
                putObjectArgs.WithHeaders(headers);
            }

            await _minioClient.PutObjectAsync(putObjectArgs);

            // Set tags if provided
            if (tags.Any())
            {
                try
                {
                    // Create Tagging with tags dictionary (false = bucket tags, true = object tags)
                    var tagging = new Tagging(tags, true);

                    var setObjectTagsArgs = new SetObjectTagsArgs()
                        .WithBucket(targetBucket)
                        .WithObject(fileName)
                        .WithTagging(tagging);

                    await _minioClient.SetObjectTagsAsync(setObjectTagsArgs);
                }
                catch (Exception tagEx)
                {
                    // If tagging fails, log but don't fail the upload
                    StatusText.Text = $"File uploaded but tagging failed: {tagEx.Message}";
                }
            }
            
            StatusText.Text = $"File '{fileName}' uploaded successfully";
            MessageBox.Show($"File '{fileName}' uploaded successfully!", "Upload Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            
            // Refresh the file list
            await LoadFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Upload failed: {ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Upload failed";
        }
        finally
        {
            UploadButton.IsEnabled = true;
        }
    }

    private string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".cpd" => "text/plain",
            ".cpdz" => "text/plain",
            ".txt" => "text/plain",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".csv" => "text/csv",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".json" => "application/json",
            _ => "application/octet-stream"
        };
    }

    // Admin tab event handlers
    private async void RefreshUsersButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadUsers();
    }

    private async void AddUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_userService == null)
        {
            MessageBox.Show("User service not initialized. Please connect first.", "Service Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var addUserDialog = new AddUserDialog();
        addUserDialog.Owner = this;

        if (addUserDialog.ShowDialog() == true && addUserDialog.UserRequest != null)
        {
            await CreateUser(addUserDialog.UserRequest);
        }
    }

    private void UsersDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UsersDataGrid.SelectedItem is User selectedUser)
        {
            _selectedUser = selectedUser;
            DisplayUserDetails(selectedUser);
        }
        else
        {
            _selectedUser = null;
            ClearUserDetailsDisplay();
        }
    }

    private async void UpdateUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null || _userService == null) return;

        var selectedRoleItem = UserRoleComboBox.SelectedItem as ComboBoxItem;
        if (selectedRoleItem == null) return;

        var updateRequest = new UpdateUserRequest
        {
            Role = (UserRole)selectedRoleItem.Tag,
            IsActive = UserActiveCheckBox.IsChecked == true
        };

        await UpdateUser(_selectedUser.Id, updateRequest);
    }

    private async void DeleteUserButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null || _userService == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete user '{_selectedUser.Username}'?", 
            "Confirm Delete", 
            MessageBoxButton.YesNo, 
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            await DeleteUser(_selectedUser.Id);
        }
    }

    // Admin helper methods
    private async Task LoadUsers()
    {
        if (_userService == null) return;

        try
        {
            StatusText.Text = "Loading users...";
            RefreshUsersButton.IsEnabled = false;
            
            _users = await _userService.GetAllUsersAsync();
            UsersDataGrid.ItemsSource = _users;
            
            StatusText.Text = $"Loaded {_users.Count} users";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load users: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to load users";
        }
        finally
        {
            RefreshUsersButton.IsEnabled = true;
        }
    }

    private async Task CreateUser(RegisterRequest request)
    {
        if (_userService == null) return;

        try
        {
            StatusText.Text = "Creating user...";
            AddUserButton.IsEnabled = false;

            var newUser = await _userService.CreateUserAsync(request);
            
            StatusText.Text = $"User '{newUser.Username}' created successfully";
            MessageBox.Show($"User '{newUser.Username}' created successfully!", "User Created", MessageBoxButton.OK, MessageBoxImage.Information);
            
            await LoadUsers();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to create user: {ex.Message}", "Create Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to create user";
        }
        finally
        {
            AddUserButton.IsEnabled = true;
        }
    }

    private async Task UpdateUser(string userId, UpdateUserRequest request)
    {
        if (_userService == null) return;

        try
        {
            StatusText.Text = "Updating user...";
            UpdateUserButton.IsEnabled = false;

            var updatedUser = await _userService.UpdateUserAsync(userId, request);
            
            StatusText.Text = $"User '{updatedUser.Username}' updated successfully";
            MessageBox.Show($"User '{updatedUser.Username}' updated successfully!", "User Updated", MessageBoxButton.OK, MessageBoxImage.Information);
            
            await LoadUsers();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to update user: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to update user";
        }
        finally
        {
            UpdateUserButton.IsEnabled = true;
        }
    }

    private async Task DeleteUser(string userId)
    {
        if (_userService == null) return;

        try
        {
            StatusText.Text = "Deleting user...";
            DeleteUserButton.IsEnabled = false;

            var success = await _userService.DeleteUserAsync(userId);
            
            if (success)
            {
                StatusText.Text = "User deleted successfully";
                MessageBox.Show("User deleted successfully!", "User Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadUsers();
                ClearUserDetailsDisplay();
            }
            else
            {
                MessageBox.Show("Failed to delete user.", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Failed to delete user";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete user: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to delete user";
        }
        finally
        {
            DeleteUserButton.IsEnabled = true;
        }
    }

    private void DisplayUserDetails(User user)
    {
        NoUserSelectionText.Visibility = Visibility.Collapsed;
        UserDetailsGroup.Visibility = Visibility.Visible;
        UserActionsGroup.Visibility = Visibility.Visible;

        UserIdText.Text = user.Id;
        UsernameText.Text = user.Username;
        UserEmailText.Text = user.Email;
        UserActiveCheckBox.IsChecked = user.IsActive;
        LastLoginText.Text = user.LastLoginAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Never";

        // Set role in combobox
        foreach (ComboBoxItem item in UserRoleComboBox.Items)
        {
            if ((UserRole)item.Tag == user.Role)
            {
                UserRoleComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void ClearUserDetailsDisplay()
    {
        NoUserSelectionText.Visibility = Visibility.Visible;
        UserDetailsGroup.Visibility = Visibility.Collapsed;
        UserActionsGroup.Visibility = Visibility.Collapsed;
        _selectedUser = null;
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesListBox.SelectedItem is not string selectedFileName)
        {
            MessageBox.Show("Please select a file to download.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (actualFileName, bucketName) = ExtractFileInfoFromDisplayName(selectedFileName);

        try
        {
            StatusText.Text = "Downloading file...";
            DownloadButton.IsEnabled = false;

            // Show save file dialog
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = actualFileName,
                Title = "Save File As",
                Filter = "All Files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                await DownloadFile(actualFileName, saveFileDialog.FileName, bucketName);
                MessageBox.Show($"File downloaded successfully to:\n{saveFileDialog.FileName}", "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = "Download completed";
            }
            else
            {
                StatusText.Text = "Download cancelled";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to download file: {ex.Message}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Download failed";
        }
        finally
        {
            DownloadButton.IsEnabled = true;
        }
    }

    private async Task DownloadFile(string fileName, string localFilePath, string bucketName)
    {
        var getObjectArgs = new GetObjectArgs()
            .WithBucket(bucketName)
            .WithObject(fileName)
            .WithCallbackStream(stream =>
            {
                using var fileStream = File.Create(localFilePath);
                stream.CopyTo(fileStream);
            });

        await _minioClient.GetObjectAsync(getObjectArgs);
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesListBox.SelectedItem is not string selectedFileName)
        {
            MessageBox.Show("Please select a file to delete.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (actualFileName, bucketName) = ExtractFileInfoFromDisplayName(selectedFileName);

        // Confirm deletion
        var result = MessageBox.Show(
            $"Are you sure you want to delete '{actualFileName}' from {bucketName}?\n\nThis action cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            StatusText.Text = "Deleting file...";
            DeleteButton.IsEnabled = false;

            await DeleteFile(actualFileName, bucketName);
            
            MessageBox.Show($"File '{actualFileName}' deleted successfully.", "Delete Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = "File deleted successfully";
            
            // Refresh the file list and clear metadata display
            await LoadFiles();
            ClearMetadataDisplay();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete file: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Delete failed";
        }
        finally
        {
            DeleteButton.IsEnabled = true;
        }
    }

    private async Task DeleteFile(string fileName, string bucketName)
    {
        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(bucketName)
            .WithObject(fileName);

        await _minioClient.RemoveObjectAsync(removeObjectArgs);
    }

    private async void ViewVersionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilesListBox.SelectedItem is not string selectedFileName)
        {
            MessageBox.Show("Please select a file to view versions.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            StatusText.Text = "Loading file versions...";
            ViewVersionsButton.IsEnabled = false;

            // Create a window to show versions
            var versionsWindow = new FileVersionsWindow(selectedFileName, _minioClient, _workingBucketName, _stableBucketName);
            versionsWindow.Owner = this;
            versionsWindow.ShowDialog();

            StatusText.Text = "Ready";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to load file versions: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "Failed to load versions";
        }
        finally
        {
            ViewVersionsButton.IsEnabled = true;
        }
    }
}