using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using BioSAK.Models;
using BioSAK.Services;
using BioSAK.Controls;

namespace BioSAK.Pages
{
    /// <summary>
    /// Restriction Enzyme Pattern Predictor 頁面
    /// </summary>
    public partial class RestrictionEnzymePatternPage : Page
    {
        private readonly RestrictionEnzymeCutter _cutter;
        private List<RestrictionEnzyme> _allEnzymes;
        private RestrictionEnzyme _selectedEnzyme;

        public RestrictionEnzymePatternPage()
        {
            InitializeComponent();
            
            _cutter = new RestrictionEnzymeCutter();
            LoadEnzymes();
            
            // 綁定序列輸入變更事件
            SequenceTextBox.TextChanged += SequenceTextBox_TextChanged;
        }

        /// <summary>
        /// 載入酶列表
        /// </summary>
        private void LoadEnzymes()
        {
            try
            {
                _allEnzymes = RebaseParser.LoadEnzymes();
                EnzymeListBox.ItemsSource = _allEnzymes;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading enzyme database: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 序列輸入變更
        /// </summary>
        private void SequenceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string sequence = CleanSequence(SequenceTextBox.Text);
            SequenceLengthText.Text = $"{sequence.Length} bp";
        }

        /// <summary>
        /// 酶搜尋
        /// </summary>
        private void EnzymeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = EnzymeSearchBox.Text.Trim();
            
            if (string.IsNullOrEmpty(searchText))
            {
                EnzymeListBox.ItemsSource = _allEnzymes;
            }
            else
            {
                var filtered = _allEnzymes
                    .Where(enzyme => 
                        enzyme.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        enzyme.RecognitionSequence.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
                EnzymeListBox.ItemsSource = filtered;
            }
        }

        /// <summary>
        /// 酶選擇變更
        /// </summary>
        private void EnzymeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedEnzyme = EnzymeListBox.SelectedItem as RestrictionEnzyme;
            
            if (_selectedEnzyme != null)
            {
                SelectedEnzymePanel.Visibility = Visibility.Visible;
                SelectedEnzymeName.Text = _selectedEnzyme.Name;
                
                string overhangInfo = _selectedEnzyme.OverhangType switch
                {
                    OverhangType.FivePrime => $"5' overhang ({_selectedEnzyme.GetOverhangSequence()})",
                    OverhangType.ThreePrime => $"3' overhang ({_selectedEnzyme.GetOverhangSequence()})",
                    OverhangType.Blunt => "Blunt end",
                    _ => "Unknown"
                };
                
                SelectedEnzymeInfo.Text = $"Recognition: {_selectedEnzyme.RecognitionSequence}\n" +
                                         $"Cut site: {_selectedEnzyme.CutPosition5}/{_selectedEnzyme.CutPosition3}\n" +
                                         $"End type: {overhangInfo}\n" +
                                         $"Palindromic: {(_selectedEnzyme.IsPalindromic ? "Yes" : "No")}";
            }
            else
            {
                SelectedEnzymePanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Marker 類型變更
        /// </summary>
        private void MarkerTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GelControl == null) return;
            
            var markerType = MarkerTypeCombo.SelectedIndex == 0 
                ? ElectrophoresisGelControl.MarkerType.Standard 
                : ElectrophoresisGelControl.MarkerType.HighResolution;
            
            GelControl.SetMarkerType(markerType);
        }

        /// <summary>
        /// 執行切割
        /// </summary>
        private void DigestButton_Click(object sender, RoutedEventArgs e)
        {
            // 驗證輸入
            if (_selectedEnzyme == null)
            {
                MessageBox.Show("Please select an enzyme.", "Validation", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string sequence = CleanSequence(SequenceTextBox.Text);
            if (string.IsNullOrEmpty(sequence))
            {
                MessageBox.Show("Please enter a DNA sequence.", "Validation", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isCircular = CircularRadio.IsChecked == true;

            try
            {
                // 執行切割
                var fragments = _cutter.DigestSequence(sequence, _selectedEnzyme, isCircular);
                var cutSites = _cutter.FindCutSites(sequence, _selectedEnzyme, isCircular);

                // 更新結果面板
                ResultsPanel.Visibility = Visibility.Visible;
                CutSitesText.Text = $"Cut sites found: {cutSites.Count}";
                
                if (fragments.Count > 0)
                {
                    var sizeList = fragments.Select(f => $"{f.Size} bp").ToList();
                    FragmentsText.Text = $"Fragments: {string.Join(", ", sizeList)}";
                }
                else
                {
                    FragmentsText.Text = "No fragments generated.";
                }

                // 更新表格
                FragmentsDataGrid.ItemsSource = fragments;

                // 更新電泳圖
                GelControl.SetFragments(fragments);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during digestion: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 清理序列
        /// </summary>
        private string CleanSequence(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            // 移除非 DNA 字元
            var cleaned = new System.Text.StringBuilder();
            foreach (char c in input.ToUpper())
            {
                if ("ATGCRYKMSWBDHVN".Contains(c))
                {
                    cleaned.Append(c);
                }
            }
            return cleaned.ToString();
        }
    }
}
