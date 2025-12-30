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

namespace BioSAK.Pages
{
    public partial class sgRNADesignerPage : Page
    {
        private static readonly HttpClient httpClient = new HttpClient();
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

        public sgRNADesignerPage()
        {
            InitializeComponent();
            sgRNADataGrid.ItemsSource = sgRNACandidates;
            
            httpClient.Timeout = TimeSpan.FromSeconds(30);
            if (!httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                httpClient.DefaultRequestHeaders.Add("User-Agent", "BioSAK/1.0 (contact@example.com)");
            }
        }

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

                string accession = AccessionTextBox.Text.Trim();
                string geneSymbol = GeneSymbolTextBox.Text.Trim();
                currentTaxId = GetSelectedTaxId();

                if (!string.IsNullOrEmpty(accession))
                {
                    ShowLoading(true, $"Fetching {accession}...");
                    await FetchByAccession(accession);
                }
                else if (!string.IsNullOrEmpty(geneSymbol))
                {
                    ShowLoading(true, $"Searching {geneSymbol}...");
                    await FetchByGeneSymbol(geneSymbol, currentTaxId);
                }
                else
                {
                    ShowInfo("Enter gene symbol or accession.", true);
                    return;
                }

                AnalyzeButton.IsEnabled = cdsList.Count > 0 && !string.IsNullOrEmpty(genomicSequence);
                
                // Update summary and collapse
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

        private async Task FetchByGeneSymbol(string geneSymbol, string taxId)
        {
            // Search Gene database
            string geneSearchUrl = $"{NCBI_ESEARCH_URL}?db=gene&term={Uri.EscapeDataString(geneSymbol)}[Gene Name]+AND+{taxId}[Taxonomy ID]&retmode=xml";
            
            var geneSearchResponse = await httpClient.GetStringAsync(geneSearchUrl);
            var geneXml = System.Xml.Linq.XDocument.Parse(geneSearchResponse);
            var geneIds = geneXml.Descendants("Id").Select(x => x.Value).ToList();

            if (geneIds.Count == 0)
            {
                // Try symbol search
                geneSearchUrl = $"{NCBI_ESEARCH_URL}?db=gene&term={Uri.EscapeDataString(geneSymbol)}[sym]+AND+{taxId}[Taxonomy ID]&retmode=xml";
                geneSearchResponse = await httpClient.GetStringAsync(geneSearchUrl);
                geneXml = System.Xml.Linq.XDocument.Parse(geneSearchResponse);
                geneIds = geneXml.Descendants("Id").Select(x => x.Value).ToList();
            }

            if (geneIds.Count == 0)
            {
                ShowInfo($"Gene '{geneSymbol}' not found. Try accession.", true);
                return;
            }

            string geneId = geneIds.First();
            ShowLoading(true, $"Gene ID: {geneId}, finding RefSeqGene...");

            // Use elink to find RefSeqGene
            string elinkUrl = $"{NCBI_ELINK_URL}?dbfrom=gene&db=nuccore&id={geneId}&linkname=gene_nuccore_refseqgene&retmode=xml";
            
            var elinkResponse = await httpClient.GetStringAsync(elinkUrl);
            var elinkXml = System.Xml.Linq.XDocument.Parse(elinkResponse);
            var nucIds = elinkXml.Descendants("Link").Select(x => x.Element("Id")?.Value).Where(x => x != null).ToList();

            if (nucIds.Count == 0)
            {
                // Direct search
                string nucSearchUrl = $"{NCBI_ESEARCH_URL}?db=nuccore&term=NG_[Accession]+AND+{Uri.EscapeDataString(geneSymbol)}[Gene]+AND+{taxId}[Taxonomy ID]+AND+RefSeqGene[Keyword]&retmax=5&retmode=xml";
                var nucSearchResponse = await httpClient.GetStringAsync(nucSearchUrl);
                var nucXml = System.Xml.Linq.XDocument.Parse(nucSearchResponse);
                nucIds = nucXml.Descendants("Id").Select(x => x.Value).ToList();
            }

            if (nucIds.Count == 0)
            {
                ShowInfo($"No RefSeqGene for '{geneSymbol}'. Try accession (NG_...).", true);
                return;
            }

            await FetchAndParseGenBank(nucIds.First());
        }

