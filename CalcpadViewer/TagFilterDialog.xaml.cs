using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using CalcpadViewer.Models;

namespace CalcpadViewer
{
    public partial class TagFilterDialog : Window
    {
        public ObservableCollection<SelectableTag> FilteredTags { get; set; }
        public List<PreDefinedTag> SelectedTags { get; private set; }
        
        private List<SelectableTag> _allTags;
        private string _searchText = "";

        public TagFilterDialog(List<PreDefinedTag> availableTags, List<PreDefinedTag> currentlySelectedTags)
        {
            InitializeComponent();
            
            // Initialize collections
            FilteredTags = new ObservableCollection<SelectableTag>();
            SelectedTags = new List<PreDefinedTag>();
            
            // Handle null inputs
            availableTags = availableTags ?? new List<PreDefinedTag>();
            currentlySelectedTags = currentlySelectedTags ?? new List<PreDefinedTag>();
            
            // Convert tags to selectable tags
            _allTags = availableTags.Select(tag => new SelectableTag 
            { 
                Id = tag.Id, 
                Name = tag.Name, 
                IsSelected = currentlySelectedTags.Any(st => st.Id == tag.Id)
            }).ToList();
            
            // Set data context
            DataContext = this;
            
            // Initialize UI
            UpdateFilteredTags();
            UpdateSelectedTagsSummary();
            
            // Set focus to search box and clear placeholder text
            TagSearchBox.Focus();
            TagSearchBox.SelectAll();
        }

        private void UpdateFilteredTags()
        {
            if (FilteredTags == null || _allTags == null)
                return;
                
            FilteredTags.Clear();
            
            var filtered = _allTags.Where(tag => 
                string.IsNullOrEmpty(_searchText) || 
                tag.Name.ToLower().Contains(_searchText.ToLower())
            ).OrderBy(tag => tag.Name);
            
            foreach (var tag in filtered)
            {
                FilteredTags.Add(tag);
            }
        }

        private void UpdateSelectedTagsSummary()
        {
            if (_allTags == null)
            {
                SelectedTagsSummary.Text = "No tags selected";
                return;
            }
            
            var selectedCount = _allTags.Count(t => t.IsSelected);
            
            if (selectedCount == 0)
            {
                SelectedTagsSummary.Text = "No tags selected";
            }
            else if (selectedCount == 1)
            {
                var selectedTag = _allTags.First(t => t.IsSelected);
                SelectedTagsSummary.Text = $"1 tag selected: {selectedTag.Name}";
            }
            else
            {
                SelectedTagsSummary.Text = $"{selectedCount} tags selected";
            }
        }

        private void TagSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            _searchText = textBox?.Text == "Search tags..." ? "" : textBox?.Text ?? "";
            UpdateFilteredTags();
        }

        private void TagCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            var tag = checkBox?.DataContext as SelectableTag;
            if (tag != null && _allTags != null)
            {
                tag.IsSelected = true;
                var originalTag = _allTags.FirstOrDefault(t => t.Id == tag.Id);
                if (originalTag != null)
                {
                    originalTag.IsSelected = true;
                }
                UpdateSelectedTagsSummary();
            }
        }

        private void TagCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            var checkBox = sender as CheckBox;
            var tag = checkBox?.DataContext as SelectableTag;
            if (tag != null && _allTags != null)
            {
                tag.IsSelected = false;
                var originalTag = _allTags.FirstOrDefault(t => t.Id == tag.Id);
                if (originalTag != null)
                {
                    originalTag.IsSelected = false;
                }
                UpdateSelectedTagsSummary();
            }
        }

        private void AvailableTagsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // This method can be used for additional selection handling if needed
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allTags != null)
            {
                foreach (var tag in _allTags)
                {
                    tag.IsSelected = true;
                }
            }
            
            if (FilteredTags != null)
            {
                foreach (var tag in FilteredTags)
                {
                    tag.IsSelected = true;
                }
            }
            
            UpdateSelectedTagsSummary();
        }

        private void ClearAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_allTags != null)
            {
                foreach (var tag in _allTags)
                {
                    tag.IsSelected = false;
                }
            }
            
            if (FilteredTags != null)
            {
                foreach (var tag in FilteredTags)
                {
                    tag.IsSelected = false;
                }
            }
            
            UpdateSelectedTagsSummary();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Get selected tags
            SelectedTags = _allTags?.Where(tag => tag.IsSelected)
                .Select(tag => new PreDefinedTag { Id = tag.Id, Name = tag.Name })
                .ToList() ?? new List<PreDefinedTag>();
            
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}