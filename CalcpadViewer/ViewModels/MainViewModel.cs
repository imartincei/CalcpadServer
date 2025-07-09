using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using CalcpadViewer.Models;
using CalcpadViewer.Services;

namespace CalcpadViewer.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private IUserService? _userService;
    
    // Observable collections for data binding
    public ObservableCollection<User> Users { get; } = [];
    public ObservableCollection<PreDefinedTag> Tags { get; } = [];
    public ObservableCollection<BlobMetadata> Files { get; } = [];
    public ObservableCollection<BlobMetadata> AllFiles { get; } = [];
    
    // Selected items
    private User? _selectedUser;
    public User? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (_selectedUser != value)
            {
                _selectedUser = value;
                OnPropertyChanged();
                OnUserSelectionChanged?.Invoke(_selectedUser);
            }
        }
    }
    
    public Action<User?>? OnUserSelectionChanged { get; set; }
    
    private PreDefinedTag? _selectedTag;
    public PreDefinedTag? SelectedTag
    {
        get => _selectedTag;
        set
        {
            if (_selectedTag != value)
            {
                _selectedTag = value;
                OnPropertyChanged();
                OnTagSelectionChanged?.Invoke(_selectedTag);
            }
        }
    }
    
    public Action<PreDefinedTag?>? OnTagSelectionChanged { get; set; }
    
    // Multi-tag selection for filtering (now the primary method)
    public ObservableCollection<PreDefinedTag> SelectedTagFilters { get; } = [];
    
    private string _currentCategoryFilter = "All";
    public string CurrentCategoryFilter
    {
        get => _currentCategoryFilter;
        set
        {
            if (_currentCategoryFilter != value)
            {
                _currentCategoryFilter = value;
                OnPropertyChanged();
                FilterFilesByCategory();
            }
        }
    }
    
    private string _selectedFile = string.Empty;
    public string SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (_selectedFile != value)
            {
                _selectedFile = value;
                OnPropertyChanged();
            }
        }
    }
    
    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }
    
    private bool _isLoadingUsers;
    public bool IsLoadingUsers
    {
        get => _isLoadingUsers;
        set
        {
            if (_isLoadingUsers != value)
            {
                _isLoadingUsers = value;
                OnPropertyChanged();
            }
        }
    }
    
    private bool _isLoadingTags;
    public bool IsLoadingTags
    {
        get => _isLoadingTags;
        set
        {
            if (_isLoadingTags != value)
            {
                _isLoadingTags = value;
                OnPropertyChanged();
            }
        }
    }
    
    public MainViewModel(IUserService? userService = null)
    {
        _userService = userService;
        
        // Subscribe to collection changes for multi-tag filtering
        SelectedTagFilters.CollectionChanged += (s, e) => FilterFilesByTag();
    }
    
    public void SetUserService(IUserService userService)
    {
        _userService = userService;
    }
    
    public async Task LoadUsersAsync()
    {
        System.Diagnostics.Debug.WriteLine("LoadUsersAsync called");
        if (_userService == null)
        {
            System.Diagnostics.Debug.WriteLine("_userService is null, cannot load users");
            return;
        }
        if (IsLoadingUsers)
        {
            System.Diagnostics.Debug.WriteLine("Already loading users, skipping");
            return;
        }
        
        IsLoadingUsers = true;
        try
        {
            StatusText = "Loading users...";
            System.Diagnostics.Debug.WriteLine("Calling _userService.GetAllUsersAsync()");
            var users = await _userService.GetAllUsersAsync();
            System.Diagnostics.Debug.WriteLine($"Retrieved {users?.Count ?? 0} users");
            
            // Clear and repopulate the collection
            Users.Clear();
            if (users == null || users.Count == 0)
            {
                StatusText = "No users found";
                return;
            }
            foreach (var user in users)
            {
                Users.Add(user);
            }
            
            StatusText = $"Loaded {Users.Count} users";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load users: {ex.Message}";
        }
        finally
        {
            IsLoadingUsers = false;
        }
    }
    
    public async Task LoadTagsAsync()
    {
        System.Diagnostics.Debug.WriteLine("LoadTagsAsync called");
        if (_userService == null)
        {
            System.Diagnostics.Debug.WriteLine("_userService is null, cannot load tags");
            return;
        }
        if (IsLoadingTags)
        {
            System.Diagnostics.Debug.WriteLine("Already loading tags, skipping");
            return;
        }
        
        IsLoadingTags = true;
        try
        {
            StatusText = "Loading tags...";
            System.Diagnostics.Debug.WriteLine("Calling _userService.GetAllTagsAsync()");
            var tags = await _userService.GetAllTagsAsync();
            System.Diagnostics.Debug.WriteLine($"Retrieved {tags?.Count ?? 0} tags");
            
            // Clear and repopulate the collection
            Tags.Clear();
            if (tags == null || tags.Count == 0)
            {
                StatusText = "No tags found";
                return;
            }
            for (int i = 0; i < tags.Count; i++)
            {
                PreDefinedTag? tag = tags[i];
                Tags.Add(tag);
            }
            
            StatusText = $"Loaded {Tags.Count} tags";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to load tags: {ex.Message}";
        }
        finally
        {
            IsLoadingTags = false;
        }
    }
    
    public void FilterFilesByTag()
    {
        ApplyAllFilters();
    }
    
    public void FilterFilesByCategory()
    {
        ApplyAllFilters();
    }
    
    private void ApplyAllFilters()
    {
        Files.Clear();
        
        foreach (var file in AllFiles)
        {
            // Apply tag filtering
            bool passesTagFilter = true;
            if (SelectedTagFilters.Count > 0)
            {
                if (file?.Tags != null)
                {
                    // Check if any selected tag matches any file tag value
                    passesTagFilter = SelectedTagFilters.Any(selectedTag =>
                        file.Tags.Values.Any(tagValue =>
                            !string.IsNullOrEmpty(tagValue) &&
                            tagValue.Equals(selectedTag.Name, StringComparison.OrdinalIgnoreCase)));
                }
                else
                {
                    passesTagFilter = false;
                }
            }
            
            // Apply category filtering
            bool passesCategoryFilter = true;
            if (CurrentCategoryFilter != "All")
            {
                // Check if file matches the current category filter
                if (file?.Metadata != null)
                {
                    var category = file.Metadata.TryGetValue("file-category", out string? value) ? value : "Unknown";
                    passesCategoryFilter = category.Equals(CurrentCategoryFilter, StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    passesCategoryFilter = false;
                }
            }
            
            // File must pass both filters to be included
            if (passesTagFilter && passesCategoryFilter)
            {
                Files.Add(file);
            }
        }
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}