        private async Task FetchByAccession(string accession)
        {
            string searchUrl = $"{NCBI_ESEARCH_URL}?db=nuccore&term={accession}[Accession]&retmode=xml";
            var searchResponse = await httpClient.GetStringAsync(searchUrl);
            var searchXml = System.Xml.Linq.XDocument.Parse(searchResponse);
            var nucId = searchXml.Descendants("Id").FirstOrDefault()?.Value;
            
            if (string.IsNullOrEmpty(nucId))
            {
                ShowInfo($"Accession '{accession}' not found.", true);
                return;
            }

            await FetchAndParseGenBank(nucId);
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

            // Parse CDS
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

            // Parse exons
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
            sequence = Regex.Replace(sequence, @"[^ATCGatcg]", "");
            
            if (sequence.Length < 50)
            {
                ShowInfo("Enter longer sequence (50+ bp).", true);
                return;
            }

            try
            {
                ShowLoading(true, "Opening BLAST...");
                
                string query = sequence.Substring(0, Math.Min(sequence.Length, 500));
                string blastUrl = $"https://blast.ncbi.nlm.nih.gov/Blast.cgi?PROGRAM=blastn&PAGE_TYPE=BlastSearch&DATABASE=refseq_genomes&QUERY={Uri.EscapeDataString(query)}";
                
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = blastUrl,
                    UseShellExecute = true
                });

                ShowInfo("BLAST opened. Enter the gene accession after identifying.", false);
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
                foreach (var c in candidates) sgRNACandidates.Add(c);

                UpdateVisualizationWithsgRNAs();
                
                int recCount = sgRNACandidates.Count(c => c.IsRecommended);
                sgRNACountLabel.Text = $"{sgRNACandidates.Count} found, {recCount} ⭐";
                EmptyListText.Visibility = sgRNACandidates.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                
                ExportButton.IsEnabled = CopyButton.IsEnabled = BlastAllButton.IsEnabled = sgRNACandidates.Count > 0;
                
                ShowInfo($"Found {sgRNACandidates.Count} sgRNAs in CDS, {recCount} recommended", false);
                
                // Collapse input section after successful analysis
                CollapseInputSection();
                InputSectionSummary.Text = $"✓ {currentGeneSymbol} | {sgRNACandidates.Count} sgRNAs";
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

            // Forward strand
            foreach (Match match in Regex.Matches(sequence, pamRegex))
            {
                int pamStart = match.Index;
                if (pamStart >= sgRNALength)
                {
                    int sgStart = pamStart - sgRNALength;
                    int start1 = sgStart + 1;
                    int end1 = pamStart + match.Length;
                    
                    if (IsWithinCDS(start1, end1))
                    {
                        string sgRNA = sequence.Substring(sgStart, sgRNALength);
                        bool isRec = sgRNA.StartsWith("G") && sgRNA.EndsWith("A");
                        
                        candidates.Add(new sgRNACandidate
                        {
                            Index = idx++, Sequence = sgRNA, PAM = match.Value,
                            Position = start1, PositionEnd = end1, Strand = "+",
                            ExonNumber = GetExonNumber(start1), GCContent = CalcGC(sgRNA),
                            IsRecommended = isRec
                        });
                    }
                }
            }

            // Reverse strand
            string revComp = GetReverseComplement(sequence);
            foreach (Match match in Regex.Matches(revComp, pamRegex))
            {
                int pamStartRC = match.Index;
                if (pamStartRC >= sgRNALength)
                {
                    int sgStartRC = pamStartRC - sgRNALength;
                    int fwdEnd = sequence.Length - sgStartRC;
                    int fwdStart = sequence.Length - (pamStartRC + match.Length - 1);
                    
                    if (IsWithinCDS(fwdStart, fwdEnd))
                    {
                        string sgRNA = revComp.Substring(sgStartRC, sgRNALength);
                        bool isRec = sgRNA.StartsWith("G") && sgRNA.EndsWith("A");
                        
                        candidates.Add(new sgRNACandidate
                        {
                            Index = idx++, Sequence = sgRNA, PAM = match.Value,
                            Position = fwdStart, PositionEnd = fwdEnd, Strand = "-",
                            ExonNumber = GetExonNumber(fwdStart), GCContent = CalcGC(sgRNA),
                            IsRecommended = isRec
                        });
                    }
                }
            }

            return candidates.OrderBy(c => c.Position).ToList();
        }

        private bool IsWithinCDS(int start, int end) => cdsList.Any(cds => start >= cds.Start && end <= cds.End);
        private int GetExonNumber(int pos) => exonList.FirstOrDefault(e => pos >= e.Start && pos <= e.End)?.ExonNumber ?? 0;
        
