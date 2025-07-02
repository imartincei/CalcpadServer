using System.IO;
using System.Windows;
using Microsoft.Win32;
using CalcpadViewer.Models;

namespace CalcpadViewer;

public partial class UploadDialog : Window
{
    public string? SelectedFilePath { get; private set; }
    public string? Filename { get; private set; }
    public Dictionary<string, string> Tags { get; private set; } = new();
    public MetadataRequest Metadata { get; private set; } = new();
    
    public UploadDialog()
    {
        InitializeComponent();
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


        // Parse tags
        Tags.Clear();
        if (!string.IsNullOrWhiteSpace(TagsTextBox.Text))
        {
            var tagList = TagsTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var tagIndex = 1;
            foreach (var tag in tagList)
            {
                var trimmedTag = tag.Trim();
                if (!string.IsNullOrEmpty(trimmedTag))
                {
                    Tags[$"tag{tagIndex}"] = trimmedTag;
                    tagIndex++;
                }
            }
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