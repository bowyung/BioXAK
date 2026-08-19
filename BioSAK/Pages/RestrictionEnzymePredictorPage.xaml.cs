using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BioSAK.Models;
using BioSAK.Services;

namespace BioSAK.Pages
{
    /// <summary>
    /// Restriction Enzyme Predictor 頁面 - 分析所有酶的切割情況
    /// </summary>
    public partial class RestrictionEnzymePredictorPage : Page
    {
        private readonly RestrictionEnzymeCutter _cutter;
        private List<RestrictionEnzyme> _allEnzymes;
        private List<EnzymeAnalysisResult> _allResults;

        public RestrictionEnzymePredictorPage()
        {
            InitializeComponent();
            
            _cutter = new RestrictionEnzymeCutter();
            LoadEnzymes();
        }

        /// <summary>
        /// 載入酶列表
        /// </summary>
        private void LoadEnzymes()
        {
            try
            {
                _allEnzymes = RebaseParser.LoadEnzymes();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading enzyme database: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _allEnzymes = new List<RestrictionEnzyme>();
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
        /// 分析按鈕點擊 (fix #3: async 非同步處理)
        /// </summary>
        private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            string sequence = CleanSequence(SequenceTextBox.Text);
            
            if (string.IsNullOrEmpty(sequence))
            {
                MessageBox.Show("Please enter a DNA sequence.", "Validation", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isCircular = CircularRadio.IsChecked == true;

            // 防止重複點擊並提示使用者
            AnalyzeButton.IsEnabled = false;
            AnalyzeButton.Content = "Analyzing...";
            ResultsSummaryText.Text = "Analyzing sequence, please wait...";

            try
            {
                // 將繁重的運算丟到背景執行緒
                _allResults = await Task.Run(() => 
                    _cutter.AnalyzeAllEnzymes(sequence, _allEnzymes, isCircular)
                );
                
                // 運算完成後，回到 UI 執行緒套用篩選
                ApplyFilters();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during analysis: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 恢復按鈕狀態
                AnalyzeButton.IsEnabled = true;
                AnalyzeButton.Content = BioSAK.Localization.L.T("Analyze_All_Enzymes");
            }
        }

        /// <summary>
        /// 篩選器狀態改變時即時更新 (fix #2)
        /// </summary>
        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (_allResults != null)
            {
                ApplyFilters();
            }
        }

        /// <summary>
        /// 套用篩選條件 (fix #1: 修正「全不選」漏洞)
        /// </summary>
        private void ApplyFilters()
        {
            if (_allResults == null || _allResults.Count == 0)
            {
                ResultsDataGrid.ItemsSource = null;
                ResultsSummaryText.Text = "No results to display.";
                return;
            }

            var filtered = _allResults.AsEnumerable();

            // --- 修正長度篩選：直接套用 Where，全不選時自然為 0 筆 ---
            var allowedLengths = new List<int>();
            if (Filter4Cutter.IsChecked == true) allowedLengths.Add(4);
            if (Filter6Cutter.IsChecked == true) allowedLengths.Add(6);
            if (Filter8Cutter.IsChecked == true) allowedLengths.Add(8);
            bool allowOther = FilterOther.IsChecked == true;

            filtered = filtered.Where(r =>
                allowedLengths.Contains(r.Enzyme.RecognitionSequence.Length) ||
                (allowOther && !new[] { 4, 6, 8 }.Contains(r.Enzyme.RecognitionSequence.Length))
            );

            // --- 修正末端類型篩選：直接套用 Where，全不選時自然為 0 筆 ---
            var allowedOverhangs = new List<OverhangType>();
            if (Filter5Prime.IsChecked == true) allowedOverhangs.Add(OverhangType.FivePrime);
            if (Filter3Prime.IsChecked == true) allowedOverhangs.Add(OverhangType.ThreePrime);
            if (FilterBlunt.IsChecked == true) allowedOverhangs.Add(OverhangType.Blunt);

            filtered = filtered.Where(r => allowedOverhangs.Contains(r.Enzyme.OverhangType));

            // 篩選切割次數
            filtered = filtered.Where(r =>
            {
                if (r.CutCount == 0) return ShowNoCutters.IsChecked == true;
                if (r.CutCount == 1) return ShowSingleCutters.IsChecked == true;
                return ShowMultiCutters.IsChecked == true;
            });

            var resultList = filtered.ToList();
            ResultsDataGrid.ItemsSource = resultList;

            // 更新摘要
            int totalEnzymes = resultList.Count;
            int cutters = resultList.Count(r => r.CutCount > 0);
            int singleCutters = resultList.Count(r => r.CutCount == 1);
            
            ResultsSummaryText.Text = $"{totalEnzymes} enzymes shown | " +
                                      $"{cutters} cut the sequence | " +
                                      $"{singleCutters} single cutters";
        }

        /// <summary>
        /// 選擇結果項目
        /// </summary>
        private void ResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = ResultsDataGrid.SelectedItem as EnzymeAnalysisResult;
            
            if (selected != null)
            {
                DetailEnzymeName.Text = selected.Enzyme.Name;
                DetailRecognition.Text = $"Recognition: {selected.Enzyme.RecognitionSequence}";
                
                string overhangInfo = selected.Enzyme.OverhangType switch
                {
                    OverhangType.FivePrime => $"5' overhang ({selected.Enzyme.GetOverhangSequence()})",
                    OverhangType.ThreePrime => $"3' overhang ({selected.Enzyme.GetOverhangSequence()})",
                    OverhangType.Blunt => "Blunt end",
                    _ => "Unknown"
                };
                
                DetailCutInfo.Text = $"Cut positions: {selected.Enzyme.CutPosition5}/{selected.Enzyme.CutPosition3}\n" +
                                    $"End type: {overhangInfo}\n" +
                                    $"Palindromic: {(selected.Enzyme.IsPalindromic ? "Yes" : "No")}\n" +
                                    $"Number of cuts: {selected.CutCount}";

                if (selected.CutCount > 0)
                {
                    var positionDetails = selected.CutSites
                        .Select(cs => $"  Position {cs.Position}: " +
                                     $"5' cut at {cs.Cut5Position}, " +
                                     $"3' cut at {cs.Cut3Position}")
                        .ToList();
                    
                    DetailPositions.Text = "Cut site details:\n" + string.Join("\n", positionDetails);
                }
                else
                {
                    DetailPositions.Text = "No cut sites in this sequence.";
                }

                DetailPopup.IsOpen = true;
            }
            else
            {
                DetailPopup.IsOpen = false;
            }
        }

        /// <summary>
        /// 清理序列
        /// </summary>
        private string CleanSequence(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

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