        private string GetReverseComplement(string seq)
        {
            var sb = new StringBuilder();
            foreach (char c in seq.Reverse())
                sb.Append(c == 'A' ? 'T' : c == 'T' ? 'A' : c == 'G' ? 'C' : c == 'C' ? 'G' : c);
            return sb.ToString();
        }

        private string CalcGC(string seq) => string.IsNullOrEmpty(seq) ? "0%" : $"{(double)seq.Count(c => c == 'G' || c == 'C') / seq.Length * 100:F0}%";

        #endregion

        #region Visualization

        private void UpdateGeneVisualization()
        {
            GeneVisualizationCanvas.Children.Clear();
            sgRNAEllipses.Clear();
            EmptyVisualizationText.Visibility = Visibility.Collapsed;

            if (string.IsNullOrEmpty(genomicSequence)) return;

            double canvasWidth = Math.Max(800, GeneVisualizationCanvas.ActualWidth - 30);
            double geneLineY = 35, margin = 25;
            int totalLength = genomicSequence.Length;
            double scale = (canvasWidth - 2 * margin) / totalLength;

            GeneVisualizationCanvas.Width = canvasWidth;
            GeneVisualizationCanvas.Height = 90;

            // Backbone
            GeneVisualizationCanvas.Children.Add(new Line
            {
                X1 = margin, Y1 = geneLineY, X2 = canvasWidth - margin, Y2 = geneLineY,
                Stroke = new SolidColorBrush(Color.FromRgb(200, 200, 200)), StrokeThickness = 2
            });

            // Exons (light blue)
            foreach (var exon in exonList)
            {
                double x = margin + (exon.Start - 1) * scale;
                double w = Math.Max(exon.Length * scale, 3);
                var rect = new Rectangle
                {
                    Width = w, Height = 14,
                    Fill = new SolidColorBrush(Color.FromRgb(227, 242, 253)),
                    Stroke = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                    StrokeThickness = 1, RadiusX = 2, RadiusY = 2,
                    ToolTip = $"Exon {exon.ExonNumber}: {exon.Start:N0}-{exon.End:N0}"
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, geneLineY - 7);
                GeneVisualizationCanvas.Children.Add(rect);
            }

            // CDS (green)
            foreach (var cds in cdsList)
            {
                double x = margin + (cds.Start - 1) * scale;
                double w = Math.Max(cds.Length * scale, 3);
                var rect = new Rectangle
                {
                    Width = w, Height = 18,
                    Fill = new SolidColorBrush(Color.FromRgb(76, 175, 80)),
                    RadiusX = 2, RadiusY = 2,
                    ToolTip = $"CDS: {cds.Start:N0}-{cds.End:N0} ({cds.Length} bp)"
                };
                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, geneLineY - 9);
                GeneVisualizationCanvas.Children.Add(rect);
            }

