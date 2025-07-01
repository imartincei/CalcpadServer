using System.IO;
using System.Windows;
using Microsoft.Win32;
using CalcpadViewer.Models;

namespace CalcpadViewer;

public partial class UploadDialog : Window
{
    public string? SelectedFilePath { get; private set; }
    public Dictionary<string, string> Tags { get; private set; } = new();
    public StructuredMetadataRequest Metadata { get; private set; } = new();
    
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
            
            // Auto-populate original filename if not set
            if (string.IsNullOrEmpty(OriginalFileNameTextBox.Text))
            {
                OriginalFileNameTextBox.Text = Path.GetFileName(SelectedFilePath);
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

        // Parse structured metadata
        Metadata = new StructuredMetadataRequest
        {
            OriginalFileName = string.IsNullOrWhiteSpace(OriginalFileNameTextBox.Text) ? null : OriginalFileNameTextBox.Text.Trim(),
            UpdatedBy = string.IsNullOrWhiteSpace(UpdatedByTextBox.Text) ? null : UpdatedByTextBox.Text.Trim(),
            DateCreated = DateCreatedPicker.SelectedDate,
            DateUpdated = DateUpdatedPicker.SelectedDate,
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
}

public class StructuredMetadataRequest
{
    public string? OriginalFileName { get; set; }
    public DateTime? DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? DateReviewed { get; set; }
    public string? ReviewedBy { get; set; }
    public string? TestedBy { get; set; }
    public DateTime? DateTested { get; set; }
}