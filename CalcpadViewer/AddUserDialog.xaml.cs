using System.Windows;
using System.Windows.Controls;
using CalcpadViewer.Models;

namespace CalcpadViewer;

public partial class AddUserDialog : Window
{
    public RegisterRequest? UserRequest { get; private set; }
    
    public AddUserDialog()
    {
        InitializeComponent();
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e)
    {
        if (ValidateInput())
        {
            var selectedRole = ((ComboBoxItem)RoleComboBox.SelectedItem).Tag.ToString();
            var role = Enum.Parse<UserRole>(selectedRole!);
            
            UserRequest = new RegisterRequest
            {
                Username = UsernameTextBox.Text.Trim(),
                Email = EmailTextBox.Text.Trim(),
                Password = PasswordBox.Password,
                Role = role
            };
            
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool ValidateInput()
    {
        if (string.IsNullOrWhiteSpace(UsernameTextBox.Text))
        {
            MessageBox.Show("Username is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            UsernameTextBox.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(EmailTextBox.Text))
        {
            MessageBox.Show("Email is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            EmailTextBox.Focus();
            return false;
        }

        if (!IsValidEmail(EmailTextBox.Text.Trim()))
        {
            MessageBox.Show("Please enter a valid email address.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            EmailTextBox.Focus();
            return false;
        }

        if (string.IsNullOrWhiteSpace(PasswordBox.Password))
        {
            MessageBox.Show("Password is required.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            PasswordBox.Focus();
            return false;
        }

        if (PasswordBox.Password.Length < 6)
        {
            MessageBox.Show("Password must be at least 6 characters long.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            PasswordBox.Focus();
            return false;
        }

        if (PasswordBox.Password != ConfirmPasswordBox.Password)
        {
            MessageBox.Show("Passwords do not match.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            ConfirmPasswordBox.Focus();
            return false;
        }

        if (RoleComboBox.SelectedItem == null)
        {
            MessageBox.Show("Please select a role.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            RoleComboBox.Focus();
            return false;
        }

        return true;
    }

    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}