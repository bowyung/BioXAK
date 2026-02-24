using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using BioSAK.Services;

namespace BioSAK.Pages
{
    public partial class sgRNADesignerPage : Page
    {
        private static readonly HttpClient httpClient;

        static sgRNADesignerPage()
        {
            httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "BioSAK/1.0 (contact@example.com)");
        }

        private ObservableCollection<sgRNACandidate> sgRNACandidates = new ObservableCollection<sgRNACandidate>();
        private List<ExonInfo> exonList = new List<ExonInfo>();
        private List<CDSRegion> cdsList = new List<CDSRegion>();
        private string genomicSequence = "";
        private string currentGeneSymbol = "";
        private string currentAccession = "";
        private string currentTaxId = "9606";
        private bool isInputCollapsed = false;

        private Dictionary<int, Ellipse> sgRNAEllipses = new Dictionary<int, Ellipse>();

        private const string NCBI_ESEARCH_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esearch.fcgi";
        private const string NCBI_EFETCH_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi";
        private const string NCBI_ELINK_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/elink.fcgi";

        // GeneIdService 用於 ID 轉換
        private readonly GeneIdService _geneIdService;

        public sgRNADesignerPage()
        {
            InitializeComponent();
            sgRNADataGrid.ItemsSource = sgRNACandidates;

            // 初始化 GeneIdService
            _geneIdService = new GeneIdService();

            // 監聽 IsSelected 變化來更新全選狀態
            sgRNACandidates.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (sgRNACandidate item in e.NewItems)
                    {
                        item.PropertyChanged += SgRNACandidate_PropertyChanged;
                    }
                }
            };
        }

        private void SgRNACandidate_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "IsSelected")
            {
                UpdateSelectionStatus();
                UpdateSelectAllCheckBoxState();
            }
        }

        #region Select All Checkbox

        private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                bool isChecked = checkBox.IsChecked == true;
                foreach (var sg in sgRNACandidates)
                {
                    sg.IsSelected = isChecked;
                }
                UpdateSelectionStatus();
            }
        }

        private void UpdateSelectAllCheckBoxState()
        {
            if (sgRNACandidates.Count == 0)
            {
                SelectAllCheckBox.IsChecked = false;
                return;
            }

            int selectedCount = sgRNACandidates.Count(c => c.IsSelected);
            if (selectedCount == 0)
            {
                SelectAllCheckBox.IsChecked = false;
            }
            else if (selectedCount == sgRNACandidates.Count)
            {
                SelectAllCheckBox.IsChecked = true;
            }
            else
            {
                SelectAllCheckBox.IsChecked = null; // 中間狀態 (部分選中)
            }
        }

        #endregion

        #region Collapsible Input Section

        private void InputHeader_Click(object sender, MouseButtonEventArgs e)
        {
            ToggleInputSection();
        }

        private void ToggleInputSection()
        {
            isInputCollapsed = !isInputCollapsed;

            if (isInputCollapsed)
            {
                InputContentPanel.Visibility = Visibility.Collapsed;
                CollapseArrow.Text = "▶";
            }
            else
            {
                InputContentPanel.Visibility = Visibility.Visible;
                CollapseArrow.Text = "▼";
            }
        }

        private void CollapseInputSection()
        {
            if (!isInputCollapsed)
            {
                isInputCollapsed = true;
                InputContentPanel.Visibility = Visibility.Collapsed;
                CollapseArrow.Text = "▶";
            }
        }

        private void ExpandInputSection()
        {
            if (isInputCollapsed)
            {
                isInputCollapsed = false;
                InputContentPanel.Visibility = Visibility.Visible;
                CollapseArrow.Text = "▼";
            }
        }

        #endregion

        #region Fetch Gene from NCBI

        private async void FetchGeneButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowLoading(true, "Connecting to NCBI...");
                ClearResults();

                string geneQuery = GeneQueryTextBox.Text.Trim();
                currentTaxId = GetSelectedTaxId();

                if (string.IsNullOrEmpty(geneQuery))
                {
                    ShowInfo("Enter gene name/ID.", true);
                    return;
                }

                ShowLoading(true, $"Searching {geneQuery}...");
                await FetchByGeneQuery(geneQuery, currentTaxId);

                AnalyzeButton.IsEnabled = cdsList.Count > 0 && !string.IsNullOrEmpty(genomicSequence);

                if (AnalyzeButton.IsEnabled)
                {
                    InputSectionSummary.Text = $"✓ {currentGeneSymbol} ({currentAccession}) loaded";
                }
            }
            catch (Exception ex)
            {
                ShowInfo($"Error: {ex.Message}", true);
            }
            finally
            {
                ShowLoading(false);
            }
        }

        private string GetSelectedTaxId()
        {
            if (SpeciesComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
                return item.Tag.ToString();
            return "9606";
        }

        private string GetSpeciesNameFromTaxId(string taxId)
        {
            return taxId switch
            {
                "9606" => "human",
                "10090" => "mouse",
                "10116" => "rat",
                _ => "human"
            };
        }

        /// <summary>
        /// 改進的基因搜尋 - 支援多種 ID 類型，使用 GeneIdService 轉換
        /// </summary>
        private async Task FetchByGeneQuery(string query, string taxId)
        {
            string geneId = null;
            string resolvedSymbol = query;

            // 載入 GeneIdService 資料庫
            string speciesName = GetSpeciesNameFromTaxId(taxId);
            bool dbLoaded = false;

            if (_geneIdService.DatabaseExists(speciesName))
            {
                ShowLoading(true, "Loading gene database...");
                dbLoaded = await _geneIdService.LoadDatabaseAsync(speciesName);
            }

            if (dbLoaded && _geneIdService.IsDatabaseLoaded)
            {
                // 使用 GeneIdService 的自動偵測和轉換功能
                string detectedType = _geneIdService.DetectIdType(query);
                System.Diagnostics.Debug.WriteLine($"[sgRNA] Detected ID type: {detectedType} for '{query}'");

                // 嘗試轉換為 Symbol
                string convertedSymbol = _geneIdService.ConvertSingleToSymbol(query);

                if (!string.IsNullOrEmpty(convertedSymbol))
                {
                    resolvedSymbol = convertedSymbol;

                    // 取得完整的基因資訊
                    var matches = _geneIdService.Convert(query, detectedType);
                    if (matches.Count > 0)
                    {
                        var bestMatch = matches[0];

                        // 如果有 Entrez ID，直接使用
                        if (!string.IsNullOrEmpty(bestMatch.EntrezId))
                        {
                            geneId = bestMatch.EntrezId;
                        }

                        // 顯示轉換結果
                        string hint = "";
                        if (!query.Equals(resolvedSymbol, StringComparison.OrdinalIgnoreCase))
                        {
                            hint = $"'{query}' → {resolvedSymbol}";
                            if (!string.IsNullOrEmpty(bestMatch.FullName))
                            {
                                hint += $" ({bestMatch.FullName})";
                            }
                        }
                        else if (!string.IsNullOrEmpty(bestMatch.FullName))
                        {
                            hint = bestMatch.FullName;
                        }

                        SearchResultHint.Text = hint;
                        System.Diagnostics.Debug.WriteLine($"[sgRNA] Resolved: {query} → {resolvedSymbol}, Entrez: {geneId}");
                    }
                }
                else
                {
                    SearchResultHint.Text = $"'{query}' not found in local DB, searching NCBI...";
                    System.Diagnostics.Debug.WriteLine($"[sgRNA] '{query}' not found in local DB");
                }
            }
            else
            {
                SearchResultHint.Text = "Local DB not available, searching NCBI...";
            }

            // NCBI 搜尋
            if (string.IsNullOrEmpty(geneId))
            {
                ShowLoading(true, $"Searching NCBI for {resolvedSymbol}...");

                // 如果是純數字，可能是 Entrez ID
                if (int.TryParse(query, out _))
                {
                    geneId = query;
                }
                else
                {
                    string geneSearchUrl = $"{NCBI_ESEARCH_URL}?db=gene&term={Uri.EscapeDataString(resolvedSymbol)}[Gene Name]+AND+{taxId}[Taxonomy ID]&retmode=xml";

                    var geneSearchResponse = await httpClient.GetStringAsync(geneSearchUrl);
                    var geneXml = System.Xml.Linq.XDocument.Parse(geneSearchResponse);
                    var geneIds = geneXml.Descendants("Id").Select(x => x.Value).ToList();

                    if (geneIds.Count == 0)
                    {
                        geneSearchUrl = $"{NCBI_ESEARCH_URL}?db=gene&term={Uri.EscapeDataString(resolvedSymbol)}[sym]+AND+{taxId}[Taxonomy ID]&retmode=xml";
                        geneSearchResponse = await httpClient.GetStringAsync(geneSearchUrl);
                        geneXml = System.Xml.Linq.XDocument.Parse(geneSearchResponse);
                        geneIds = geneXml.Descendants("Id").Select(x => x.Value).ToList();
                    }

                    if (geneIds.Count == 0 && !query.Equals(resolvedSymbol, StringComparison.OrdinalIgnoreCase))
                    {
                        geneSearchUrl = $"{NCBI_ESEARCH_URL}?db=gene&term={Uri.EscapeDataString(query)}[Gene Name]+AND+{taxId}[Taxonomy ID]&retmode=xml";
                        geneSearchResponse = await httpClient.GetStringAsync(geneSearchUrl);
                        geneXml = System.Xml.Linq.XDocument.Parse(geneSearchResponse);
                        geneIds = geneXml.Descendants("Id").Select(x => x.Value).ToList();
                    }

                    if (geneIds.Count == 0)
                    {
                        ShowInfo($"Gene '{query}' not found.", true);
                        return;
                    }

                    geneId = geneIds.First();
                }
            }

            ShowLoading(true, $"Gene ID: {geneId}, finding RefSeqGene...");

            // 使用 elink 找 RefSeqGene
            string elinkUrl = $"{NCBI_ELINK_URL}?dbfrom=gene&db=nuccore&id={geneId}&linkname=gene_nuccore_refseqgene&retmode=xml";

            var elinkResponse = await httpClient.GetStringAsync(elinkUrl);
            var elinkXml = System.Xml.Linq.XDocument.Parse(elinkResponse);
            var nucIds = elinkXml.Descendants("Link").Select(x => x.Element("Id")?.Value).Where(x => x != null).ToList();

            if (nucIds.Count == 0)
            {
                string nucSearchUrl = $"{NCBI_ESEARCH_URL}?db=nuccore&term=NG_[Accession]+AND+{Uri.EscapeDataString(resolvedSymbol)}[Gene]+AND+{taxId}[Taxonomy ID]+AND+RefSeqGene[Keyword]&retmax=5&retmode=xml";
                var nucSearchResponse = await httpClient.GetStringAsync(nucSearchUrl);
                var nucXml = System.Xml.Linq.XDocument.Parse(nucSearchResponse);
                nucIds = nucXml.Descendants("Id").Select(x => x.Value).ToList();
            }

            if (nucIds.Count == 0)
            {
                ShowInfo($"No RefSeqGene for '{resolvedSymbol}'.", true);
                return;
            }

            await FetchAndParseGenBank(nucIds.First());
        }

        private async Task FetchAndParseGenBank(string nucId)
        {
            string gbUrl = $"{NCBI_EFETCH_URL}?db=nuccore&id={nucId}&rettype=gb&retmode=text";
            ShowLoading(true, "Downloading GenBank...");
            var gbResponse = await httpClient.GetStringAsync(gbUrl);

            string fastaUrl = $"{NCBI_EFETCH_URL}?db=nuccore&id={nucId}&rettype=fasta&retmode=text";
            ShowLoading(true, "Downloading sequence...");
            var fastaResponse = await httpClient.GetStringAsync(fastaUrl);

            genomicSequence = ParseFastaSequence(fastaResponse);

            ShowLoading(true, "Parsing features...");
            ParseGenBankForCDSAndExons(gbResponse);

            UpdateGeneVisualization();

            int cdsLen = cdsList.Sum(c => c.Length);
            ShowInfo($"✓ {currentGeneSymbol} ({currentAccession}) | {genomicSequence.Length:N0} bp | CDS: {cdsLen:N0} bp", false);
        }

        private string ParseFastaSequence(string input)
        {
            var lines = input.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new StringBuilder();
            foreach (var line in lines)
                if (!line.StartsWith(">"))
                    sb.Append(Regex.Replace(line, "[^ATCGatcg]", ""));
            return sb.ToString().ToUpper();
        }

        private void ParseGenBankForCDSAndExons(string gbText)
        {
            exonList.Clear();
            cdsList.Clear();

            var accMatch = Regex.Match(gbText, @"ACCESSION\s+(\S+)");
            if (accMatch.Success) currentAccession = accMatch.Groups[1].Value;

            var geneMatch = Regex.Match(gbText, @"/gene=""([^""]+)""");
            if (geneMatch.Success) currentGeneSymbol = geneMatch.Groups[1].Value;

            var cdsSection = Regex.Match(gbText, @"^\s{5}CDS\s+(join\([\s\S]*?\)|complement\(join\([\s\S]*?\)\)|\d+\.\.\d+)", RegexOptions.Multiline);
            if (cdsSection.Success)
            {
                string cdsContent = Regex.Replace(cdsSection.Groups[1].Value, @"\s+", "");
                var rangeMatches = Regex.Matches(cdsContent, @"(\d+)\.\.(\d+)");
                int cdsNum = 1;
                foreach (Match range in rangeMatches)
                {
                    int start = int.Parse(range.Groups[1].Value);
                    int end = int.Parse(range.Groups[2].Value);
                    cdsList.Add(new CDSRegion { PartNumber = cdsNum++, Start = start, End = end, Length = end - start + 1 });
                }
            }

            var exonMatches = Regex.Matches(gbText, @"^\s{5}exon\s+(?:complement\()?(\d+)\.\.(\d+)\)?", RegexOptions.Multiline);
            int exonNum = 1;
            foreach (Match match in exonMatches)
            {
                int start = int.Parse(match.Groups[1].Value);
                int end = int.Parse(match.Groups[2].Value);
                exonList.Add(new ExonInfo { ExonNumber = exonNum++, Start = start, End = end, Length = end - start + 1 });
            }

            exonList = exonList.OrderBy(e => e.Start).ToList();
            for (int i = 0; i < exonList.Count; i++) exonList[i].ExonNumber = i + 1;
        }

        #endregion

        #region Align DNA Sequence

        private async void AlignSequenceButton_Click(object sender, RoutedEventArgs e)
        {
            string sequence = SequenceInputTextBox.Text.Trim();
            if (sequence.StartsWith(">"))
            {
                int idx = sequence.IndexOf('\n');
                if (idx > 0) sequence = sequence.Substring(idx + 1);
            }
            sequence = Regex.Replace(sequence, @"[^ATCGatcg]", "").ToUpper();

            if (sequence.Length < 50)
            {
                ShowInfo("Enter longer sequence (50+ bp).", true);
                return;
            }

            try
            {
                ShowLoading(true, "Analyzing input sequence...");
                ClearResults();

                // 將整段序列視為一個 exon 和一個 CDS
                genomicSequence = sequence;
                currentGeneSymbol = "InputSeq";
                currentAccession = $"{sequence.Length}bp";
                currentTaxId = GetSelectedTaxId();

                exonList.Clear();
                cdsList.Clear();

                exonList.Add(new ExonInfo
                {
                    ExonNumber = 1,
                    Start = 1,
                    End = sequence.Length,
                    Length = sequence.Length
                });

                cdsList.Add(new CDSRegion
                {
                    PartNumber = 1,
                    Start = 1,
                    End = sequence.Length,
                    Length = sequence.Length
                });

                UpdateGeneVisualization();

                // 直接進行 sgRNA 分析
                string pamPattern = GetPAMPattern();
                int sgRNALength = GetsgRNALength();

                var candidates = FindsgRNAsInCDS(genomicSequence, pamPattern, sgRNALength);
                foreach (var c in candidates)
                {
                    c.PropertyChanged += SgRNACandidate_PropertyChanged;
                    sgRNACandidates.Add(c);
                }

                UpdateVisualizationWithsgRNAs();

                int recCount = sgRNACandidates.Count(c => c.IsRecommended);
                sgRNACountLabel.Text = $"{sgRNACandidates.Count} found, {recCount} ⭐";
                EmptyListText.Visibility = sgRNACandidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                ExportButton.IsEnabled = CopyButton.IsEnabled = BlastAllButton.IsEnabled = sgRNACandidates.Count > 0;
                AnalyzeButton.IsEnabled = true;
                FrameshiftAnalysisButton.IsEnabled = sgRNACandidates.Count > 0;

                ShowInfo($"Found {sgRNACandidates.Count} sgRNAs in input sequence ({sequence.Length:N0} bp), {recCount} recommended", false);

                CollapseInputSection();
                InputSectionSummary.Text = $"✓ Input sequence ({sequence.Length:N0} bp) | {sgRNACandidates.Count} sgRNAs";

                UpdateSelectAllCheckBoxState();
            }
            catch (Exception ex)
            {
                ShowInfo($"Error: {ex.Message}", true);
            }
            finally
            {
                ShowLoading(false);
            }
        }

        #endregion

        #region sgRNA Analysis

        private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(genomicSequence) || cdsList.Count == 0)
            {
                ShowInfo("Fetch gene data first.", true);
                return;
            }

            try
            {
                ShowProgress(true, "Finding PAM sites...");
                sgRNACandidates.Clear();

                string pamPattern = GetPAMPattern();
                int sgRNALength = GetsgRNALength();

                var candidates = FindsgRNAsInCDS(genomicSequence, pamPattern, sgRNALength);
                foreach (var c in candidates)
                {
                    c.PropertyChanged += SgRNACandidate_PropertyChanged;
                    sgRNACandidates.Add(c);
                }

                UpdateVisualizationWithsgRNAs();

                int recCount = sgRNACandidates.Count(c => c.IsRecommended);
                sgRNACountLabel.Text = $"{sgRNACandidates.Count} found, {recCount} ⭐";
                EmptyListText.Visibility = sgRNACandidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                ExportButton.IsEnabled = CopyButton.IsEnabled = BlastAllButton.IsEnabled = sgRNACandidates.Count > 0;

                ShowInfo($"Found {sgRNACandidates.Count} sgRNAs in CDS, {recCount} recommended", false);

                CollapseInputSection();
                InputSectionSummary.Text = $"✓ {currentGeneSymbol} | {sgRNACandidates.Count} sgRNAs";

                UpdateSelectAllCheckBoxState();
            }
            catch (Exception ex)
            {
                ShowInfo($"Error: {ex.Message}", true);
            }
            finally
            {
                ShowProgress(false);
            }
        }

        private string GetPAMPattern()
        {
            if (PAMComboBox.SelectedItem is ComboBoxItem item)
            {
                if (item.Content.ToString().Contains("NGG")) return "NGG";
                if (item.Content.ToString().Contains("NAG")) return "NAG";
            }
            return "NGG";
        }

        private int GetsgRNALength()
        {
            if (sgRNALengthComboBox.SelectedItem is ComboBoxItem item)
                if (int.TryParse(item.Content.ToString(), out int len)) return len;
            return 20;
        }

        private List<sgRNACandidate> FindsgRNAsInCDS(string sequence, string pamPattern, int sgRNALength)
        {
            var candidates = new List<sgRNACandidate>();
            string pamRegex = pamPattern.Replace("N", "[ATCG]").Replace("R", "[AG]");
            int idx = 1;

            foreach (var cds in cdsList)
            {
                int start = cds.Start - 1;
                int end = Math.Min(cds.End, sequence.Length);
                if (start < 0 || start >= sequence.Length) continue;

                string cdsSeq = sequence.Substring(start, end - start);

                // Forward strand (+)
                var fwdMatches = Regex.Matches(cdsSeq, $"([ATCG]{{{sgRNALength}}})({pamRegex})");
                foreach (Match m in fwdMatches)
                {
                    string sgRNA = m.Groups[1].Value;
                    string pam = m.Groups[2].Value;
                    int pos = start + m.Index + 1;
                    int exonNum = GetExonNumber(pos);
                    double gc = CalculateGC(sgRNA);
                    bool rec = IsRecommended(sgRNA, gc);

                    candidates.Add(new sgRNACandidate
                    {
                        Index = idx++,
                        Sequence = sgRNA,
                        PAM = pam,
                        Position = pos,
                        PositionEnd = pos + sgRNALength + pam.Length - 1,
                        Strand = "+",
                        ExonNumber = exonNum,
                        GCContent = $"{gc:F0}%",
                        IsRecommended = rec
                    });
                }

                // Reverse strand (-)
                string rcSeq = ReverseComplement(cdsSeq);
                var revMatches = Regex.Matches(rcSeq, $"([ATCG]{{{sgRNALength}}})({pamRegex})");
                foreach (Match m in revMatches)
                {
                    string sgRNA = m.Groups[1].Value;
                    string pam = m.Groups[2].Value;
                    int rcPos = m.Index;
                    int originalPos = start + (cdsSeq.Length - rcPos - sgRNALength - pam.Length) + 1;
                    int exonNum = GetExonNumber(originalPos);
                    double gc = CalculateGC(sgRNA);
                    bool rec = IsRecommended(sgRNA, gc);

                    candidates.Add(new sgRNACandidate
                    {
                        Index = idx++,
                        Sequence = sgRNA,
                        PAM = pam,
                        Position = originalPos,
                        PositionEnd = originalPos + sgRNALength + pam.Length - 1,
                        Strand = "−",
                        ExonNumber = exonNum,
                        GCContent = $"{gc:F0}%",
                        IsRecommended = rec
                    });
                }
            }

            return candidates.OrderBy(c => c.Position).ToList();
        }

        private int GetExonNumber(int position)
        {
            foreach (var exon in exonList)
                if (position >= exon.Start && position <= exon.End)
                    return exon.ExonNumber;
            return 0;
        }

        private double CalculateGC(string seq)
        {
            if (string.IsNullOrEmpty(seq)) return 0;
            int gc = seq.Count(c => c == 'G' || c == 'C');
            return 100.0 * gc / seq.Length;
        }

        private bool IsRecommended(string sgRNA, double gc)
        {
            return sgRNA.StartsWith("G") && sgRNA.EndsWith("A") && gc >= 40 && gc <= 60;
        }

        private string ReverseComplement(string seq)
        {
            var complement = new Dictionary<char, char>
            {
                {'A', 'T'}, {'T', 'A'}, {'G', 'C'}, {'C', 'G'}
            };
            var sb = new StringBuilder();
            for (int i = seq.Length - 1; i >= 0; i--)
            {
                sb.Append(complement.TryGetValue(seq[i], out char c) ? c : 'N');
            }
            return sb.ToString();
        }

        #endregion

        #region Visualization

        private double _currentScale = 0.5;
        private const double MIN_SCALE = 0.01;
        private const double MAX_SCALE = 10.0;
        private const double MARGIN = 30;
        private const double GENE_Y = 30;
        private const double SGRNA_Y_PLUS = 55;
        private const double SGRNA_Y_MINUS = 70;
        private const double FIXED_INTRON_WIDTH = 20;

        private List<(int GenomicStart, int GenomicEnd, double VisualStart, double VisualEnd)> _exonMapping
            = new List<(int, int, double, double)>();

        private bool _isInitialDraw = true;

        private void UpdateGeneVisualization()
        {
            GeneVisualizationCanvas.Children.Clear();
            EmptyVisualizationText.Visibility = Visibility.Collapsed;
            _exonMapping.Clear();
            sgRNAEllipses.Clear();

            if (exonList.Count == 0 && cdsList.Count == 0) return;

            double minExonWidth = 30;
            // maxExonWidth 隨 scale 動態調整，確保 zoom 有效果
            double maxExonWidth = Math.Max(200, exonList.Max(e => e.Length) * _currentScale);

            // 只在初次繪製時計算 scale，zoom 時保留使用者設定的 _currentScale
            if (_isInitialDraw)
            {
                int minExonLength = exonList.Count > 0 ? exonList.Min(e => e.Length) : 100;
                _currentScale = minExonWidth / Math.Max(minExonLength, 1);
                _currentScale = Math.Max(MIN_SCALE, Math.Min(MAX_SCALE, _currentScale));
                _defaultScale = _currentScale;
                _isInitialDraw = false;
                maxExonWidth = Math.Max(200, exonList.Max(e => e.Length) * _currentScale);
            }

            double totalExonWidth = exonList.Sum(e => Math.Min(Math.Max(e.Length * _currentScale, minExonWidth), maxExonWidth));
            double totalIntronWidth = Math.Max(0, exonList.Count - 1) * FIXED_INTRON_WIDTH;
            double canvasWidth = 2 * MARGIN + totalExonWidth + totalIntronWidth;

            double viewportWidth = Math.Max(GeneVisualizationScrollViewer?.ViewportWidth ?? 800, 600);
            canvasWidth = Math.Max(canvasWidth, viewportWidth);

            GeneVisualizationCanvas.Width = canvasWidth;
            GeneVisualizationCanvas.Height = 90;

            double currentX = MARGIN;
            var sortedExons = exonList.OrderBy(e => e.Start).ToList();

            for (int i = 0; i < sortedExons.Count; i++)
            {
                var exon = sortedExons[i];
                double exonWidth = Math.Min(Math.Max(exon.Length * _currentScale, minExonWidth), maxExonWidth);

                _exonMapping.Add((exon.Start, exon.End, currentX, currentX + exonWidth));

                if (i > 0)
                {
                    double prevExonEnd = currentX - FIXED_INTRON_WIDTH;
                    double midX = prevExonEnd + FIXED_INTRON_WIDTH / 2;
                    var intronLine1 = new Line { X1 = prevExonEnd, Y1 = GENE_Y, X2 = midX, Y2 = GENE_Y + 8, Stroke = Brushes.Gray, StrokeThickness = 1 };
                    var intronLine2 = new Line { X1 = midX, Y1 = GENE_Y + 8, X2 = currentX, Y2 = GENE_Y, Stroke = Brushes.Gray, StrokeThickness = 1 };
                    GeneVisualizationCanvas.Children.Add(intronLine1);
                    GeneVisualizationCanvas.Children.Add(intronLine2);
                }

                var exonRect = new Rectangle
                {
                    Width = exonWidth,
                    Height = 18,
                    Fill = new SolidColorBrush(Color.FromRgb(200, 230, 201)),
                    Stroke = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    StrokeThickness = 1,
                    RadiusX = 2,
                    RadiusY = 2,
                    ToolTip = $"Exon {exon.ExonNumber}: {exon.Start:N0}-{exon.End:N0} ({exon.Length:N0} bp)"
                };
                Canvas.SetLeft(exonRect, currentX);
                Canvas.SetTop(exonRect, GENE_Y - 9);
                GeneVisualizationCanvas.Children.Add(exonRect);

                if (exonWidth > 25)
                {
                    var lbl = new TextBlock { Text = $"E{exon.ExonNumber}", FontSize = 9, Foreground = Brushes.DarkGreen, FontWeight = FontWeights.SemiBold };
                    Canvas.SetLeft(lbl, currentX + 3);
                    Canvas.SetTop(lbl, GENE_Y - 7);
                    GeneVisualizationCanvas.Children.Add(lbl);
                }

                foreach (var cds in cdsList)
                {
                    int overlapStart = Math.Max(cds.Start, exon.Start);
                    int overlapEnd = Math.Min(cds.End, exon.End);

                    if (overlapStart <= overlapEnd)
                    {
                        double cdsRelStart = (double)(overlapStart - exon.Start) / exon.Length;
                        double cdsRelEnd = (double)(overlapEnd - exon.Start + 1) / exon.Length;

                        double cdsVisualStart = currentX + cdsRelStart * exonWidth;
                        double cdsVisualWidth = (cdsRelEnd - cdsRelStart) * exonWidth;
                        cdsVisualWidth = Math.Max(cdsVisualWidth, 3);

                        var cdsRect = new Rectangle
                        {
                            Width = cdsVisualWidth,
                            Height = 10,
                            Fill = new SolidColorBrush(Color.FromRgb(255, 183, 77)),
                            Stroke = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                            StrokeThickness = 1,
                            RadiusX = 2,
                            RadiusY = 2,
                            ToolTip = $"CDS: {overlapStart:N0}-{overlapEnd:N0}"
                        };
                        Canvas.SetLeft(cdsRect, cdsVisualStart);
                        Canvas.SetTop(cdsRect, GENE_Y - 5);
                        GeneVisualizationCanvas.Children.Add(cdsRect);
                    }
                }

                currentX += exonWidth + FIXED_INTRON_WIDTH;
            }

            GeneInfoLabel.Text = $"{currentGeneSymbol} | {currentAccession} | {genomicSequence.Length:N0} bp | {exonList.Count} exons | CDS: {cdsList.Sum(c => c.Length):N0} bp";
        }

        private double GenomicPosToVisualX(int genomicPos)
        {
            foreach (var mapping in _exonMapping)
            {
                if (genomicPos >= mapping.GenomicStart && genomicPos <= mapping.GenomicEnd)
                {
                    double relativePos = (double)(genomicPos - mapping.GenomicStart) / (mapping.GenomicEnd - mapping.GenomicStart + 1);
                    return mapping.VisualStart + relativePos * (mapping.VisualEnd - mapping.VisualStart);
                }
            }

            for (int i = 0; i < _exonMapping.Count - 1; i++)
            {
                if (genomicPos > _exonMapping[i].GenomicEnd && genomicPos < _exonMapping[i + 1].GenomicStart)
                {
                    return (_exonMapping[i].VisualEnd + _exonMapping[i + 1].VisualStart) / 2;
                }
            }

            return MARGIN;
        }

        private void UpdateVisualizationWithsgRNAs()
        {
            foreach (var el in sgRNAEllipses.Values)
                GeneVisualizationCanvas.Children.Remove(el);
            sgRNAEllipses.Clear();

            if (sgRNACandidates.Count == 0) return;

            var plusLabel = new TextBlock { Text = "+", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray };
            Canvas.SetLeft(plusLabel, 8);
            Canvas.SetTop(plusLabel, SGRNA_Y_PLUS - 2);
            GeneVisualizationCanvas.Children.Add(plusLabel);

            var minusLabel = new TextBlock { Text = "−", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.Gray };
            Canvas.SetLeft(minusLabel, 8);
            Canvas.SetTop(minusLabel, SGRNA_Y_MINUS - 2);
            GeneVisualizationCanvas.Children.Add(minusLabel);

            foreach (var sg in sgRNACandidates)
            {
                double x = GenomicPosToVisualX(sg.Position);
                double y = sg.Strand == "+" ? SGRNA_Y_PLUS : SGRNA_Y_MINUS;

                var ellipse = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = new SolidColorBrush(sg.IsRecommended ? Color.FromRgb(255, 152, 0) : Color.FromRgb(33, 150, 243)),
                    Stroke = Brushes.White,
                    StrokeThickness = 1,
                    Cursor = Cursors.Hand,
                    ToolTip = $"#{sg.Index} | Pos: {sg.Position:N0} | E{sg.ExonNumber} | {sg.Sequence}-{sg.PAM} ({sg.Strand}) | GC: {sg.GCContent}{(sg.IsRecommended ? " ⭐" : "")}"
                };

                ellipse.MouseLeftButtonDown += (s, e) =>
                {
                    sgRNADataGrid.SelectedItem = sg;
                    sgRNADataGrid.ScrollIntoView(sg);
                };

                Canvas.SetLeft(ellipse, x - 3);
                Canvas.SetTop(ellipse, y);
                GeneVisualizationCanvas.Children.Add(ellipse);
                sgRNAEllipses[sg.Index] = ellipse;
            }

            UpdateSelectionStatus();
        }

        private void HighlightSgRNAEllipse(int idx)
        {
            foreach (var kvp in sgRNAEllipses)
            {
                var sg = sgRNACandidates.FirstOrDefault(c => c.Index == kvp.Key);
                if (sg != null)
                {
                    kvp.Value.Fill = new SolidColorBrush(sg.IsRecommended ? Color.FromRgb(255, 152, 0) : Color.FromRgb(33, 150, 243));
                    kvp.Value.StrokeThickness = 1;
                    kvp.Value.Width = 6;
                    kvp.Value.Height = 6;
                }
            }
            if (sgRNAEllipses.TryGetValue(idx, out var sel))
            {
                sel.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                sel.StrokeThickness = 2;
                sel.Width = 10;
                sel.Height = 10;

                var sg = sgRNACandidates.FirstOrDefault(c => c.Index == idx);
                if (sg != null)
                {
                    double x = GenomicPosToVisualX(sg.Position);
                    if (GeneVisualizationScrollViewer != null)
                    {
                        double viewportCenter = GeneVisualizationScrollViewer.ViewportWidth / 2;
                        double targetOffset = Math.Max(0, x - viewportCenter);
                        GeneVisualizationScrollViewer.ScrollToHorizontalOffset(targetOffset);
                    }
                }
            }
        }

        private void sgRNADataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sgRNADataGrid.SelectedItem is sgRNACandidate c)
                HighlightSgRNAEllipse(c.Index);
        }

        #region Zoom Controls

        private double _defaultScale = 0.01;

        private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomVisualization(1.5);
        private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomVisualization(1.0 / 1.5);

        private void ZoomResetButton_Click(object sender, RoutedEventArgs e)
        {
            _currentScale = _defaultScale;
            RedrawVisualization();
        }

        private void GeneVisualizationScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                ZoomVisualization(e.Delta > 0 ? 1.2 : 1.0 / 1.2);
                e.Handled = true;
            }
            else
            {
                var sv = sender as ScrollViewer;
                sv?.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void ZoomVisualization(double factor)
        {
            if (string.IsNullOrEmpty(genomicSequence) || exonList.Count == 0) return;

            double newScale = Math.Max(MIN_SCALE, Math.Min(MAX_SCALE, _currentScale * factor));
            if (Math.Abs(newScale - _currentScale) < 0.001) return;

            double scrollRatio = 0.5;
            if (GeneVisualizationScrollViewer != null && GeneVisualizationCanvas.Width > 0)
            {
                double viewCenter = GeneVisualizationScrollViewer.HorizontalOffset + GeneVisualizationScrollViewer.ViewportWidth / 2;
                scrollRatio = viewCenter / GeneVisualizationCanvas.Width;
            }

            _currentScale = newScale;
            RedrawVisualization();

            if (GeneVisualizationScrollViewer != null && GeneVisualizationCanvas.Width > 0)
            {
                double newCenter = GeneVisualizationCanvas.Width * scrollRatio;
                GeneVisualizationScrollViewer.ScrollToHorizontalOffset(Math.Max(0, newCenter - GeneVisualizationScrollViewer.ViewportWidth / 2));
            }
        }

        private void RedrawVisualization()
        {
            UpdateGeneVisualization();
            UpdateVisualizationWithsgRNAs();
        }

        #endregion

        #endregion

        #region BLAST

        private void BlastSingle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is sgRNACandidate c)
                OpenBlastInBrowser(new[] { c });
        }

        private void BlastAllButton_Click(object sender, RoutedEventArgs e)
        {
            var sel = sgRNACandidates.Where(c => c.IsSelected).ToList();
            if (sel.Count == 0) sel = sgRNADataGrid.SelectedItems.Cast<sgRNACandidate>().ToList();
            if (sel.Count == 0) { ShowInfo("Select sgRNAs first.", true); return; }
            OpenBlastInBrowser(sel);
        }

        private void OpenBlastInBrowser(IEnumerable<sgRNACandidate> candidates)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var c in candidates)
                {
                    sb.AppendLine($">sgRNA_{c.Index}_{currentGeneSymbol}_E{c.ExonNumber}_{c.Strand}");
                    sb.AppendLine(c.Sequence);
                }

                string query = Uri.EscapeDataString(sb.ToString());
                string taxQ = Uri.EscapeDataString($"txid{currentTaxId}[Organism]");
                string blastUrl = $"https://blast.ncbi.nlm.nih.gov/Blast.cgi?PROGRAM=blastn&PAGE_TYPE=BlastSearch&DATABASE=refseq_genomes&QUERY={query}&ENTREZ_QUERY={taxQ}&WORD_SIZE=7";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = blastUrl, UseShellExecute = true });
                ShowInfo($"BLAST opened for {candidates.Count()} sgRNA(s)", false);
            }
            catch (Exception ex)
            {
                ShowInfo($"Error: {ex.Message}", true);
            }
        }

        #endregion

        #region Export

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            if (sgRNACandidates.Count == 0) return;
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "CSV|*.csv", FileName = $"sgRNA_{currentGeneSymbol}_{DateTime.Now:yyyyMMdd}" };
            if (dlg.ShowDialog() == true)
            {
                var sb = new StringBuilder("Index,Position,Exon,Sequence,PAM,Strand,GC%,Recommended,+1_Frameshift,+2_Frameshift\n");
                foreach (var c in sgRNACandidates)
                    sb.AppendLine($"{c.Index},{c.Position}-{c.PositionEnd},{c.ExonNumber},{c.Sequence},{c.PAM},{c.Strand},{c.GCContent},{c.IsRecommended},{c.FrameshiftPlus1 ?? ""},{c.FrameshiftPlus2 ?? ""}");
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString());
                ShowInfo("Exported", false);
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var sel = sgRNACandidates.Where(c => c.IsSelected).ToList();
            if (sel.Count == 0) sel = sgRNADataGrid.SelectedItems.Cast<sgRNACandidate>().ToList();
            if (sel.Count == 0) { ShowInfo("Select first", true); return; }

            var sb = new StringBuilder("#\tPos\tE\tSequence\tPAM\t±\t⭐\t+1 ins\t+2 ins\n");
            foreach (var c in sel)
                sb.AppendLine($"{c.Index}\t{c.Position}-{c.PositionEnd}\t{c.ExonNumber}\t{c.Sequence}\t{c.PAM}\t{c.Strand}\t{(c.IsRecommended ? "⭐" : "")}\t{c.FrameshiftPlus1 ?? ""}\t{c.FrameshiftPlus2 ?? ""}");
            Clipboard.SetText(sb.ToString());
            ShowInfo($"Copied {sel.Count}", false);
        }

        #endregion

        #region Frameshift Analysis

        private string _cdsSequence = "";
        private int _originalProteinLength = 0;

        private void BuildCDSSequence()
        {
            if (cdsList.Count == 0 || string.IsNullOrEmpty(genomicSequence))
            {
                _cdsSequence = "";
                _originalProteinLength = 0;
                return;
            }

            var sb = new StringBuilder();
            foreach (var cds in cdsList.OrderBy(c => c.Start))
            {
                int start = cds.Start - 1;
                int length = cds.Length;
                if (start >= 0 && start + length <= genomicSequence.Length)
                    sb.Append(genomicSequence.Substring(start, length));
            }
            _cdsSequence = sb.ToString();
            _originalProteinLength = _cdsSequence.Length / 3;
            if (_originalProteinLength > 0) _originalProteinLength--;
        }

        private int GenomicToCdsPosition(int genomicPos)
        {
            int cdsPos = 0;
            foreach (var cds in cdsList.OrderBy(c => c.Start))
            {
                if (genomicPos < cds.Start) return -1;
                else if (genomicPos <= cds.End) return cdsPos + (genomicPos - cds.Start);
                else cdsPos += cds.Length;
            }
            return -1;
        }

        private static readonly HashSet<string> StopCodons = new HashSet<string> { "TAA", "TAG", "TGA" };

        private int CalculateFrameshiftProteinLength(string cdsSequence, int insertionSite, int insertionCount)
        {
            if (insertionSite < 0 || insertionSite >= cdsSequence.Length) return -1;

            int codonsBefore = insertionSite / 3;
            string insertion = new string('N', insertionCount);
            string mutatedSeq = cdsSequence.Substring(0, insertionSite) + insertion + cdsSequence.Substring(insertionSite);

            int startCodon = codonsBefore;
            int newProteinLength = startCodon;

            for (int i = startCodon * 3; i + 2 < mutatedSeq.Length; i += 3)
            {
                string codon = mutatedSeq.Substring(i, 3).ToUpper();
                if (codon.Contains('N')) { newProteinLength++; continue; }
                if (StopCodons.Contains(codon)) return newProteinLength;
                newProteinLength++;
            }

            return newProteinLength;
        }

        private int CalculateCleavageSiteInCDS(sgRNACandidate sg)
        {
            int pamStart = sg.Strand == "+" ? sg.Position + sg.Sequence.Length - 1 : sg.Position;
            int cleavageSite = sg.Strand == "+" ? pamStart - 3 : pamStart + sg.PAM.Length + 3 - 1;
            return GenomicToCdsPosition(cleavageSite);
        }

        private async void FrameshiftAnalysisButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedSgRNAs = sgRNACandidates.Where(c => c.IsSelected).ToList();
            if (selectedSgRNAs.Count == 0)
            {
                ShowInfo("Please select sgRNAs (check ✓) for frameshift analysis.", true);
                return;
            }

            try
            {
                AnalysisProgressPanel.Visibility = Visibility.Visible;
                AnalysisProgressBar.Value = 0;
                AnalysisProgressText.Text = "0%";
                FrameshiftAnalysisButton.IsEnabled = false;

                BuildCDSSequence();

                if (string.IsNullOrEmpty(_cdsSequence))
                {
                    ShowInfo("Cannot build CDS sequence.", true);
                    return;
                }

                int total = selectedSgRNAs.Count;
                int processed = 0;

                await Task.Run(() =>
                {
                    foreach (var sg in selectedSgRNAs)
                    {
                        int cleavageSiteInCDS = CalculateCleavageSiteInCDS(sg);
                        sg.CleavageSite = cleavageSiteInCDS;

                        string plus1Result = "-";
                        string plus2Result = "-";

                        if (cleavageSiteInCDS >= 0 && cleavageSiteInCDS < _cdsSequence.Length)
                        {
                            int newLen1 = CalculateFrameshiftProteinLength(_cdsSequence, cleavageSiteInCDS, 1);
                            if (newLen1 >= 0)
                            {
                                int reduction1 = _originalProteinLength - newLen1;
                                plus1Result = $"-{reduction1}aa ({100.0 * reduction1 / _originalProteinLength:F0}%)";
                            }

                            int newLen2 = CalculateFrameshiftProteinLength(_cdsSequence, cleavageSiteInCDS, 2);
                            if (newLen2 >= 0)
                            {
                                int reduction2 = _originalProteinLength - newLen2;
                                plus2Result = $"-{reduction2}aa ({100.0 * reduction2 / _originalProteinLength:F0}%)";
                            }
                        }

                        Dispatcher.Invoke(() =>
                        {
                            sg.FrameshiftPlus1 = plus1Result;
                            sg.FrameshiftPlus2 = plus2Result;
                            processed++;
                            int percent = (int)(100.0 * processed / total);
                            AnalysisProgressBar.Value = percent;
                            AnalysisProgressText.Text = $"{percent}%";
                        });

                        System.Threading.Thread.Sleep(10);
                    }
                });

                ShowInfo($"Frameshift analysis completed for {selectedSgRNAs.Count} sgRNAs. Original protein: {_originalProteinLength} aa", false);
            }
            catch (Exception ex)
            {
                ShowInfo($"Analysis error: {ex.Message}", true);
            }
            finally
            {
                AnalysisProgressPanel.Visibility = Visibility.Collapsed;
                FrameshiftAnalysisButton.IsEnabled = true;
            }
        }

        private void UpdateSelectionStatus()
        {
            int count = sgRNACandidates.Count(c => c.IsSelected);
            SelectedCountText.Text = count > 0 ? $"({count} selected)" : "";
            FrameshiftAnalysisButton.IsEnabled = sgRNACandidates.Count > 0;
        }

        #endregion

        #region Helpers

        private void ShowLoading(bool show, string msg = "")
        {
            LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            LoadingText.Text = msg;
        }

        private void ShowProgress(bool show, string msg = "")
        {
            AnalysisProgressPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            AnalysisProgressText.Text = msg;
        }

        private void ShowInfo(string msg, bool err)
        {
            InfoBar.Visibility = Visibility.Visible;
            InfoBar.Background = new SolidColorBrush(err ? Color.FromRgb(255, 235, 238) : Color.FromRgb(227, 242, 253));
            InfoIcon.Text = err ? "⚠️" : "✅";
            InfoText.Text = msg;
            InfoText.Foreground = new SolidColorBrush(err ? Color.FromRgb(198, 40, 40) : Color.FromRgb(21, 101, 192));
        }

        private void ClearResults()
        {
            sgRNACandidates.Clear();
            exonList.Clear();
            cdsList.Clear();
            genomicSequence = currentAccession = "";
            sgRNAEllipses.Clear();
            GeneVisualizationCanvas.Children.Clear();
            EmptyVisualizationText.Visibility = EmptyListText.Visibility = Visibility.Visible;
            sgRNACountLabel.Text = GeneInfoLabel.Text = "";
            InputSectionSummary.Text = "";
            SearchResultHint.Text = "";
            InfoBar.Visibility = Visibility.Collapsed;
            AnalyzeButton.IsEnabled = ExportButton.IsEnabled = CopyButton.IsEnabled = BlastAllButton.IsEnabled = false;
            SelectAllCheckBox.IsChecked = false;
            _isInitialDraw = true;
        }

        #endregion
    }

    #region Models

    public class sgRNACandidate : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _frameshiftPlus1;
        private string _frameshiftPlus2;

        public int Index { get; set; }
        public string Sequence { get; set; }
        public string PAM { get; set; }
        public int Position { get; set; }
        public int PositionEnd { get; set; }
        public string Strand { get; set; }
        public int ExonNumber { get; set; }
        public string GCContent { get; set; }
        public bool IsRecommended { get; set; }
        public int CleavageSite { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string FrameshiftPlus1
        {
            get => _frameshiftPlus1;
            set { _frameshiftPlus1 = value; OnPropertyChanged(); }
        }

        public string FrameshiftPlus2
        {
            get => _frameshiftPlus2;
            set { _frameshiftPlus2 = value; OnPropertyChanged(); }
        }

        public string PositionDisplay => $"{Position:N0}-{PositionEnd:N0}";
        public string ExonDisplay => ExonNumber > 0 ? $"{ExonNumber}" : "-";
        public string RecommendDisplay => IsRecommended ? "⭐" : "";

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public class ExonInfo
    {
        public int ExonNumber { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public int Length { get; set; }
    }

    public class CDSRegion
    {
        public int PartNumber { get; set; }
        public int Start { get; set; }
        public int End { get; set; }
        public int Length { get; set; }
    }

    #endregion
}
