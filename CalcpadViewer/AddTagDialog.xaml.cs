using System.Windows;

namespace CalcpadViewer;

public partial class AddTagDialog : Window
{
    public string TagName { get; private set; } = string.Empty;

    public AddTagDialog()
    {
        InitializeComponent();
        TagNameTextBox.Focus();
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var tagName = TagNameTextBox.Text.Trim();
        
        if (string.IsNullOrWhiteSpace(tagName))
        {
            MessageBox.Show("Please enter a tag name.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            TagNameTextBox.Focus();
            return;
        }

        if (tagName.Length > 100)
        {
            MessageBox.Show("Tag name cannot exceed 100 characters.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            TagNameTextBox.Focus();
            return;
        }

        TagName = tagName;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}