using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using CalcpadViewer.Models;
using CalcpadViewer.Services;

namespace CalcpadViewer;

public partial class UploadDialog : Window
{
    public string? SelectedFilePath { get; private set; }
    public string? Filename { get; private set; }
    public Dictionary<string, string> Tags { get; private set; } = new();
    public MetadataRequest Metadata { get; private set; } = new();
    
    private readonly IUserService? _userService;
    private List<SelectableTag> _allTags = new();
    private List<SelectableTag> _filteredTags = new();
    
    public UploadDialog(IUserService? userService = null)
    {
        InitializeComponent();
        _userService = userService;
        
        // Load tags when dialog opens
        Loaded += async (s, e) => await LoadAvailableTags();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "Select file to upload",
            Filter = "Supported Files|*.cpd;*.cpdz;*.txt;*.png;*.jpg;*.jpeg;*.csv;*.xlsx;*.json|All Files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            SelectedFilePath = openFileDialog.FileName;
            FilePathTextBox.Text = SelectedFilePath;
            
            // Auto-populate filename if not set
            if (string.IsNullOrEmpty(FilenameTextBox.Text))
            {
                FilenameTextBox.Text = Path.GetFileName(SelectedFilePath);
            }
            
            UploadButton.IsEnabled = true;
        }
    }

    private void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            MessageBox.Show("Please select a file to upload.", "No File Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!File.Exists(SelectedFilePath))
        {
            MessageBox.Show("The selected file does not exist.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Capture the filename from the textbox
        Filename = string.IsNullOrWhiteSpace(FilenameTextBox.Text) ? Path.GetFileName(SelectedFilePath) : FilenameTextBox.Text.Trim();

        // Parse structured metadata
        Metadata = new MetadataRequest
        {
            FileCategory = DetermineFileCategory(SelectedFilePath),
            ReviewedBy = string.IsNullOrWhiteSpace(ReviewedByTextBox.Text) ? null : ReviewedByTextBox.Text.Trim(),
            DateReviewed = DateReviewedPicker.SelectedDate,
            TestedBy = string.IsNullOrWhiteSpace(TestedByTextBox.Text) ? null : TestedByTextBox.Text.Trim(),
            DateTested = DateTestedPicker.SelectedDate
        };


        // Parse selected tags
        Tags.Clear();
        var selectedTags = _allTags.Where(t => t.IsSelected).ToList();
        var tagIndex = 1;
        foreach (var tag in selectedTags)
        {
            Tags[$"tag{tagIndex}"] = tag.Name;
            tagIndex++;
        }

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private string DetermineFileCategory(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        
        return extension switch
        {
            // Photo files
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tiff" or ".webp" => "Photo",
            
            // Data files
            ".xlsx" or ".csv" or ".txt" => "Data",
            
            // Working files (Calcpad files)
            ".cpd" or ".cpdz" => "Working",
            
            // Default for unknown extensions
            _ => "Data"
        };
    }

    private async Task LoadAvailableTags()
    {
        if (_userService == null)
        {
            // If no user service, show message and disable tag selection
            TagSearchBox.IsEnabled = false;
            AvailableTagsListBox.IsEnabled = false;
            SelectedTagsText.Text = "Tags unavailable (not connected)";
            return;
        }

        try
        {
            var prefinedTags = await _userService.GetAllTagsAsync();
            _allTags = prefinedTags.Select(t => new SelectableTag
            {
                Id = t.Id,
                Name = t.Name,
                IsSelected = false
            }).ToList();

            _filteredTags = new List<SelectableTag>(_allTags);
            AvailableTagsListBox.ItemsSource = _filteredTags;
            UpdateSelectedTagsDisplay();
        }
        catch (Exception ex)
        {
            // Handle error gracefully
            TagSearchBox.IsEnabled = false;
            AvailableTagsListBox.IsEnabled = false;
            SelectedTagsText.Text = $"Error loading tags: {ex.Message}";
        }
    }

    private void TagSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var searchText = TagSearchBox.Text.ToLower();
        
        if (string.IsNullOrWhiteSpace(searchText))
        {
            _filteredTags = new List<SelectableTag>(_allTags);
        }
        else
        {
            _filteredTags = _allTags.Where(t => t.Name.ToLower().Contains(searchText)).ToList();
        }
        
        AvailableTagsListBox.ItemsSource = _filteredTags;
    }

    private void AvailableTagsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Handle selection changes if needed
    }

    private void TagCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        UpdateSelectedTagsDisplay();
    }

    private void UpdateSelectedTagsDisplay()
    {
        var selectedTags = _allTags.Where(t => t.IsSelected).Select(t => t.Name).ToList();
        
        if (selectedTags.Any())
        {
            SelectedTagsText.Text = string.Join(", ", selectedTags);
        }
        else
        {
            SelectedTagsText.Text = "None";
        }
    }
}

public class MetadataRequest
{
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? DateReviewed { get; set; }
    public string? ReviewedBy { get; set; }
    public string? TestedBy { get; set; }
    public DateTime? DateTested { get; set; }
    public string? FileCategory { get; set; }
}