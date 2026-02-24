using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using BioSAK.Services;

namespace BioSAK.Pages
{
    public partial class GeneIdConverterPage : Page
    {
        private readonly GeneIdService _geneService;
        private List<GeneConversionResult> _currentResults;

        public GeneIdConverterPage()
        {
            InitializeComponent();
            _geneService = new GeneIdService();

            InputGenesTextBox.TextChanged += (s, e) => UpdateInputCount();
            Loaded += GeneIdConverterPage_Loaded;
        }

        private async void GeneIdConverterPage_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDatabaseAsync();
        }

        #region Progress

        private void ShowProgress(string title, int value = 0)
        {
            ProgressTitle.Text = title;
            ProgressBar.Value = value;
            ProgressText.Text = $"{value}%";
            ProgressOverlay.Visibility = Visibility.Visible;
        }

        private void UpdateProgress(int value, string message = null)
        {
            ProgressBar.Value = value;
            ProgressText.Text = message ?? $"{value}%";
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private void HideProgress()
        {
            ProgressOverlay.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Database Loading

        private async Task LoadDatabaseAsync()
        {
            try
            {
                ShowProgress("Loading gene database...", 0);

                string species = GetSelectedSpecies();
                UpdateProgress(20, $"Loading {species} data...");

                // 檢查資料庫是否存在
                if (!_geneService.DatabaseExists(species))
                {
                    HideProgress();
                    DatabaseVersionText.Text = $"Database for {species} not found!";
                    LastUpdateText.Text = "Please click 'Download Selected' to get the database.";

                    // 顯示可用的資料庫
                    var available = _geneService.GetAvailableDatabases();
                    if (available.Count > 0)
                    {
                        AvailableDbText.Text = $"Available databases: {string.Join(", ", available)}";
                    }
                    else
                    {
                        AvailableDbText.Text = "No databases found. Please download first.";
                    }
                    return;
                }

                bool loaded = await _geneService.LoadDatabaseAsync(species);

                if (loaded)
                {
                    UpdateProgress(100, "Complete!");
                    var info = _geneService.GetDatabaseInfo();
                    DatabaseVersionText.Text = $"Version: {info.Version} | Genes: {info.GeneCount:N0} | Source: {info.Sources}";
                    LastUpdateText.Text = $"Last update: {info.LastUpdate:yyyy-MM-dd}";

                    var available = _geneService.GetAvailableDatabases();
                    AvailableDbText.Text = $"Available databases: {string.Join(", ", available)}";
                }
                else
                {
                    DatabaseVersionText.Text = "Failed to load database.";
                    LastUpdateText.Text = _geneService.LastError ?? "Unknown error";
                }

                HideProgress();
            }
            catch (Exception ex)
            {
                HideProgress();
                MessageBox.Show($"Error loading database: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SpeciesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            await LoadDatabaseAsync();
        }

        private string GetSelectedSpecies()
        {
            if (SpeciesComboBox.SelectedItem is ComboBoxItem item)
                return item.Tag?.ToString() ?? "human";
            return "human";
        }

        #endregion

        #region Input Handling

        private void UpdateInputCount()
        {
            var genes = ParseInputGenes();
            InputCountText.Text = $"{genes.Count} genes";
        }

        private List<string> ParseInputGenes()
        {
            var text = InputGenesTextBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();

            var separators = new[] { '\n', '\r', ',', '\t', ';' };
            return text.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                       .Select(g => g.Trim())
                       .Where(g => !string.IsNullOrEmpty(g))
                       .Distinct()
                       .ToList();
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                InputGenesTextBox.Text = Clipboard.GetText();
            }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            InputGenesTextBox.Clear();
            ResultsDataGrid.ItemsSource = null;
            _currentResults = null;
            ResultSummaryText.Text = "";
            NotFoundExpander.Visibility = Visibility.Collapsed;
            MultiMatchExpander.Visibility = Visibility.Collapsed;
        }

        private void LoadFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Text files (*.txt;*.csv;*.tsv)|*.txt;*.csv;*.tsv|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    InputGenesTextBox.Text = File.ReadAllText(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading file: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Conversion

        private async void Convert_Click(object sender, RoutedEventArgs e)
        {
            var genes = ParseInputGenes();
            if (genes.Count == 0)
            {
                MessageBox.Show("Please enter gene IDs to convert.", "Input Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_geneService.IsDatabaseLoaded)
            {
                MessageBox.Show("Gene database not loaded. Please wait or click 'Download Selected'.",
                    "Database Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 按鈕鎖定，防止重複點擊
            var convertBtn = sender as Button;
            if (convertBtn != null) convertBtn.IsEnabled = false;

            try
            {
                ShowProgress("Converting gene IDs...", 0);

                bool useAutoDetect = InputTypeAuto.IsChecked == true;
                string fixedInputType = GetInputType();

                var results = new List<GeneConversionResult>();
                var notFound = new List<string>();
                var multiMatch = new List<string>();

                await Task.Run(() =>
                {
                    for (int i = 0; i < genes.Count; i++)
                    {
                        if (i % 100 == 0)
                        {
                            int progress = (i * 100) / genes.Count;
                            Dispatcher.Invoke(() => UpdateProgress(progress, $"Processing {i + 1}/{genes.Count}..."));
                        }

                        var gene = genes[i];

                        // 決定輸入類型
                        string inputType = useAutoDetect ? _geneService.DetectIdType(gene) : fixedInputType;

                        var matches = _geneService.Convert(gene, inputType);

                        if (matches.Count == 0)
                        {
                            notFound.Add(gene);
                            results.Add(new GeneConversionResult
                            {
                                Input = gene,
                                Status = "Not Found"
                            });
                        }
                        else if (matches.Count == 1)
                        {
                            var m = matches[0];
                            results.Add(new GeneConversionResult
                            {
                                Input = gene,
                                Status = "OK",
                                Symbol = m.Symbol,
                                EnsemblId = m.EnsemblId,
                                EntrezId = m.EntrezId,
                                HgncId = m.HgncId,
                                FullName = m.FullName,
                                Biotype = m.Biotype
                            });
                        }
                        else
                        {
                            multiMatch.Add($"{gene} ({matches.Count} matches)");
                            var m = matches[0];
                            results.Add(new GeneConversionResult
                            {
                                Input = gene,
                                Status = $"Multi ({matches.Count})",
                                Symbol = m.Symbol,
                                EnsemblId = m.EnsemblId,
                                EntrezId = m.EntrezId,
                                HgncId = m.HgncId,
                                FullName = m.FullName,
                                Biotype = m.Biotype
                            });

                            for (int j = 1; j < Math.Min(matches.Count, 5); j++)
                            {
                                var mm = matches[j];
                                results.Add(new GeneConversionResult
                                {
                                    Input = $"  └─ {gene}",
                                    Status = $"Alt {j + 1}",
                                    Symbol = mm.Symbol,
                                    EnsemblId = mm.EnsemblId,
                                    EntrezId = mm.EntrezId,
                                    HgncId = mm.HgncId,
                                    FullName = mm.FullName,
                                    Biotype = mm.Biotype
                                });
                            }
                        }
                    }
                });

                UpdateProgress(100, "Complete!");

                _currentResults = results;
                ResultsDataGrid.ItemsSource = results;

                int found = genes.Count - notFound.Count;
                ResultSummaryText.Text = $"Found: {found}/{genes.Count} | Not found: {notFound.Count} | Multiple matches: {multiMatch.Count}";

                if (notFound.Count > 0)
                {
                    NotFoundText.Text = string.Join(", ", notFound.Take(50));
                    if (notFound.Count > 50)
                        NotFoundText.Text += $"... and {notFound.Count - 50} more";
                    NotFoundExpander.Visibility = Visibility.Visible;
                }
                else
                {
                    NotFoundExpander.Visibility = Visibility.Collapsed;
                }

                if (multiMatch.Count > 0)
                {
                    MultiMatchText.Text = string.Join("\n", multiMatch.Take(20));
                    if (multiMatch.Count > 20)
                        MultiMatchText.Text += $"\n... and {multiMatch.Count - 20} more";
                    MultiMatchExpander.Visibility = Visibility.Visible;
                }
                else
                {
                    MultiMatchExpander.Visibility = Visibility.Collapsed;
                }

                HideProgress();
            }
            catch (Exception ex)
            {
                HideProgress();
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 確保按鈕一定會解鎖
                if (convertBtn != null) convertBtn.IsEnabled = true;
            }
        }

        private string GetInputType()
        {
            if (InputTypeAuto.IsChecked == true) return "auto";
            if (InputTypeSymbol.IsChecked == true) return "symbol";
            if (InputTypeEnsembl.IsChecked == true) return "ensembl";
            if (InputTypeEntrez.IsChecked == true) return "entrez";
            if (InputTypeHGNC.IsChecked == true) return "hgnc";
            return "symbol";
        }

        #endregion

        #region Copy Functions

        private void CopyCell_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsDataGrid.SelectedCells.Count > 0)
            {
                var sb = new StringBuilder();
                var selectedCells = ResultsDataGrid.SelectedCells
                    .OrderBy(c => ResultsDataGrid.Items.IndexOf(c.Item))
                    .ThenBy(c => c.Column.DisplayIndex);

                int lastRowIndex = -1;
                foreach (var cell in selectedCells)
                {
                    int rowIndex = ResultsDataGrid.Items.IndexOf(cell.Item);

                    if (lastRowIndex != -1 && rowIndex != lastRowIndex)
                        sb.AppendLine();
                    else if (lastRowIndex == rowIndex)
                        sb.Append("\t");

                    var item = cell.Item as GeneConversionResult;
                    if (item != null)
                    {
                        string value = GetCellValue(item, cell.Column.Header.ToString());
                        sb.Append(value ?? "");
                    }

                    lastRowIndex = rowIndex;
                }

                if (sb.Length > 0)
                    Clipboard.SetText(sb.ToString());
            }
        }

        private void CopyRow_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ResultsDataGrid.SelectedItems.Cast<GeneConversionResult>().ToList();
            if (selectedItems.Count == 0 && ResultsDataGrid.SelectedCells.Count > 0)
            {
                // 從選取的儲存格取得列
                selectedItems = ResultsDataGrid.SelectedCells
                    .Select(c => c.Item as GeneConversionResult)
                    .Where(i => i != null)
                    .Distinct()
                    .ToList();
            }

            if (selectedItems.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Input\tStatus\tSymbol\tEnsembl ID\tEntrez ID\tHGNC ID\tFull Name\tBiotype");

                foreach (var item in selectedItems)
                {
                    sb.AppendLine($"{item.Input}\t{item.Status}\t{item.Symbol}\t{item.EnsemblId}\t{item.EntrezId}\t{item.HgncId}\t{item.FullName}\t{item.Biotype}");
                }

                Clipboard.SetText(sb.ToString());
            }
        }

        private void CopyColumn_Click(object sender, RoutedEventArgs e)
        {
            if (ResultsDataGrid.SelectedCells.Count > 0 && _currentResults != null)
            {
                var columnHeader = ResultsDataGrid.SelectedCells[0].Column.Header.ToString();
                var values = _currentResults
                    .Where(r => !r.Input.StartsWith("  └─"))
                    .Select(r => GetCellValue(r, columnHeader))
                    .Where(v => !string.IsNullOrEmpty(v));

                Clipboard.SetText(string.Join("\n", values));
            }
        }

        /// <summary>
        /// 複製未找到的基因 ID 清單，方便使用者到其他工具查詢
        /// </summary>
        private void CopyNotFound_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResults == null) return;

            var notFoundIds = _currentResults
                .Where(r => r.Status == "Not Found")
                .Select(r => r.Input)
                .ToList();

            if (notFoundIds.Count == 0)
            {
                MessageBox.Show("No missing IDs to copy.", "Copy", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(string.Join(Environment.NewLine, notFoundIds));
            MessageBox.Show($"Copied {notFoundIds.Count} not-found ID(s) to clipboard.", "Copy Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string GetCellValue(GeneConversionResult item, string columnHeader)
        {
            return columnHeader switch
            {
                "Input" => item.Input,
                "Status" => item.Status,
                "Symbol" => item.Symbol,
                "Ensembl ID" => item.EnsemblId,
                "Entrez ID" => item.EntrezId,
                "HGNC ID" => item.HgncId,
                "Full Name" => item.FullName,
                "Biotype" => item.Biotype,
                _ => ""
            };
        }

        #endregion

        #region Export

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResults == null || _currentResults.Count == 0)
            {
                MessageBox.Show("No results to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"GeneIdConversion_{GetSelectedSpecies()}_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Input,Status,Symbol,EnsemblId,EntrezId,HgncId,FullName,Biotype");

                foreach (var r in _currentResults.Where(r => !r.Input.StartsWith("  └─")))
                {
                    sb.AppendLine($"\"{EscapeCsv(r.Input)}\",\"{r.Status}\",\"{EscapeCsv(r.Symbol)}\"," +
                        $"\"{EscapeCsv(r.EnsemblId)}\",\"{EscapeCsv(r.EntrezId)}\",\"{EscapeCsv(r.HgncId)}\"," +
                        $"\"{EscapeCsv(r.FullName)}\",\"{EscapeCsv(r.Biotype)}\"");
                }

                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Exported to:\n{dialog.FileName}", "Export Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("\"", "\"\"");
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_currentResults == null || _currentResults.Count == 0)
            {
                MessageBox.Show("No results to copy.", "Copy", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Input\tStatus\tSymbol\tEnsemblId\tEntrezId\tHgncId\tFullName\tBiotype");

            foreach (var r in _currentResults.Where(r => !r.Input.StartsWith("  └─")))
            {
                sb.AppendLine($"{r.Input}\t{r.Status}\t{r.Symbol}\t{r.EnsemblId}\t{r.EntrezId}\t{r.HgncId}\t{r.FullName}\t{r.Biotype}");
            }

            Clipboard.SetText(sb.ToString());
            MessageBox.Show("Results copied to clipboard (Tab-separated)!", "Copy Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        #endregion

        #region Database Update

        private async void UpdateDatabase_Click(object sender, RoutedEventArgs e)
        {
            var selectedSpecies = new List<string>();
            if (UpdateHumanCheck.IsChecked == true) selectedSpecies.Add("human");
            if (UpdateMouseCheck.IsChecked == true) selectedSpecies.Add("mouse");
            if (UpdateRatCheck.IsChecked == true) selectedSpecies.Add("rat");
            if (UpdateZebrafishCheck.IsChecked == true) selectedSpecies.Add("zebrafish");
            if (UpdateFlyCheck.IsChecked == true) selectedSpecies.Add("fly");
            if (UpdateWormCheck.IsChecked == true) selectedSpecies.Add("worm");

            if (selectedSpecies.Count == 0)
            {
                MessageBox.Show("Please select at least one species to download.", "Selection Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var speciesNames = string.Join(", ", selectedSpecies.Select(s => char.ToUpper(s[0]) + s.Substring(1)));
            var result = MessageBox.Show(
                $"Download gene databases for:\n{speciesNames}\n\nThis may take 2-10 minutes depending on your internet connection.\n\nContinue?",
                "Download Databases", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // 按鈕鎖定，防止重複點擊
            var downloadBtn = sender as Button;
            if (downloadBtn != null) downloadBtn.IsEnabled = false;

            try
            {
                int totalSpecies = selectedSpecies.Count;
                int completed = 0;
                var failed = new List<string>();

                foreach (var species in selectedSpecies)
                {
                    ShowProgress($"Downloading {species}...", 0);
                    UpdateStatusText.Text = $"Downloading {completed + 1}/{totalSpecies}: {species}";

                    bool success = await _geneService.DownloadDatabaseAsync(species, (progress, message) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            UpdateProgress(progress, message);
                        });
                    });

                    if (!success)
                    {
                        failed.Add(species);
                    }

                    completed++;
                }

                HideProgress();

                await LoadDatabaseAsync();

                if (failed.Count == 0)
                {
                    UpdateStatusText.Text = $"✓ Downloaded {completed} database(s) successfully";
                    MessageBox.Show($"Successfully downloaded {completed} gene database(s)!",
                        "Download Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    UpdateStatusText.Text = $"⚠ {failed.Count} download(s) failed";
                    MessageBox.Show($"Downloaded {completed - failed.Count}/{completed} databases.\n\nFailed: {string.Join(", ", failed)}",
                        "Download Partial", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                HideProgress();
                UpdateStatusText.Text = "✗ Download failed";
                MessageBox.Show($"Error downloading databases:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // 確保按鈕一定會解鎖
                if (downloadBtn != null) downloadBtn.IsEnabled = true;
            }
        }

        #endregion
    }

    #region Result Model

    public class GeneConversionResult
    {
        public string Input { get; set; }
        public string Status { get; set; }
        public string Symbol { get; set; }
        public string EnsemblId { get; set; }
        public string EntrezId { get; set; }
        public string HgncId { get; set; }
        public string FullName { get; set; }
        public string Biotype { get; set; }
    }

    #endregion
}
