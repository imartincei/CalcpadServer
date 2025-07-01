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
    private string _bucketName = "calcpad-storage";
    private List<BlobMetadata> _files = new();
    private IUserService? _userService;
    private List<User> _users = new();
    private User? _selectedUser;

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
            UploadButton.IsEnabled = true;
            DownloadButton.IsEnabled = true;
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
                    // Process all metadata, including system metadata
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
                    else
                    {
                        // Add system metadata (non user-defined) to custom metadata for visibility
                        customMetadata[$"[System] {kvp.Key}"] = kvp.Value;
                    }
                }
            }

            var tags = await GetFileTags(fileName);

            return new BlobMetadata
            {
                FileName = fileName,
                Size = objectStat.Size,
                LastModified = objectStat.LastModified,
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

        // Structured Metadata - Always show section, display "None" if empty
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

        StructuredMetadataGroup.Visibility = Visibility.Visible;
        if (structuredItems.Any())
        {
            StructuredMetadataItems.ItemsSource = structuredItems;
        }
        else
        {
            StructuredMetadataItems.ItemsSource = new List<KeyValueDisplay> 
            { 
                new KeyValueDisplay { Key = "Status", Value = "No structured metadata found" } 
            };
        }

        // Custom Metadata - Always show section, display "None" if empty
        CustomMetadataGroup.Visibility = Visibility.Visible;
        if (metadata.CustomMetadata.Any())
        {
            var customItems = metadata.CustomMetadata.Select(kvp => new KeyValueDisplay { Key = kvp.Key, Value = kvp.Value }).ToList();
            CustomMetadataItems.ItemsSource = customItems;
        }
        else
        {
            CustomMetadataItems.ItemsSource = new List<KeyValueDisplay> 
            { 
                new KeyValueDisplay { Key = "Status", Value = "No custom metadata found" } 
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
        StructuredMetadataGroup.Visibility = Visibility.Visible;
        CustomMetadataGroup.Visibility = Visibility.Visible;
        TagsGroup.Visibility = Visibility.Visible;
        
        // Clear the content of metadata sections
        StructuredMetadataItems.ItemsSource = null;
        CustomMetadataItems.ItemsSource = null;
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
            await UploadFile(uploadDialog.SelectedFilePath!, uploadDialog.CustomMetadata, uploadDialog.Tags, uploadDialog.StructuredMetadata);
        }
    }

    private async Task UploadFile(string filePath, Dictionary<string, string> customMetadata, Dictionary<string, string> tags, StructuredMetadataRequest structuredMetadata)
    {
        if (_minioClient == null) return;

        try
        {
            StatusText.Text = "Uploading file...";
            UploadButton.IsEnabled = false;

            var fileName = Path.GetFileName(filePath);
            
            using var fileStream = File.OpenRead(filePath);
            var putObjectArgs = new PutObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(fileName)
                .WithStreamData(fileStream)
                .WithObjectSize(fileStream.Length)
                .WithContentType(GetContentType(fileName));

            // Add custom metadata with x-amz-meta- prefix
            var headers = new Dictionary<string, string>();
            
            if (customMetadata.Any())
            {
                foreach (var kvp in customMetadata)
                {
                    headers[$"x-amz-meta-{kvp.Key.ToLower()}"] = kvp.Value;
                }
            }

            // Add structured metadata
            if (!string.IsNullOrEmpty(structuredMetadata.OriginalFileName))
                headers["x-amz-meta-original-filename"] = structuredMetadata.OriginalFileName;
            if (structuredMetadata.DateCreated.HasValue)
                headers["x-amz-meta-date-created"] = structuredMetadata.DateCreated.Value.ToString("O");
            if (structuredMetadata.DateUpdated.HasValue)
                headers["x-amz-meta-date-updated"] = structuredMetadata.DateUpdated.Value.ToString("O");
            if (!string.IsNullOrEmpty(structuredMetadata.CreatedBy))
                headers["x-amz-meta-created-by"] = structuredMetadata.CreatedBy;
            if (!string.IsNullOrEmpty(structuredMetadata.UpdatedBy))
                headers["x-amz-meta-updated-by"] = structuredMetadata.UpdatedBy;
            if (structuredMetadata.DateReviewed.HasValue)
                headers["x-amz-meta-date-reviewed"] = structuredMetadata.DateReviewed.Value.ToString("O");
            if (!string.IsNullOrEmpty(structuredMetadata.ReviewedBy))
                headers["x-amz-meta-reviewed-by"] = structuredMetadata.ReviewedBy;
            if (!string.IsNullOrEmpty(structuredMetadata.TestedBy))
                headers["x-amz-meta-tested-by"] = structuredMetadata.TestedBy;
            if (structuredMetadata.DateTested.HasValue)
                headers["x-amz-meta-date-tested"] = structuredMetadata.DateTested.Value.ToString("O");

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
                    var tagging = new Tagging();
                    
                    // Initialize Tags dictionary if it's null
                    if (tagging.Tags == null)
                    {
                        tagging.Tags = new Dictionary<string, string>();
                    }
                    
                    foreach (var kvp in tags)
                    {
                        tagging.Tags[kvp.Key] = kvp.Value;
                    }

                    var setObjectTagsArgs = new SetObjectTagsArgs()
                        .WithBucket(_bucketName)
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

        try
        {
            StatusText.Text = "Downloading file...";
            DownloadButton.IsEnabled = false;

            // Show save file dialog
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = selectedFileName,
                Title = "Save File As",
                Filter = "All Files (*.*)|*.*"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                await DownloadFile(selectedFileName, saveFileDialog.FileName);
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

    private async Task DownloadFile(string fileName, string localFilePath)
    {
        var getObjectArgs = new GetObjectArgs()
            .WithBucket(_bucketName)
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

        // Confirm deletion
        var result = MessageBox.Show(
            $"Are you sure you want to delete '{selectedFileName}'?\n\nThis action cannot be undone.",
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

            await DeleteFile(selectedFileName);
            
            MessageBox.Show($"File '{selectedFileName}' deleted successfully.", "Delete Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private async Task DeleteFile(string fileName)
    {
        var removeObjectArgs = new RemoveObjectArgs()
            .WithBucket(_bucketName)
            .WithObject(fileName);

        await _minioClient.RemoveObjectAsync(removeObjectArgs);
    }
}