            DrawScale(canvasWidth, margin, totalLength, scale);
            GeneInfoLabel.Text = $"{currentGeneSymbol} ({currentAccession}) | {genomicSequence.Length:N0} bp";
        }

        private void UpdateVisualizationWithsgRNAs()
        {
            foreach (var el in sgRNAEllipses.Values) GeneVisualizationCanvas.Children.Remove(el);
            sgRNAEllipses.Clear();

            if (sgRNACandidates.Count == 0) return;

            double canvasWidth = GeneVisualizationCanvas.Width;
            double margin = 25, sgRNABaseY = 58, rowHeight = 10;
            double scale = (canvasWidth - 2 * margin) / genomicSequence.Length;

            var rows = new List<List<double>>();

            foreach (var sg in sgRNACandidates)
            {
                double centerX = margin + ((sg.Position + sg.PositionEnd) / 2.0 - 1) * scale;
                double r = 4;

                int row = 0;
                for (; row < rows.Count; row++)
                    if (!rows[row].Any(x => Math.Abs(centerX - x) < r * 2.5)) break;
                if (row >= rows.Count) rows.Add(new List<double>());
                rows[row].Add(centerX);

                var ellipse = new Ellipse
                {
                    Width = r * 2, Height = r * 2,
                    Fill = new SolidColorBrush(sg.IsRecommended ? Color.FromRgb(255, 152, 0) : Color.FromRgb(33, 150, 243)),
                    Stroke = Brushes.White, StrokeThickness = 1,
                    Cursor = Cursors.Hand,
                    ToolTip = $"#{sg.Index} {sg.Sequence}\n{sg.PAM} ({sg.Strand})\nPos: {sg.Position}-{sg.PositionEnd}\n{(sg.IsRecommended ? "⭐ Recommended\n" : "")}Click to select",
                    Tag = sg.Index
                };
                ellipse.MouseLeftButtonDown += SgRNAEllipse_Click;
                
                Canvas.SetLeft(ellipse, centerX - r);
                Canvas.SetTop(ellipse, sgRNABaseY + row * rowHeight);
                GeneVisualizationCanvas.Children.Add(ellipse);
                sgRNAEllipses[sg.Index] = ellipse;
            }

            GeneVisualizationCanvas.Height = Math.Max(90, sgRNABaseY + rows.Count * rowHeight + 15);
        }

        private void SgRNAEllipse_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse el && el.Tag is int idx)
            {
                var candidate = sgRNACandidates.FirstOrDefault(c => c.Index == idx);
                if (candidate != null)
                {
                    sgRNADataGrid.SelectedItem = candidate;
                    sgRNADataGrid.ScrollIntoView(candidate);
                    HighlightSgRNAEllipse(idx);
                }
            }
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
                }
            }
            if (sgRNAEllipses.TryGetValue(idx, out var sel))
            {
                sel.Fill = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                sel.StrokeThickness = 2;
            }
        }

        private void sgRNADataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sgRNADataGrid.SelectedItem is sgRNACandidate c)
                HighlightSgRNAEllipse(c.Index);
        }

        private void DrawScale(double canvasWidth, double margin, int totalLength, double scale)
        {
            double scaleY = 78;
            GeneVisualizationCanvas.Children.Add(new Line
            {
                X1 = margin, Y1 = scaleY, X2 = canvasWidth - margin, Y2 = scaleY,
                Stroke = Brushes.LightGray, StrokeThickness = 1
            });

            int tick = totalLength <= 1000 ? 200 : totalLength <= 5000 ? 500 : 1000;
            for (int pos = 0; pos <= totalLength; pos += tick)
            {
                double x = margin + pos * scale;
                GeneVisualizationCanvas.Children.Add(new Line
                {
                    X1 = x, Y1 = scaleY, X2 = x, Y2 = scaleY + 3,
                    Stroke = Brushes.Gray, StrokeThickness = 1
                });
                var lbl = new TextBlock { Text = pos < 1000 ? pos.ToString() : $"{pos/1000.0:0.#}k", FontSize = 8, Foreground = Brushes.Gray };
                Canvas.SetLeft(lbl, x - 8);
                Canvas.SetTop(lbl, scaleY + 3);
                GeneVisualizationCanvas.Children.Add(lbl);
            }
        }

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
                var sb = new StringBuilder("Index,Position,Exon,Sequence,PAM,Strand,GC%,Recommended\n");
                foreach (var c in sgRNACandidates)
                    sb.AppendLine($"{c.Index},{c.Position}-{c.PositionEnd},{c.ExonNumber},{c.Sequence},{c.PAM},{c.Strand},{c.GCContent},{c.IsRecommended}");
                System.IO.File.WriteAllText(dlg.FileName, sb.ToString());
                ShowInfo("Exported", false);
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var sel = sgRNACandidates.Where(c => c.IsSelected).ToList();
            if (sel.Count == 0) sel = sgRNADataGrid.SelectedItems.Cast<sgRNACandidate>().ToList();
            if (sel.Count == 0) { ShowInfo("Select first", true); return; }
            
            var sb = new StringBuilder("#\tPos\tE\tSequence\tPAM\t±\t⭐\n");
            foreach (var c in sel)
                sb.AppendLine($"{c.Index}\t{c.Position}-{c.PositionEnd}\t{c.ExonNumber}\t{c.Sequence}\t{c.PAM}\t{c.Strand}\t{(c.IsRecommended ? "⭐" : "")}");
            Clipboard.SetText(sb.ToString());
            ShowInfo($"Copied {sel.Count}", false);
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
            ProgressPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ProgressText.Text = msg;
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
            InfoBar.Visibility = Visibility.Collapsed;
            AnalyzeButton.IsEnabled = ExportButton.IsEnabled = CopyButton.IsEnabled = BlastAllButton.IsEnabled = false;
        }

        #endregion
    }

    #region Models

    public class sgRNACandidate : INotifyPropertyChanged
    {
        private bool _isSelected;

        public int Index { get; set; }
        public string Sequence { get; set; }
        public string PAM { get; set; }
        public int Position { get; set; }
        public int PositionEnd { get; set; }
        public string Strand { get; set; }
        public int ExonNumber { get; set; }
        public string GCContent { get; set; }
        public bool IsRecommended { get; set; }

        public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }

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
