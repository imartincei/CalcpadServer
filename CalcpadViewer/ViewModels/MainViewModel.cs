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
    public ObservableCollection<User> Users { get; } = new();
    public ObservableCollection<PreDefinedTag> Tags { get; } = new();
    public ObservableCollection<BlobMetadata> Files { get; } = new();
    public ObservableCollection<BlobMetadata> AllFiles { get; } = new();
    
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
    
    private PreDefinedTag? _currentTagFilter;
    public PreDefinedTag? CurrentTagFilter
    {
        get => _currentTagFilter;
        set
        {
            if (_currentTagFilter != value)
            {
                _currentTagFilter = value;
                OnPropertyChanged();
                // Filter files when tag filter changes
                FilterFilesByTag();
            }
        }
    }
    
    // Multi-tag selection for filtering
    public ObservableCollection<PreDefinedTag> SelectedTagFilters { get; } = new();
    
    private bool _isMultiTagFilterEnabled = false;
    public bool IsMultiTagFilterEnabled
    {
        get => _isMultiTagFilterEnabled;
        set
        {
            if (_isMultiTagFilterEnabled != value)
            {
                _isMultiTagFilterEnabled = value;
                OnPropertyChanged();
                // Clear single selection when switching to multi-select
                if (value)
                {
                    CurrentTagFilter = null;
                }
                else
                {
                    SelectedTagFilters.Clear();
                }
                FilterFilesByTag();
            }
        }
    }
    
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
            foreach (var tag in tags)
            {
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
    
    private void FilterFilesByTag()
    {
        Files.Clear();
        
        if (IsMultiTagFilterEnabled)
        {
            // Multi-tag filtering
            if (SelectedTagFilters.Count == 0)
            {
                // No tags selected, show all files
                foreach (var file in AllFiles)
                {
                    Files.Add(file);
                }
            }
            else
            {
                // Show files that have ANY of the selected tags
                foreach (var file in AllFiles)
                {
                    if (file?.Tags != null && SelectedTagFilters.Any(selectedTag =>
                        file.Tags.Values.Any(tagValue =>
                            !string.IsNullOrEmpty(tagValue) &&
                            tagValue.Equals(selectedTag.Name, StringComparison.OrdinalIgnoreCase))))
                    {
                        Files.Add(file);
                    }
                }
            }
        }
        else
        {
            // Single tag filtering (original logic)
            if (CurrentTagFilter == null)
            {
                // Show all files
                foreach (var file in AllFiles)
                {
                    Files.Add(file);
                }
            }
            else
            {
                // Filter by single tag
                foreach (var file in AllFiles)
                {
                    if (file?.Tags != null && file.Tags.Values.Any(tagValue =>
                        !string.IsNullOrEmpty(tagValue) &&
                        tagValue.Equals(CurrentTagFilter.Name, StringComparison.OrdinalIgnoreCase)))
                    {
                        Files.Add(file);
                    }
                }
            }
        }
        
        // Also apply category filter
        FilterFilesByCategory();
    }
    
    private void FilterFilesByCategory()
    {
        // Implement category filtering logic
        FilterFilesByTag(); // Also apply tag filter
    }
    
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}