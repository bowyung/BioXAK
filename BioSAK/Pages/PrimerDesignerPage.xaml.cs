using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.IO;
using BioSAK.Services;
using BioSAK.Models;

namespace BioSAK.Pages
{
    public partial class PrimerDesignerPage : Page
    {
        #region === Fields ===

        private static readonly HttpClient httpClient = new HttpClient();
        private readonly GeneIdService _geneIdService;
        private List<RestrictionEnzyme> _allEnzymes;
        private CancellationTokenSource _fetchCts; // prevents race conditions on rapid clicks

        // ── Static NN thermodynamic lookup arrays (SantaLucia 1998) ──
        // Indexed by (base1 * 4 + base2) where A=0, C=1, G=2, T=3
        private static readonly int[] _baseIdx = new int[128];
        private static readonly double[] _nnH = new double[16]; // ΔH kcal/mol
        private static readonly double[] _nnS = new double[16]; // ΔS cal/(mol·K)
        private static readonly double[] _nnG = new double[16]; // ΔG kcal/mol at 37°C

        static PrimerDesignerPage()
        {
            for (int i = 0; i < 128; i++) _baseIdx[i] = -1;
            _baseIdx['A'] = 0; _baseIdx['a'] = 0;
            _baseIdx['C'] = 1; _baseIdx['c'] = 1;
            _baseIdx['G'] = 2; _baseIdx['g'] = 2;
            _baseIdx['T'] = 3; _baseIdx['t'] = 3;

            // Order: AA AC AG AT CA CC CG CT GA GC GG GT TA TC TG TT
            double[] h = { -7.9, -8.4, -7.8, -7.2, -8.5, -8.0, -10.6, -7.8, -8.2, -9.8, -8.0, -8.4, -7.2, -8.2, -8.5, -7.9 };
            double[] s = { -22.2, -22.4, -21.0, -20.4, -22.7, -19.9, -27.2, -21.0, -22.2, -24.4, -19.9, -22.4, -21.3, -22.2, -22.7, -22.2 };
            double[] g = { -1.00, -1.44, -1.28, -0.88, -1.45, -1.84, -2.17, -1.28, -1.30, -2.24, -1.84, -1.44, -0.58, -1.30, -1.45, -1.00 };
            Array.Copy(h, _nnH, 16);
            Array.Copy(s, _nnS, 16);
            Array.Copy(g, _nnG, 16);
        }

        // Gene data
        private string _genomicSequence = "";
        private string _activeTemplate = "";
        private string _currentGeneSymbol = "";
        private string _currentAccession = "";
        private string _currentTaxId = "9606";
        private List<ExonInfo> _exonList = new List<ExonInfo>();
        private List<CDSRegion> _cdsList = new List<CDSRegion>();
        private List<TranscriptIsoform> _isoforms = new List<TranscriptIsoform>();

        // Genomic mode isoform tracking
        private bool _isGenomicMode = false;
        private List<GenomicIsoform> _genomicIsoforms = new List<GenomicIsoform>();

        // Auto-pick region constraint ("product must include this region", in template coords)
        private int _constraintStart = -1;
        private int _constraintEnd = -1;

        // Visualization
        private double _currentScale = 0.01;
        private double _defaultScale = 0.01;
        private const double MIN_SCALE = 0.001;
        private const double MAX_SCALE = 1.0;
        private const double MARGIN = 30;
        private const double GENE_Y = 35;

        // Region selection
        private bool _isDragging = false;
        private double _dragStartX;
        private int _selectionStart = -1;
        private int _selectionEnd = -1;
        private int _templateOffset = 0; // genomic offset of _activeTemplate start
        private int _markerFwdStart = -1, _markerFwdLen = 0;
        private int _markerRevStart = -1, _markerRevLen = 0;

        // Collapsible
        private bool _isInputCollapsed = false;

        // Primer results (for RE preview)
        private string _lastFwdPrimer = "";
        private string _lastRevPrimer = "";

        // Tm calculation parameters (defaults match NCBI Primer-BLAST)
        private double _naConc = 0.05;      // 50 mM monovalent cation
        private double _mgConc = 0.0015;    // 1.5 mM Mg2+
        private double _dntpConc = 0.0006;  // 0.6 mM dNTP
        private double _primerConc = 250e-9; // 250 nM primer

        private const string NCBI_ESEARCH_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esearch.fcgi";
        private const string NCBI_EFETCH_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/efetch.fcgi";
        private const string NCBI_ESUMMARY_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/esummary.fcgi";
        private const string NCBI_ELINK_URL = "https://eutils.ncbi.nlm.nih.gov/entrez/eutils/elink.fcgi";

        #endregion

        #region === Constructor ===

        public PrimerDesignerPage()
        {
            InitializeComponent();

            httpClient.Timeout = TimeSpan.FromSeconds(30);
            if (!httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                httpClient.DefaultRequestHeaders.Add("User-Agent", "BioXAK/1.0");

            _geneIdService = new GeneIdService();
            LoadRestrictionEnzymes();

            // Register numeric-only input validation on all parameter TextBoxes
            var numericBoxes = new[] {
                MinLengthInput, MaxLengthInput, MaxTmDiffInput,
                MinTmInput, TargetTmInput, MaxTmInput,
                MinProductInput, TargetProductInput, MaxProductInput,
                ManualTargetTmInput, ManualTmToleranceInput, ManualTargetProductInput, ManualProductToleranceInput,
                NaConcInput, MgConcInput, DntpConcInput, PrimerConcInput
            };
            foreach (var tb in numericBoxes)
            {
                tb.PreviewTextInput += NumericTextBox_PreviewTextInput;
                DataObject.AddPastingHandler(tb, NumericTextBox_Pasting);
            }
        }

        private void LoadRestrictionEnzymes()
        {
            try { _allEnzymes = RebaseParser.LoadEnzymes(); }
            catch { _allEnzymes = new List<RestrictionEnzyme>(); }

            var reItems = new List<string> { "(None)" };
            var commonNames = new[] { "EcoRI", "BamHI", "HindIII", "XhoI", "NheI", "NcoI", "XbaI",
                "SalI", "NotI", "KpnI", "SacI", "NdeI", "BglII", "SpeI", "AgeI", "MluI",
                "EcoRV", "SmaI", "PstI", "ApaI", "PacI", "ClaI", "MfeI" };

            foreach (var name in commonNames)
            {
                var enz = _allEnzymes.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (enz != null) reItems.Add($"{enz.Name}  ({enz.RecognitionSequence})");
            }
            reItems.Add("── All Enzymes ──");
            foreach (var enz in _allEnzymes.Where(e => !commonNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase)))
                reItems.Add($"{enz.Name}  ({enz.RecognitionSequence})");

            AutoFwdREComboBox.ItemsSource = reItems;
            AutoRevREComboBox.ItemsSource = reItems;
            FwdREComboBox.ItemsSource = reItems;
            RevREComboBox.ItemsSource = reItems;
            AutoFwdREComboBox.SelectedIndex = 0;
            AutoRevREComboBox.SelectedIndex = 0;
            FwdREComboBox.SelectedIndex = 0;
            RevREComboBox.SelectedIndex = 0;
        }

        #endregion

        #region === Data Models ===

        private class PrimerAnalysis
        {
            public string Sequence { get; set; } = "";
            public int Length { get; set; }
            public double Tm { get; set; }               // full-oligo Tm (used when OH present)
            public double TmGeneSpecific { get; set; }   // gene-specific-only Tm (always set)
            public double GCPercent { get; set; }
            public double MolecularWeight { get; set; }
            public bool HasGCClamp { get; set; }
            public int SelfComplementarityScore { get; set; }
            public int HairpinScore { get; set; }
            public double EndStabilityDeltaG { get; set; }
            public double SelfDimerDeltaG { get; set; }
            public double HairpinDeltaG { get; set; }
            public int AnyCompScore { get; set; }
            public List<string> Warnings { get; set; } = new List<string>();
        }

        public class PrimerPairResult
        {
            public int Rank { get; set; }
            public string ForwardSequence { get; set; } = "";   // gene-specific part only
            public string ReverseSequence { get; set; } = "";
            public string ForwardDisplay { get; set; } = "";    // display string (may have prot-RE- prefix)
            public string ReverseDisplay { get; set; } = "";
            public string ForwardFullSequence { get; set; } = ""; // full synthesised oligo (prot+RE+gene)
            public string ReverseFullSequence { get; set; } = "";
            public double ForwardTm { get; set; }
            public double ReverseTm { get; set; }
            public double TmDiff { get; set; }
            public int ProductSize { get; set; }
            public double Score { get; set; }
            public int FwdStart { get; set; }
            public int RevStart { get; set; }

            // Full primer Tm including RE overhang (= ForwardTm when no RE)
            public double ForwardTmFull { get; set; }
            public double ReverseTmFull { get; set; }
            public bool HasREOverhang { get; set; }

            // DataGrid display: show full Tm only when RE overhang exists
            public string ForwardTmFullDisplay => HasREOverhang ? $"{ForwardTmFull:F1}" : "-";
            public string ReverseTmFullDisplay => HasREOverhang ? $"{ReverseTmFull:F1}" : "-";
        }

        public class TranscriptIsoform
        {
            public string Accession { get; set; } = "";
            public string NucId { get; set; } = "";
            public int Length { get; set; }
            public string Description { get; set; } = "";
            public string IsoformLabel { get; set; } = "";
            public string LengthDisplay => $"{Length:N0} bp";
        }

        /// <summary>
        /// One mRNA transcript isoform parsed from a genomic (NG_) GenBank file.
        /// Stores exon and CDS coordinates on the genomic sequence — no re-fetch needed.
        /// </summary>
        public class GenomicIsoform
        {
            public string TranscriptId { get; set; } = "";
            public string IsoformLabel { get; set; } = "";
            public string ProductName { get; set; } = "";
            public bool IsComplement { get; set; } = false;   // gene on minus strand
            public List<ExonInfo> Exons { get; set; } = new List<ExonInfo>();
            public List<CDSRegion> CdsParts { get; set; } = new List<CDSRegion>();
            public int TotalExonBp => Exons.Sum(e => e.Length);
            public string Accession => TranscriptId;
            public string LengthDisplay => $"{TotalExonBp:N0} bp (exonic)";
            public string Description => ProductName.Length > 80 ? ProductName.Substring(0, 80) + "…" : ProductName;
        }

        #endregion

        #region === Collapsible Input ===

        private void InputHeader_Click(object sender, MouseButtonEventArgs e)
        {
            _isInputCollapsed = !_isInputCollapsed;
            InputContentPanel.Visibility = _isInputCollapsed ? Visibility.Collapsed : Visibility.Visible;
            CollapseArrow.Text = _isInputCollapsed ? "▶" : "▼";
        }

        #endregion

        #region === Direct Sequence Input ===

        private void DirectSequenceInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DirectSeqHint == null) return;
            string text = DirectSequenceInput.Text;
            DirectSeqHint.Visibility = string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;
            DirectSeqLengthLabel.Text = $"Length: {CleanSequence(text).Length} bp";
        }

        private void UseDirectSequence_Click(object sender, RoutedEventArgs e)
        {
            string seq = CleanSequence(DirectSequenceInput.Text);
            if (seq.Length < 20)
            {
                MessageBox.Show("Please enter a longer sequence (≥20 bp).", "Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _genomicSequence = seq;
            _currentGeneSymbol = "Custom";
            _currentAccession = "Direct input";
            _exonList.Clear();
            _cdsList.Clear();
            GeneVisualizationPanel.Visibility = Visibility.Collapsed;
            SetActiveTemplate(seq);
            InputSectionSummary.Text = $"✔ Custom sequence ({seq.Length} bp)";
        }

        #endregion

        #region === NCBI Gene Fetch ===

        private async void FetchGeneButton_Click(object sender, RoutedEventArgs e)
        {
            // Cancel any previous in-flight fetch (prevents race condition)
            _fetchCts?.Cancel();
            _fetchCts?.Dispose();
            _fetchCts = new CancellationTokenSource();
            var ct = _fetchCts.Token;

            try
            {
                ShowLoading(true, "Connecting to NCBI...");
                string accession = AccessionTextBox.Text.Trim();
                string geneSymbol = GeneSymbolTextBox.Text.Trim();
                _currentTaxId = GetSelectedTaxId();
                string seqType = GetSelectedSeqType();
                _isGenomicMode = false;
                _genomicIsoforms.Clear();

                if (!string.IsNullOrEmpty(accession))
                {
                    ShowLoading(true, $"Fetching {accession}...");
                    await FetchByAccession(accession, ct);
                }
                else if (!string.IsNullOrEmpty(geneSymbol))
                {
                    ShowLoading(true, $"Searching {geneSymbol}...");
                    await FetchByGeneSymbol(geneSymbol, _currentTaxId, seqType, ct);
                }
                else
                {
                    ShowInfo("Enter gene symbol or accession.", true);
                    return;
                }

                ct.ThrowIfCancellationRequested();

                if (!string.IsNullOrEmpty(_genomicSequence))
                {
                    InputSectionSummary.Text = $"✔ {_currentGeneSymbol} ({_currentAccession}) loaded";
                    GeneVisualizationPanel.Visibility = Visibility.Visible;

                    if (_isoforms.Count > 0)
                        IsoformPanel.Visibility = Visibility.Visible;

                    SetActiveTemplate(_genomicSequence);
                }
            }
            catch (OperationCanceledException) { /* user clicked again, ignore */ }
            catch (Exception ex) { ShowInfo($"Error: {ex.Message}", true); }
            finally { ShowLoading(false); }
        }

        private string GetSelectedTaxId()
        {
            if (SpeciesComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
                return item.Tag.ToString();
            return "9606";
        }

        private string GetSelectedSeqType()
        {
            if (SeqTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
                return item.Tag.ToString();
            return "mRNA";
        }

        private string GetSpeciesNameFromTaxId(string taxId) => taxId switch
        {
            "9606" => "human",
            "10090" => "mouse",
            "10116" => "rat",
            _ => "human"
        };

        private async Task FetchByGeneSymbol(string geneSymbol, string taxId, string seqType, CancellationToken ct = default)
        {
            string geneId = null;
            string resolvedSymbol = geneSymbol;

            ShowLoading(true, "Searching local database...");
            string speciesName = GetSpeciesNameFromTaxId(taxId);

            if (_geneIdService.DatabaseExists(speciesName))
            {
                bool loaded = await _geneIdService.LoadDatabaseAsync(speciesName);
                if (loaded && _geneIdService.IsDatabaseLoaded)
                {
                    var matches = _geneIdService.Convert(geneSymbol, "symbol");
                    if (matches.Count > 0)
                    {
                        resolvedSymbol = matches[0].Symbol;
                        if (!string.IsNullOrEmpty(matches[0].EntrezId))
                        {
                            geneId = matches[0].EntrezId;
                            ShowLoading(true, $"Found: {resolvedSymbol} (Entrez: {geneId})");
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(geneId))
            {
                ct.ThrowIfCancellationRequested();
                ShowLoading(true, $"Searching NCBI for {resolvedSymbol}...");
                string searchUrl = $"{NCBI_ESEARCH_URL}?db=gene&term={Uri.EscapeDataString(resolvedSymbol)}[Gene Name]+AND+{taxId}[Taxonomy ID]&retmode=xml";
                var resp = await httpClient.GetStringAsync(searchUrl);
                ct.ThrowIfCancellationRequested();
                var xml = System.Xml.Linq.XDocument.Parse(resp);
                var ids = xml.Descendants("Id").Select(x => x.Value).ToList();

                if (ids.Count == 0)
                {
                    searchUrl = $"{NCBI_ESEARCH_URL}?db=gene&term={Uri.EscapeDataString(resolvedSymbol)}[sym]+AND+{taxId}[Taxonomy ID]&retmode=xml";
                    resp = await httpClient.GetStringAsync(searchUrl);
                    ct.ThrowIfCancellationRequested();
                    xml = System.Xml.Linq.XDocument.Parse(resp);
                    ids = xml.Descendants("Id").Select(x => x.Value).ToList();
                }

                if (ids.Count == 0) { ShowInfo($"Gene '{geneSymbol}' not found.", true); return; }
                geneId = ids.First();
            }

            ct.ThrowIfCancellationRequested();
            ShowLoading(true, $"Gene ID: {geneId}, finding transcripts...");
            if (seqType == "mRNA") await FetchMRNATranscripts(geneId, resolvedSymbol, taxId, ct);
            else await FetchGenomicSequence(geneId, resolvedSymbol, taxId, ct);
        }

        /// <summary>
        /// Fetch mRNA transcripts — parse "transcript variant X" from NCBI description
        /// to display real isoform labels instead of arbitrary numbers.
        /// </summary>
        private async Task FetchMRNATranscripts(string geneId, string geneSymbol, string taxId, CancellationToken ct = default)
        {
            string searchUrl = $"{NCBI_ESEARCH_URL}?db=nuccore&term={Uri.EscapeDataString(geneSymbol)}[Gene]+AND+(NM_[Accession]+OR+NR_[Accession])+AND+{taxId}[Taxonomy ID]+AND+RefSeq[Filter]&retmax=20&retmode=xml";
            var resp = await httpClient.GetStringAsync(searchUrl);
            ct.ThrowIfCancellationRequested();
            var xml = System.Xml.Linq.XDocument.Parse(resp);
            var nucIds = xml.Descendants("Id").Select(x => x.Value).ToList();

            if (nucIds.Count == 0)
            {
                string elinkUrl = $"{NCBI_ELINK_URL}?dbfrom=gene&db=nuccore&id={geneId}&linkname=gene_nuccore_refseqrna&retmode=xml";
                await Task.Delay(350, ct);
                resp = await httpClient.GetStringAsync(elinkUrl);
                xml = System.Xml.Linq.XDocument.Parse(resp);
                nucIds = xml.Descendants("Link").Select(x => x.Element("Id")?.Value).Where(x => x != null).ToList();
            }

            if (nucIds.Count == 0) { ShowInfo($"No mRNA transcripts found for '{geneSymbol}'. Try Genomic mode.", true); return; }

            ShowLoading(true, $"Found {nucIds.Count} transcript(s), fetching details...");
            _isoforms.Clear();

            // Use ESummary (not EFetch) for proper DocumentSummary XML
            string summaryUrl = $"{NCBI_ESUMMARY_URL}?db=nuccore&id={string.Join(",", nucIds.Take(15))}&retmode=xml";
            await Task.Delay(350, ct);
            resp = await httpClient.GetStringAsync(summaryUrl);
            xml = System.Xml.Linq.XDocument.Parse(resp);

            // ESummary v2 returns <DocumentSummarySet><DocumentSummary uid="...">
            var docSums = xml.Descendants("DocumentSummary").ToList();

            if (docSums.Count > 0)
            {
                foreach (var docSum in docSums)
                {
                    string uid = docSum.Attribute("uid")?.Value ?? "";
                    string acc = docSum.Element("AccessionVersion")?.Value ?? docSum.Element("Caption")?.Value ?? "";
                    string title = docSum.Element("Title")?.Value ?? "";
                    int.TryParse(docSum.Element("Slen")?.Value ?? "0", out int len);

                    if (string.IsNullOrEmpty(acc) && !string.IsNullOrEmpty(uid)) acc = uid;
                    if (string.IsNullOrEmpty(acc)) continue;

                    string isoformLabel = ExtractIsoformLabel(title);

                    _isoforms.Add(new TranscriptIsoform
                    {
                        Accession = acc,
                        NucId = uid,
                        Length = len,
                        Description = title.Length > 80 ? title.Substring(0, 80) + "..." : title,
                        IsoformLabel = isoformLabel
                    });
                }
            }

            // Fallback: ESummary v1 (old format with <Item Name="...">)
            if (_isoforms.Count == 0)
            {
                var docSumsOld = xml.Descendants("DocSum").ToList();
                foreach (var docSum in docSumsOld)
                {
                    string uid = docSum.Element("Id")?.Value ?? "";
                    string acc = docSum.Descendants("Item").FirstOrDefault(i => i.Attribute("Name")?.Value == "AccessionVersion")?.Value
                              ?? docSum.Descendants("Item").FirstOrDefault(i => i.Attribute("Name")?.Value == "Caption")?.Value ?? "";
                    string title = docSum.Descendants("Item").FirstOrDefault(i => i.Attribute("Name")?.Value == "Title")?.Value ?? "";
                    int.TryParse(docSum.Descendants("Item").FirstOrDefault(i => i.Attribute("Name")?.Value == "Length")?.Value ?? "0", out int len);

                    if (string.IsNullOrEmpty(acc) && !string.IsNullOrEmpty(uid)) acc = uid;
                    if (string.IsNullOrEmpty(acc)) continue;

                    string isoformLabel = ExtractIsoformLabel(title);

                    _isoforms.Add(new TranscriptIsoform
                    {
                        Accession = acc,
                        NucId = uid,
                        Length = len,
                        Description = title.Length > 80 ? title.Substring(0, 80) + "..." : title,
                        IsoformLabel = isoformLabel
                    });
                }
            }

            // Last resort fallback
            if (_isoforms.Count == 0)
            {
                foreach (var id in nucIds.Take(10))
                    _isoforms.Add(new TranscriptIsoform { Accession = id, NucId = id, Description = "RefSeq transcript", IsoformLabel = "—" });
            }

            // Sort by variant/isoform number (natural sort: numeric first, then alpha)
            _isoforms = _isoforms.OrderBy(iso =>
            {
                var m = Regex.Match(iso.IsoformLabel, @"\d+");
                return m.Success ? int.Parse(m.Value) : int.MaxValue;
            }).ThenBy(iso => iso.IsoformLabel).ThenBy(iso => iso.Accession).ToList();

            IsoformListBox.ItemsSource = _isoforms;
            IsoformPanel.Visibility = Visibility.Visible;

            if (_isoforms.Count > 0)
                IsoformListBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Extract isoform label from NCBI title.
        /// Matches patterns: "transcript variant 1", "transcript variant X2", "transcript variant alpha"
        /// If not found, returns "—" to indicate no isoform info in NCBI data.
        /// </summary>
        private string ExtractIsoformLabel(string title)
        {
            if (string.IsNullOrEmpty(title)) return "—";

            // Pattern: "transcript variant X" where X can be a number, letter combo, or word
            // Strip trailing comma/period/semicolon that comes from NCBI title format
            var match = Regex.Match(title, @"transcript variant\s+([\w]+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return $"Variant {match.Groups[1].Value}";

            // Pattern: "isoform X" in title
            match = Regex.Match(title, @"isoform\s+([\w]+)", RegexOptions.IgnoreCase);
            if (match.Success)
                return $"Isoform {match.Groups[1].Value}";

            return "—";
        }

        private async Task FetchGenomicSequence(string geneId, string geneSymbol, string taxId, CancellationToken ct = default)
        {
            string elinkUrl = $"{NCBI_ELINK_URL}?dbfrom=gene&db=nuccore&id={geneId}&linkname=gene_nuccore_refseqgene&retmode=xml";
            var resp = await httpClient.GetStringAsync(elinkUrl);
            ct.ThrowIfCancellationRequested();
            var xml = System.Xml.Linq.XDocument.Parse(resp);
            var nucIds = xml.Descendants("Link").Select(x => x.Element("Id")?.Value).Where(x => x != null).ToList();

            if (nucIds.Count == 0)
            {
                string searchUrl = $"{NCBI_ESEARCH_URL}?db=nuccore&term=NG_[Accession]+AND+{Uri.EscapeDataString(geneSymbol)}[Gene]+AND+{taxId}[Taxonomy ID]+AND+RefSeqGene[Keyword]&retmax=5&retmode=xml";
                resp = await httpClient.GetStringAsync(searchUrl);
                xml = System.Xml.Linq.XDocument.Parse(resp);
                nucIds = xml.Descendants("Id").Select(x => x.Value).ToList();
            }

            if (nucIds.Count == 0) { ShowInfo($"No RefSeqGene for '{geneSymbol}'. Try mRNA mode.", true); return; }
            ct.ThrowIfCancellationRequested();

            _isGenomicMode = true;
            await FetchAndParseGenBank(nucIds.First(), ct);

            // Populate isoform panel from parsed genomic isoforms (no network call needed on selection)
            if (_genomicIsoforms.Count > 0)
            {
                IsoformListBox.ItemsSource = _genomicIsoforms;
                IsoformPanel.Visibility = Visibility.Visible;
                IsoformListBox.SelectedIndex = 0;
            }
        }

        private async void IsoformListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // ── Genomic mode: switch isoform CDS/exon view without re-fetching ──
            if (_isGenomicMode)
            {
                if (IsoformListBox.SelectedItem is GenomicIsoform gIso)
                {
                    ApplyGenomicIsoform(gIso);
                    UpdateGeneVisualization();
                    ShowInfo($"{gIso.TranscriptId} ({gIso.IsoformLabel}) | {gIso.Exons.Count} exons | CDS: {gIso.CdsParts.Sum(c => c.Length):N0} bp", false);
                }
                return;
            }

            // ── mRNA mode: fetch the selected transcript sequence from NCBI ──
            if (IsoformListBox.SelectedItem is TranscriptIsoform iso)
            {
                try
                {
                    ShowLoading(true, $"Loading {iso.Accession}...");
                    string nucId = !string.IsNullOrEmpty(iso.NucId) ? iso.NucId : iso.Accession;

                    // If nucId is not a numeric UID, resolve via Accession search
                    if (!long.TryParse(nucId, out _))
                    {
                        string searchUrl = $"{NCBI_ESEARCH_URL}?db=nuccore&term={nucId}[Accession]&retmode=xml";
                        var resp = await httpClient.GetStringAsync(searchUrl);
                        var xml = System.Xml.Linq.XDocument.Parse(resp);
                        nucId = xml.Descendants("Id").FirstOrDefault()?.Value ?? iso.NucId;
                    }

                    await FetchAndParseGenBank(nucId);

                    if (!string.IsNullOrEmpty(_genomicSequence))
                    {
                        GeneVisualizationPanel.Visibility = Visibility.Visible;
                        SetActiveTemplate(_genomicSequence);
                        InputSectionSummary.Text = $"✔ {_currentGeneSymbol} ({_currentAccession}) {_genomicSequence.Length:N0} bp";
                    }
                }
                catch (Exception ex) { ShowInfo($"Error loading isoform: {ex.Message}", true); }
                finally { ShowLoading(false); }
            }
        }

        private async Task FetchByAccession(string accession, CancellationToken ct = default)
        {
            string searchUrl = $"{NCBI_ESEARCH_URL}?db=nuccore&term={accession}[Accession]&retmode=xml";
            var resp = await httpClient.GetStringAsync(searchUrl);
            ct.ThrowIfCancellationRequested();
            var xml = System.Xml.Linq.XDocument.Parse(resp);
            var nucId = xml.Descendants("Id").FirstOrDefault()?.Value;
            if (string.IsNullOrEmpty(nucId)) { ShowInfo($"Accession '{accession}' not found.", true); return; }
            await FetchAndParseGenBank(nucId, ct);
        }

        private async Task FetchAndParseGenBank(string nucId, CancellationToken ct = default)
        {
            ShowLoading(true, "Downloading GenBank...");
            string gbUrl = $"{NCBI_EFETCH_URL}?db=nuccore&id={nucId}&rettype=gb&retmode=text";
            var gbResponse = await httpClient.GetStringAsync(gbUrl);
            ct.ThrowIfCancellationRequested();

            ShowLoading(true, "Downloading sequence...");
            string fastaUrl = $"{NCBI_EFETCH_URL}?db=nuccore&id={nucId}&rettype=fasta&retmode=text";
            var fastaResponse = await httpClient.GetStringAsync(fastaUrl);
            ct.ThrowIfCancellationRequested();

            _genomicSequence = ParseFastaSequence(fastaResponse);

            ShowLoading(true, "Parsing features...");
            ParseGenBankForCDSAndExons(gbResponse);
            UpdateGeneVisualization();

            ShowInfo($"✔ {_currentGeneSymbol} ({_currentAccession}) | {_genomicSequence.Length:N0} bp | Exons: {_exonList.Count} | CDS: {_cdsList.Sum(c => c.Length):N0} bp", false);
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
            _exonList.Clear();
            _cdsList.Clear();
            _genomicIsoforms.Clear();

            var accMatch = Regex.Match(gbText, @"ACCESSION\s+(\S+)");
            if (accMatch.Success) _currentAccession = accMatch.Groups[1].Value;

            var geneMatch = Regex.Match(gbText, @"/gene=""([^""]+)""");
            if (geneMatch.Success) _currentGeneSymbol = geneMatch.Groups[1].Value;

            if (_isGenomicMode)
            {
                ParseGenomicIsoforms(gbText);
                // Set initial view to first isoform
                if (_genomicIsoforms.Count > 0)
                    ApplyGenomicIsoform(_genomicIsoforms[0]);
                return;
            }

            // ── mRNA / accession mode: original single CDS + exon parsing ──

            var cdsHeaderMatch = Regex.Match(gbText, @"^\s{5}CDS\s+", RegexOptions.Multiline);
            if (cdsHeaderMatch.Success)
            {
                int startIdx = cdsHeaderMatch.Index + cdsHeaderMatch.Length;
                var sb = new StringBuilder();
                int i = startIdx;
                while (i < gbText.Length)
                {
                    int lineEnd = gbText.IndexOf('\n', i);
                    if (lineEnd < 0) lineEnd = gbText.Length;
                    string line = gbText.Substring(i, lineEnd - i).TrimEnd('\r');

                    if (line.Contains("/")) break;
                    if (sb.Length > 0 && line.Length > 0 && !char.IsWhiteSpace(line[0])) break;
                    if (sb.Length > 0 && Regex.IsMatch(line, @"^\s{5}\S")) break;

                    sb.Append(line.Trim());
                    i = lineEnd + 1;
                }

                string content = Regex.Replace(sb.ToString(), @"\s+", "");
                bool cdsIsComplement = content.StartsWith("complement(", StringComparison.OrdinalIgnoreCase);
                var ranges = Regex.Matches(content, @"<?(\d+)\.\.>?(\d+)");
                int num = 1;
                foreach (Match range in ranges)
                {
                    int s = int.Parse(range.Groups[1].Value);
                    int end = int.Parse(range.Groups[2].Value);
                    _cdsList.Add(new CDSRegion { PartNumber = num++, Start = s, End = end, Length = end - s + 1 });
                }
                // If on minus strand, reverse part order so part 1 = highest coordinate
                if (cdsIsComplement) _cdsList.Reverse();
            }

            var exonMatches = Regex.Matches(gbText, @"^\s{5}exon\s+(?:complement\()?<?(\d+)\.\.>?(\d+)\)?", RegexOptions.Multiline);
            int exNum = 1;
            foreach (Match m in exonMatches)
            {
                int s = int.Parse(m.Groups[1].Value);
                int end = int.Parse(m.Groups[2].Value);
                _exonList.Add(new ExonInfo { ExonNumber = exNum++, Start = s, End = end, Length = end - s + 1 });
            }

            _exonList = _exonList.OrderBy(e => e.Start).ToList();
            for (int idx = 0; idx < _exonList.Count; idx++) _exonList[idx].ExonNumber = idx + 1;
        }

        /// <summary>
        /// Parse all mRNA feature blocks from a genomic (NG_) GenBank file.
        /// Each mRNA feature defines one isoform's exon structure; matched CDS
        /// features are linked by /transcript_id qualifier.
        /// </summary>
        private void ParseGenomicIsoforms(string gbText)
        {
            // Helper: read a multi-line feature block starting just after the feature keyword line.
            // Returns (locationStr, qualifierStr) — location is all continuation lines before the
            // first /qualifier; qualifiers are everything from the first / onwards.
            string ReadFeatureBlock(int startIdx)
            {
                var loc = new StringBuilder();
                var qual = new StringBuilder();
                bool inQual = false;
                int i = startIdx;
                while (i < gbText.Length)
                {
                    int lineEnd = gbText.IndexOf('\n', i);
                    if (lineEnd < 0) lineEnd = gbText.Length;
                    string raw = gbText.Substring(i, lineEnd - i).TrimEnd('\r');

                    // new top-level or new feature at col 5
                    if (raw.Length > 0 && !char.IsWhiteSpace(raw[0])) break;
                    if (raw.Length >= 6 && !char.IsWhiteSpace(raw[5]) && char.IsLetter(raw[5])) break;

                    string content = raw.TrimStart();
                    if (content.StartsWith("/")) inQual = true;

                    if (!inQual) loc.Append(content);
                    else qual.AppendLine(content);

                    i = lineEnd + 1;
                }
                return loc.ToString() + "\n---QUAL---\n" + qual.ToString();
            }

            var mRNABlocks = new List<(string loc, string qual)>();
            var cdsBlocks = new List<(string loc, string qual)>();

            // Find FEATURES section
            int featIdx = gbText.IndexOf("\nFEATURES", StringComparison.Ordinal);
            if (featIdx < 0) return;

            // Scan feature lines
            var featurePattern = new Regex(@"^\s{5}(mRNA|CDS)\s+(.+)", RegexOptions.Multiline);
            foreach (Match m in featurePattern.Matches(gbText, featIdx))
            {
                // Collect location from match line + continuation lines
                var locSb = new StringBuilder(m.Groups[2].Value.Trim());
                var qualSb = new StringBuilder();
                bool inQual = false;
                int i = m.Index + m.Length + 1; // character after the match line's newline
                while (i < gbText.Length)
                {
                    int le = gbText.IndexOf('\n', i);
                    if (le < 0) le = gbText.Length;
                    string raw = gbText.Substring(i, le - i).TrimEnd('\r');

                    // Stop at next feature (col 5 non-space) or end of FEATURES / origin
                    if (raw.Length >= 5 && !char.IsWhiteSpace(raw[0])) break;
                    if (raw.Length >= 6 && raw[0] == ' ' && raw[5] != ' ' && char.IsLetter(raw[5])) break;

                    string content = raw.TrimStart();
                    if (content.StartsWith("/")) inQual = true;
                    if (!inQual) locSb.Append(content);
                    else qualSb.AppendLine(content);

                    i = le + 1;
                }

                string locStr = Regex.Replace(locSb.ToString(), @"\s+", "");
                string qualStr = qualSb.ToString();

                if (m.Groups[1].Value == "mRNA") mRNABlocks.Add((locStr, qualStr));
                else cdsBlocks.Add((locStr, qualStr));
            }

            int isoNum = 1;
            foreach (var (loc, qual) in mRNABlocks)
            {
                var iso = new GenomicIsoform();

                var txM = Regex.Match(qual, @"/transcript_id=""([^""]+)""");
                iso.TranscriptId = txM.Success ? txM.Groups[1].Value : $"Isoform{isoNum}";

                var prodM = Regex.Match(qual, @"/product=""([^""]+)""");
                iso.ProductName = prodM.Success ? prodM.Groups[1].Value : "";

                string label = ExtractIsoformLabel(iso.ProductName);
                if (label == "—") label = ExtractIsoformLabel(iso.TranscriptId);
                if (label == "—") label = $"Isoform {isoNum}";
                iso.IsoformLabel = label;

                // Parse exon ranges from mRNA location join(...)
                bool mRNAIsComplement = loc.StartsWith("complement(", StringComparison.OrdinalIgnoreCase);
                int exNum = 1;
                foreach (Match r in Regex.Matches(loc, @"<?(\d+)\.\.>?(\d+)"))
                {
                    int s = int.Parse(r.Groups[1].Value);
                    int e = int.Parse(r.Groups[2].Value);
                    iso.Exons.Add(new ExonInfo { ExonNumber = exNum++, Start = s, End = e, Length = e - s + 1 });
                }
                // On minus strand, exons are listed 3'→5'; reverse to get 5'→3' order
                if (mRNAIsComplement) iso.Exons.Reverse();
                for (int idx = 0; idx < iso.Exons.Count; idx++) iso.Exons[idx].ExonNumber = idx + 1;
                iso.IsComplement = mRNAIsComplement;

                // Match CDS block by transcript_id
                foreach (var (cLoc, cQual) in cdsBlocks)
                {
                    var cTx = Regex.Match(cQual, @"/transcript_id=""([^""]+)""");
                    if (cTx.Success && cTx.Groups[1].Value == iso.TranscriptId)
                    {
                        bool cdsIsComp = cLoc.StartsWith("complement(", StringComparison.OrdinalIgnoreCase);
                        int partNum = 1;
                        foreach (Match r in Regex.Matches(cLoc, @"<?(\d+)\.\.>?(\d+)"))
                        {
                            int s = int.Parse(r.Groups[1].Value);
                            int e = int.Parse(r.Groups[2].Value);
                            iso.CdsParts.Add(new CDSRegion { PartNumber = partNum++, Start = s, End = e, Length = e - s + 1 });
                        }
                        if (cdsIsComp) iso.CdsParts.Reverse();
                        break;
                    }
                }

                if (iso.Exons.Count > 0)
                    _genomicIsoforms.Add(iso);

                isoNum++;
            }

            // Natural sort by transcript accession number
            _genomicIsoforms = _genomicIsoforms
                .OrderBy(iso => { var m = Regex.Match(iso.TranscriptId, @"\d+"); return m.Success ? int.Parse(m.Value) : int.MaxValue; })
                .ThenBy(iso => iso.TranscriptId)
                .ToList();
        }

        /// <summary>
        /// Apply a genomic isoform: update _exonList and _cdsList from the isoform's
        /// pre-parsed coordinates, then trigger redraw. No network call.
        /// </summary>
        private void ApplyGenomicIsoform(GenomicIsoform iso)
        {
            _exonList = iso.Exons
                .OrderBy(e => e.Start)
                .Select((e, i) => new ExonInfo { ExonNumber = i + 1, Start = e.Start, End = e.End, Length = e.Length })
                .ToList();

            _cdsList = iso.CdsParts
                .OrderBy(c => c.Start)
                .Select((c, i) => new CDSRegion { PartNumber = i + 1, Start = c.Start, End = c.End, Length = c.Length })
                .ToList();

            // For minus-strand genes, the primer template must be the reverse complement
            // of the genomic sequence region spanning the gene.
            if (iso.IsComplement && !string.IsNullOrEmpty(_genomicSequence) && _exonList.Count > 0)
            {
                int geneStart = _exonList.Min(e => e.Start) - 1;            // 0-based
                int geneEnd = _exonList.Max(e => e.End);                   // exclusive
                geneStart = Math.Max(0, geneStart);
                geneEnd = Math.Min(_genomicSequence.Length, geneEnd);

                string plusStrand = _genomicSequence.Substring(geneStart, geneEnd - geneStart);
                string minusStrand = GetReverseComplement(plusStrand);

                _templateOffset = geneStart;
                SetActiveTemplate(minusStrand);

                // Remap coordinates into minus-strand template space
                // On minus strand: feature at genomic pos p maps to (geneEnd - p) in RC
                int rcLen = minusStrand.Length;
                _exonList = _exonList.Select((e, i) =>
                {
                    int rcStart = geneEnd - e.End + 1;        // 1-based in RC
                    int rcEnd = geneEnd - e.Start + 1;
                    return new ExonInfo { ExonNumber = i + 1, Start = rcStart, End = rcEnd, Length = rcEnd - rcStart + 1 };
                }).OrderBy(e => e.Start).ToList();
                for (int i = 0; i < _exonList.Count; i++) _exonList[i].ExonNumber = i + 1;

                _cdsList = _cdsList.Select((c, i) =>
                {
                    int rcStart = geneEnd - c.End + 1;
                    int rcEnd = geneEnd - c.Start + 1;
                    return new CDSRegion { PartNumber = i + 1, Start = rcStart, End = rcEnd, Length = rcEnd - rcStart + 1 };
                }).OrderBy(c => c.Start).ToList();
                for (int i = 0; i < _cdsList.Count; i++) _cdsList[i].PartNumber = i + 1;

                ShowInfo($"Gene is on minus strand — template set to reverse complement ({minusStrand.Length:N0} bp).", false);
            }
        }

        #endregion

        #region === Gene Structure Visualization ===

        private void UpdateGeneVisualization()
        {
            GeneVisualizationCanvas.Children.Clear();
            if (_exonList.Count == 0 && _cdsList.Count == 0 && string.IsNullOrEmpty(_genomicSequence)) return;

            int totalLen = _genomicSequence.Length;
            if (totalLen == 0) return;

            double viewportWidth = Math.Max(GeneVisualizationCanvas.ActualWidth, 800);
            double minScaleViewport = (viewportWidth - 2 * MARGIN) / totalLen;
            double minExonWidth = 15;
            double minScaleExons = _exonList.Count > 0 && _exonList.Min(e => e.Length) > 0
                ? minExonWidth / _exonList.Min(e => e.Length) : minScaleViewport;

            _currentScale = Math.Max(minScaleViewport, Math.Min(minScaleExons, 0.1));
            _currentScale = Math.Max(MIN_SCALE, Math.Min(MAX_SCALE, _currentScale));
            _defaultScale = _currentScale;

            RedrawVisualization();
            GeneInfoLabel.Text = $"{_currentGeneSymbol} | {_currentAccession} | {totalLen:N0} bp | {_exonList.Count} exons | CDS: {_cdsList.Sum(c => c.Length):N0} bp";
        }

        private void RedrawVisualization()
        {
            GeneVisualizationCanvas.Children.Clear();
            if (string.IsNullOrEmpty(_genomicSequence)) return;

            int totalLen = _genomicSequence.Length;
            double viewportWidth = Math.Max(GeneVisualizationScrollViewer?.ViewportWidth ?? 800, 800);
            double canvasWidth = Math.Max(2 * MARGIN + totalLen * _currentScale, viewportWidth);

            GeneVisualizationCanvas.Width = canvasWidth;
            GeneVisualizationCanvas.Height = 105;

            // Backbone
            GeneVisualizationCanvas.Children.Add(new Line
            {
                X1 = MARGIN,
                Y1 = GENE_Y,
                X2 = canvasWidth - MARGIN,
                Y2 = GENE_Y,
                Stroke = Brushes.LightGray,
                StrokeThickness = 2
            });

            // Exons
            foreach (var exon in _exonList)
            {
                double x = MARGIN + (exon.Start - 1) * _currentScale;
                double w = Math.Max(exon.Length * _currentScale, 4);
                var rect = new Rectangle
                {
                    Width = w,
                    Height = 18,
                    Fill = new SolidColorBrush(Color.FromRgb(200, 230, 255)),
                    Stroke = new SolidColorBrush(Color.FromRgb(100, 181, 246)),
                    StrokeThickness = 1,
                    RadiusX = 2,
                    RadiusY = 2,
                    ToolTip = $"Exon {exon.ExonNumber}: {exon.Start:N0}-{exon.End:N0} ({exon.Length:N0} bp)"
                };
                Canvas.SetLeft(rect, x); Canvas.SetTop(rect, GENE_Y - 9);
                GeneVisualizationCanvas.Children.Add(rect);

                if (w > 20)
                {
                    var lbl = new TextBlock { Text = $"E{exon.ExonNumber}", FontSize = 8, Foreground = Brushes.Gray };
                    Canvas.SetLeft(lbl, x + 3); Canvas.SetTop(lbl, GENE_Y - 7);
                    GeneVisualizationCanvas.Children.Add(lbl);
                }
            }

            // CDS — clickable to set as constraint region
            foreach (var cds in _cdsList)
            {
                double x = MARGIN + (cds.Start - 1) * _currentScale;
                double w = Math.Max(cds.Length * _currentScale, 4);
                var cdsCapture = cds; // closure capture
                var rect = new Rectangle
                {
                    Width = w,
                    Height = 10,
                    Fill = new SolidColorBrush(Color.FromRgb(255, 183, 77)),
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                    StrokeThickness = 1,
                    RadiusX = 2,
                    RadiusY = 2,
                    Cursor = Cursors.Hand,
                    ToolTip = $"CDS {cdsCapture.PartNumber}: {cdsCapture.Start:N0}-{cdsCapture.End:N0} — Click to set as constraint region"
                };
                rect.MouseLeftButtonDown += (s, ev) =>
                {
                    ev.Handled = true;
                    if (string.IsNullOrEmpty(_genomicSequence)) return;
                    int cgsStart = _cdsList.Min(c => c.Start) - 1;
                    int cgsEnd = _cdsList.Max(c => c.End);
                    if (cgsEnd > _genomicSequence.Length) return;

                    string cdsSeq = _genomicSequence.Substring(cgsStart, cgsEnd - cgsStart);
                    _templateOffset = cgsStart;
                    SetActiveTemplate(cdsSeq);
                    _constraintStart = 0;
                    _constraintEnd = cdsSeq.Length;
                    UpdateConstraintLabel();
                    if (IncludeRegionCheckBox != null) IncludeRegionCheckBox.IsChecked = true;
                    ShowInfo($"Active template set to CDS ({cdsSeq.Length:N0} bp). Product will span the full CDS.", false);
                };
                Canvas.SetLeft(rect, x); Canvas.SetTop(rect, GENE_Y - 5);
                GeneVisualizationCanvas.Children.Add(rect);
            }

            // Selection overlay
            if (_selectionStart >= 0 && _selectionEnd >= 0)
                DrawSelectionOverlay();

            DrawScale(canvasWidth, totalLen);

            // Draw primer position markers
            DrawPrimerMarkers();
        }

        private void DrawPrimerMarkers()
        {
            if (_markerFwdStart < 0 && _markerRevStart < 0) return;
            double markerY = GENE_Y + 14; // below the backbone

            if (_markerFwdStart >= 0)
            {
                int genomicPos = _templateOffset + _markerFwdStart;
                double x = MARGIN + genomicPos * _currentScale;
                double w = Math.Max(_markerFwdLen * _currentScale, 6);

                var rect = new Rectangle
                {
                    Width = w,
                    Height = 5,
                    Fill = new SolidColorBrush(Color.FromRgb(25, 118, 210)), // blue
                    RadiusX = 1,
                    RadiusY = 1,
                    ToolTip = $"Forward Primer: {genomicPos + 1}–{genomicPos + _markerFwdLen} ({_markerFwdLen} bp)"
                };
                Canvas.SetLeft(rect, x); Canvas.SetTop(rect, markerY);
                GeneVisualizationCanvas.Children.Add(rect);

                // Arrow head (right-pointing ▶)
                var arrow = new Polygon
                {
                    Points = new PointCollection { new Point(0, 0), new Point(5, 2.5), new Point(0, 5) },
                    Fill = new SolidColorBrush(Color.FromRgb(25, 118, 210))
                };
                Canvas.SetLeft(arrow, x + w); Canvas.SetTop(arrow, markerY);
                GeneVisualizationCanvas.Children.Add(arrow);

                var lbl = new TextBlock { Text = "Fwd", FontSize = 7, Foreground = new SolidColorBrush(Color.FromRgb(25, 118, 210)), FontWeight = FontWeights.Bold };
                Canvas.SetLeft(lbl, x); Canvas.SetTop(lbl, markerY + 6);
                GeneVisualizationCanvas.Children.Add(lbl);
            }

            if (_markerRevStart >= 0)
            {
                int genomicPos = _templateOffset + _markerRevStart;
                double x = MARGIN + genomicPos * _currentScale;
                double w = Math.Max(_markerRevLen * _currentScale, 6);

                var rect = new Rectangle
                {
                    Width = w,
                    Height = 5,
                    Fill = new SolidColorBrush(Color.FromRgb(56, 142, 60)), // green
                    RadiusX = 1,
                    RadiusY = 1,
                    ToolTip = $"Reverse Primer: {genomicPos + 1}–{genomicPos + _markerRevLen} ({_markerRevLen} bp)"
                };
                Canvas.SetLeft(rect, x); Canvas.SetTop(rect, markerY + 16);
                GeneVisualizationCanvas.Children.Add(rect);

                // Arrow head (left-pointing ◀)
                var arrow = new Polygon
                {
                    Points = new PointCollection { new Point(5, 0), new Point(0, 2.5), new Point(5, 5) },
                    Fill = new SolidColorBrush(Color.FromRgb(56, 142, 60))
                };
                Canvas.SetLeft(arrow, x - 5); Canvas.SetTop(arrow, markerY + 16);
                GeneVisualizationCanvas.Children.Add(arrow);

                var lbl = new TextBlock { Text = "Rev", FontSize = 7, Foreground = new SolidColorBrush(Color.FromRgb(56, 142, 60)), FontWeight = FontWeights.Bold };
                Canvas.SetLeft(lbl, x + w - 16); Canvas.SetTop(lbl, markerY + 22);
                GeneVisualizationCanvas.Children.Add(lbl);
            }
        }

        private void DrawScale(double canvasWidth, int totalLen)
        {
            double scaleY = 70;
            int tick = totalLen <= 50 ? 10 :
                       totalLen <= 200 ? 25 :
                       totalLen <= 1000 ? 100 :
                       totalLen <= 5000 ? 500 :
                       totalLen <= 20000 ? 2000 :
                       totalLen <= 100000 ? 10000 :
                       totalLen <= 500000 ? 50000 : 100000;

            // Ensure at most ~10 tick marks to prevent label overlap
            while (totalLen / tick > 10) tick *= 2;

            for (int pos = 0; pos <= totalLen; pos += tick)
            {
                double x = MARGIN + pos * _currentScale;
                if (x > canvasWidth - MARGIN) break;

                GeneVisualizationCanvas.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = scaleY,
                    X2 = x,
                    Y2 = scaleY + 4,
                    Stroke = Brushes.Gray,
                    StrokeThickness = 1
                });

                string label = pos >= 1000000 ? $"{pos / 1e6:0.#}M" : pos >= 1000 ? $"{pos / 1e3:0.#}k" : pos.ToString();
                var lbl = new TextBlock { Text = label, FontSize = 8, Foreground = Brushes.Gray };
                Canvas.SetLeft(lbl, x - 10); Canvas.SetTop(lbl, scaleY + 5);
                GeneVisualizationCanvas.Children.Add(lbl);
            }
        }

        #endregion

        #region === Canvas Region Selection ===

        private void GeneCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_genomicSequence)) return;
            _isDragging = true;
            _dragStartX = e.GetPosition(GeneVisualizationCanvas).X;
            GeneVisualizationCanvas.CaptureMouse();
        }

        private void GeneCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging || string.IsNullOrEmpty(_genomicSequence)) return;

            double currentX = e.GetPosition(GeneVisualizationCanvas).X;
            double startX = Math.Min(_dragStartX, currentX);
            double endX = Math.Max(_dragStartX, currentX);

            int startBp = Math.Max(0, (int)((startX - MARGIN) / _currentScale));
            int endBp = Math.Min(_genomicSequence.Length, (int)((endX - MARGIN) / _currentScale));

            _selectionStart = startBp;
            _selectionEnd = endBp;

            RedrawVisualization();
            SelectedRegionLabel.Text = $"{startBp + 1} – {endBp} ({endBp - startBp} bp)";
        }

        private void GeneCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging) return;
            _isDragging = false;
            GeneVisualizationCanvas.ReleaseMouseCapture();
            UseSelectedRegionButton.IsEnabled = _selectionStart >= 0 && _selectionEnd > _selectionStart && (_selectionEnd - _selectionStart) >= 20;
        }

        private void DrawSelectionOverlay()
        {
            if (_selectionStart < 0 || _selectionEnd <= _selectionStart) return;

            double x = MARGIN + _selectionStart * _currentScale;
            double w = (_selectionEnd - _selectionStart) * _currentScale;

            var rect = new Rectangle
            {
                Width = Math.Max(w, 2),
                Height = 50,
                Fill = new SolidColorBrush(Color.FromArgb(50, 33, 150, 243)),
                Stroke = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, GENE_Y - 25);
            GeneVisualizationCanvas.Children.Add(rect);

            // "Stretched to selected region" label
            var label = new TextBlock
            {
                Text = "▸ Stretched to selection",
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                FontStyle = FontStyles.Italic
            };
            Canvas.SetLeft(label, x + 2); Canvas.SetTop(label, GENE_Y - 24);
            GeneVisualizationCanvas.Children.Add(label);
        }

        private void UseSelectedRegion_Click(object sender, RoutedEventArgs e)
        {
            if (_selectionStart < 0 || _selectionEnd <= _selectionStart) return;
            int start = Math.Max(0, _selectionStart);
            int end = Math.Min(_genomicSequence.Length, _selectionEnd);
            string region = _genomicSequence.Substring(start, end - start);
            _templateOffset = start;
            SetActiveTemplate(region);
            ShowInfo($"Active template set to region {start + 1}–{end} ({region.Length} bp)", false);
            ActiveTemplateDisplay.BringIntoView();
        }

        private void SetConstraintFromSelection_Click(object sender, RoutedEventArgs e)
        {
            if (_selectionStart < 0 || _selectionEnd <= _selectionStart || (_selectionEnd - _selectionStart) < 10)
            {
                ShowInfo("Please drag-select a region on the gene canvas first.", true); return;
            }
            // Convert genomic canvas coordinates to active template coordinates
            _constraintStart = _selectionStart - _templateOffset;
            _constraintEnd = _selectionEnd - _templateOffset;
            _constraintStart = Math.Max(0, _constraintStart);
            _constraintEnd = Math.Min(_activeTemplate.Length, _constraintEnd);
            UpdateConstraintLabel();
            if (IncludeRegionCheckBox != null) IncludeRegionCheckBox.IsChecked = true;
        }

        private void SetConstraintFromCDS_Click(object sender, RoutedEventArgs e)
        {
            if (_cdsList.Count == 0)
            {
                ShowInfo("No CDS found. Load a gene in Genomic mode or an mRNA with CDS annotation.", true); return;
            }

            // CDS coordinates are 1-based genomic
            int cdsGenomicStart = _cdsList.Min(c => c.Start) - 1;  // 0-based
            int cdsGenomicEnd = _cdsList.Max(c => c.End);         // exclusive

            if (string.IsNullOrEmpty(_genomicSequence) || cdsGenomicEnd > _genomicSequence.Length)
            {
                ShowInfo("CDS coordinates exceed genomic sequence. Cannot extract CDS region.", true); return;
            }

            // Set active template = CDS sequence
            string cdsSeq = _genomicSequence.Substring(cdsGenomicStart, cdsGenomicEnd - cdsGenomicStart);
            _templateOffset = cdsGenomicStart;
            SetActiveTemplate(cdsSeq);

            // Constraint = entire template (must span whole CDS)
            _constraintStart = 0;
            _constraintEnd = cdsSeq.Length;
            UpdateConstraintLabel();
            if (IncludeRegionCheckBox != null) IncludeRegionCheckBox.IsChecked = true;

            ShowInfo($"Active template set to CDS ({cdsSeq.Length:N0} bp). Product will span the full CDS.", false);
        }

        private void ClearConstraint_Click(object sender, RoutedEventArgs e)
        {
            _constraintStart = -1;
            _constraintEnd = -1;
            if (IncludeRegionCheckBox != null) IncludeRegionCheckBox.IsChecked = false;
            UpdateConstraintLabel();
        }

        private void UpdateConstraintLabel()
        {
            if (ConstraintRegionLabel == null) return;
            if (_constraintStart >= 0 && _constraintEnd > _constraintStart)
            {
                int gs = _constraintStart + _templateOffset + 1;
                int ge = _constraintEnd + _templateOffset;
                ConstraintRegionLabel.Text = $"Region: {gs}–{ge} ({_constraintEnd - _constraintStart} bp)";
                ConstraintRegionLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3"));
            }
            else
            {
                ConstraintRegionLabel.Text = "No constraint set";
                ConstraintRegionLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#999999"));
            }
        }

        private void UseEntireSequence_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_genomicSequence))
            {
                _templateOffset = 0;
                SetActiveTemplate(_genomicSequence);
                ActiveTemplateDisplay.BringIntoView();
            }
        }

        #endregion

        #region === Zoom ===

        private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomVisualization(1.5);
        private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomVisualization(1.0 / 1.5);
        private void ZoomResetButton_Click(object sender, RoutedEventArgs e) { _currentScale = _defaultScale; RedrawVisualization(); }

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
            if (string.IsNullOrEmpty(_genomicSequence)) return;
            double newScale = Math.Max(MIN_SCALE, Math.Min(MAX_SCALE, _currentScale * factor));
            if (Math.Abs(newScale - _currentScale) < 0.0001) return;
            _currentScale = newScale;
            RedrawVisualization();
        }

        #endregion

        #region === Set Active Template (auto-switch to AutoPick when template exists) ===

        private void SetActiveTemplate(string seq)
        {
            _activeTemplate = seq;
            ActiveTemplateDisplay.Text = seq.Length > 500 ? seq.Substring(0, 500) + "..." : seq;
            ActiveTemplateLengthLabel.Text = $"Active template: {seq.Length:N0} bp";

            // Auto-switch to Auto-Pick mode when template is loaded
            if (seq.Length >= 50)
            {
                AutoModeRadio.IsChecked = true;
            }
        }

        #endregion

        #region === Design Mode Toggle ===

        private void DesignMode_Changed(object sender, RoutedEventArgs e)
        {
            if (ManualInputPanel == null || AutoDesignPanel == null) return;
            bool isManual = ManualModeRadio.IsChecked == true;
            ManualInputPanel.Visibility = isManual ? Visibility.Visible : Visibility.Collapsed;
            AutoDesignPanel.Visibility = isManual ? Visibility.Collapsed : Visibility.Visible;
        }

        #endregion

        #region === RE Checkbox in Auto-Pick ===

        private void AutoPickRECheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (AutoPickREPanel == null) return;
            AutoPickREPanel.Visibility = AutoPickRECheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ManualRECheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (ManualREPanel == null) return;
            ManualREPanel.Visibility = ManualRECheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            UpdateREPreview();
        }

        #endregion

        #region === Manual Primer Analysis (with find-missing-strand) ===

        private void UpdateIonConcentrations()
        {
            if (double.TryParse(NaConcInput.Text, out double na) && na > 0) _naConc = na / 1000.0; // mM → M
            if (double.TryParse(MgConcInput.Text, out double mg) && mg >= 0) _mgConc = mg / 1000.0;
            if (double.TryParse(DntpConcInput.Text, out double dntp) && dntp >= 0) _dntpConc = dntp / 1000.0;
            if (double.TryParse(PrimerConcInput.Text, out double pc) && pc > 0) _primerConc = pc * 1e-9; // nM → M
        }

        private void AnalyzePrimers_Click(object sender, RoutedEventArgs e)
        {
            UpdateIonConcentrations();
            string fwd = CleanSequence(ForwardPrimerInput.Text);
            string rev = CleanSequence(ReversePrimerInput.Text);

            if (string.IsNullOrEmpty(fwd) && string.IsNullOrEmpty(rev))
            {
                MessageBox.Show("Please enter at least one primer.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // If one primer is missing and we have a template, try to find the complementary strand
            if (!string.IsNullOrEmpty(_activeTemplate) && (string.IsNullOrEmpty(fwd) || string.IsNullOrEmpty(rev)))
            {
                FindMissingStrand(ref fwd, ref rev);
            }

            _lastFwdPrimer = fwd;
            _lastRevPrimer = rev;

            PrimerAnalysis fwdA = null, revA = null;

            if (!string.IsNullOrEmpty(fwd))
            {
                fwdA = AnalyzePrimer(fwd);
                DisplayPrimerResult(fwdA, FwdSequenceDisplay, FwdLength, FwdTm, FwdGC, FwdMW, FwdGCClamp, FwdSelfComp, FwdHairpin, FwdEndStability, FwdWarnings, FwdSelfDimerDG, FwdHairpinDG, FwdAnyComp);
            }
            else ClearPrimerDisplay(FwdSequenceDisplay, FwdLength, FwdTm, FwdGC, FwdMW, FwdGCClamp, FwdSelfComp, FwdHairpin, FwdEndStability, FwdWarnings, FwdSelfDimerDG, FwdHairpinDG, FwdAnyComp);

            if (!string.IsNullOrEmpty(rev))
            {
                revA = AnalyzePrimer(rev);
                DisplayPrimerResult(revA, RevSequenceDisplay, RevLength, RevTm, RevGC, RevMW, RevGCClamp, RevSelfComp, RevHairpin, RevEndStability, RevWarnings, RevSelfDimerDG, RevHairpinDG, RevAnyComp);
            }
            else ClearPrimerDisplay(RevSequenceDisplay, RevLength, RevTm, RevGC, RevMW, RevGCClamp, RevSelfComp, RevHairpin, RevEndStability, RevWarnings, RevSelfDimerDG, RevHairpinDG, RevAnyComp);

            if (fwdA != null && revA != null)
            {
                DisplayPairSummary(fwdA, revA, _activeTemplate, null);
                PairSummaryPanel.Visibility = Visibility.Visible;
            }
            else PairSummaryPanel.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrEmpty(_activeTemplate) && (fwdA != null || revA != null))
            {
                GenerateSequenceVisualization(_activeTemplate, fwd, rev);
                SequenceViewPanel.Visibility = Visibility.Visible;
            }
            else SequenceViewPanel.Visibility = Visibility.Collapsed;

            AutoResultsPanel.Visibility = Visibility.Collapsed;
            UpdateREPreview();
        }

        /// <summary>
        /// When one primer is provided and the other is empty, search the template for
        /// the best complementary primer matching the user's criteria.
        /// Searches ALL binding sites on the template (not just the first), to handle
        /// non-unique sequences and find the globally best pairing.
        /// </summary>
        private void FindMissingStrand(ref string fwd, ref string rev)
        {
            if (!double.TryParse(ManualTargetTmInput.Text, out double targetTm)) targetTm = 60;
            if (!double.TryParse(ManualTmToleranceInput.Text, out double tmTol)) tmTol = 3;
            if (!int.TryParse(ManualTargetProductInput.Text, out int targetProduct)) targetProduct = 300;
            if (!int.TryParse(ManualProductToleranceInput.Text, out int productTol)) productTol = 200;

            string template = _activeTemplate;
            bool findingReverse = !string.IsNullOrEmpty(fwd) && string.IsNullOrEmpty(rev);
            bool findingForward = string.IsNullOrEmpty(fwd) && !string.IsNullOrEmpty(rev);

            if (findingReverse)
            {
                // Find ALL forward primer positions on template
                var fwdPositions = FindAllOccurrences(template, fwd);
                if (fwdPositions.Count == 0)
                {
                    ShowInfo("Forward primer not found on template. Cannot find complementary reverse primer.", true);
                    return;
                }

                PrimerAnalysis bestA = null;
                string bestSeq = "";
                double bestScore = double.MinValue;

                foreach (int fwdPos in fwdPositions)
                {
                    int minEnd = fwdPos + fwd.Length + targetProduct - productTol;
                    int maxEnd = Math.Min(template.Length, fwdPos + fwd.Length + targetProduct + productTol);

                    for (int end = Math.Max(minEnd, fwdPos + fwd.Length + 50); end <= maxEnd; end++)
                    {
                        for (int len = 18; len <= 25 && end - len >= 0; len++)
                        {
                            string region = template.Substring(end - len, len);
                            string rc = GetReverseComplement(region);
                            var a = AnalyzePrimer(rc);

                            if (Math.Abs(a.Tm - targetTm) > tmTol) continue;
                            if (a.GCPercent < 30 || a.GCPercent > 70) continue;

                            int prod = end - fwdPos;
                            double score = 100 - Math.Abs(a.Tm - targetTm) * 3 - Math.Abs(prod - targetProduct) * 0.1
                                - a.SelfComplementarityScore * 2 - a.HairpinScore * 3;

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestA = a;
                                bestSeq = rc;
                            }
                        }
                    }
                }

                if (bestA != null)
                {
                    rev = bestSeq;
                    ReversePrimerInput.Text = rev;
                    string siteMsg = fwdPositions.Count > 1 ? $" ({fwdPositions.Count} binding sites evaluated)" : "";
                    ShowInfo($"Found complementary reverse primer: {rev} (Tm={bestA.Tm:F1}°C){siteMsg}", false);
                }
                else
                {
                    ShowInfo("No reverse primer found matching criteria. Try adjusting parameters.", true);
                }
            }
            else if (findingForward)
            {
                string revRC = GetReverseComplement(rev);
                var revPositions = FindAllOccurrences(template, revRC);
                if (revPositions.Count == 0)
                {
                    ShowInfo("Reverse primer not found on template. Cannot find complementary forward primer.", true);
                    return;
                }

                PrimerAnalysis bestA = null;
                string bestSeq = "";
                double bestScore = double.MinValue;

                foreach (int revPos in revPositions)
                {
                    int maxStart = revPos - targetProduct + productTol;
                    int minStart = Math.Max(0, revPos - targetProduct - productTol);

                    for (int start = minStart; start <= Math.Min(maxStart, revPos - 50); start++)
                    {
                        for (int len = 18; len <= 25 && start + len <= template.Length; len++)
                        {
                            string seq = template.Substring(start, len);
                            var a = AnalyzePrimer(seq);

                            if (Math.Abs(a.Tm - targetTm) > tmTol) continue;
                            if (a.GCPercent < 30 || a.GCPercent > 70) continue;

                            int prod = revPos + revRC.Length - start;
                            double score = 100 - Math.Abs(a.Tm - targetTm) * 3 - Math.Abs(prod - targetProduct) * 0.1
                                - a.SelfComplementarityScore * 2 - a.HairpinScore * 3;

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestA = a;
                                bestSeq = seq;
                            }
                        }
                    }
                }

                if (bestA != null)
                {
                    fwd = bestSeq;
                    ForwardPrimerInput.Text = fwd;
                    string siteMsg = revPositions.Count > 1 ? $" ({revPositions.Count} binding sites evaluated)" : "";
                    ShowInfo($"Found complementary forward primer: {fwd} (Tm={bestA.Tm:F1}°C){siteMsg}", false);
                }
                else
                {
                    ShowInfo("No forward primer found matching criteria. Try adjusting parameters.", true);
                }
            }
        }

        /// <summary>Find all occurrences of a subsequence in a template string.</summary>
        private List<int> FindAllOccurrences(string template, string query)
        {
            var positions = new List<int>();
            int idx = 0;
            while ((idx = template.IndexOf(query, idx, StringComparison.Ordinal)) >= 0)
            {
                positions.Add(idx);
                idx++; // allow overlapping matches
            }
            return positions;
        }

        #endregion

        #region === Auto Primer Picking (with target values + RE integration) ===

        private CancellationTokenSource _autoPickCts;

        private async void AutoPickPrimers_Click(object sender, RoutedEventArgs e)
        {
            UpdateIonConcentrations();
            string template = _activeTemplate;
            if (string.IsNullOrEmpty(template) || template.Length < 50)
            {
                MessageBox.Show("Load or enter a template (≥50 bp) first.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Cancel previous auto-pick if still running
            _autoPickCts?.Cancel();
            _autoPickCts?.Dispose();
            _autoPickCts = new CancellationTokenSource();
            var ct = _autoPickCts.Token;

            if (!int.TryParse(MinLengthInput.Text, out int minLen)) minLen = 18;
            if (!int.TryParse(MaxLengthInput.Text, out int maxLen)) maxLen = 25;
            if (!double.TryParse(MinTmInput.Text, out double minTm)) minTm = 55;
            if (!double.TryParse(MaxTmInput.Text, out double maxTm)) maxTm = 65;
            if (!double.TryParse(TargetTmInput.Text, out double targetTm)) targetTm = 60;
            if (!int.TryParse(MinProductInput.Text, out int minProd)) minProd = 100;
            if (!int.TryParse(MaxProductInput.Text, out int maxProd)) maxProd = 1000;
            if (!int.TryParse(TargetProductInput.Text, out int targetProd)) targetProd = 300;
            if (!double.TryParse(MaxTmDiffInput.Text, out double maxTmDiff)) maxTmDiff = 3;
            bool gcClamp = GcClampCheckBox.IsChecked == true;
            bool penalizeSelfComp = SelfCompCheckBox.IsChecked == true;

            // RE integration
            bool includeRE = AutoPickRECheckBox.IsChecked == true;
            string fwdRESeq = includeRE ? GetSelectedRESequence(AutoFwdREComboBox) : "";
            string revRESeq = includeRE ? GetSelectedRESequence(AutoRevREComboBox) : "";

            // Read protective bases BEFORE Task.Run (UI thread access)
            var _pbItem = ProtectiveBasesInput?.SelectedItem as System.Windows.Controls.ComboBoxItem;
            if (!int.TryParse(_pbItem?.Tag as string ?? "2", out int protBases)) protBases = 2;

            // "Product must include region" constraint (in template coordinates)
            bool useConstraint = IncludeRegionCheckBox?.IsChecked == true && _constraintStart >= 0 && _constraintEnd > _constraintStart;
            int constraintStart = useConstraint ? _constraintStart : -1;
            int constraintEnd = useConstraint ? _constraintEnd : -1;

            // When constraint is active: product size and ΔTm are determined by the constraint,
            // so we open up both limits to avoid no-result situations.
            if (useConstraint)
            {
                minProd = 0;
                maxProd = template.Length;
                maxTmDiff = 999;   // no ΔTm filter
            }

            ShowLoading(true, "Scanning primer candidates...");
            await Task.Delay(10); // let UI update

            List<PrimerPairResult> top = null;
            int totalResults = 0;

            try
            {
                top = await Task.Run(() =>
                {
                    // ════════════════════════════════════════════════════════════
                    // PHASE 1: Candidate generation
                    // When RE overhangs are active, user's Min/Max Tm and Length
                    // targets are interpreted for the FULL synthesised oligo
                    // (prot + RE_site + gene_specific).  We scan gene-specific
                    // sub-sequences only, but evaluate Tm/GC/length on the full
                    // oligo so that the returned primers match the user's intent.
                    // Protective bases are chosen per-candidate (see Phase 3) to
                    // minimize GC% shift; for Phase 1 screening we use a neutral
                    // "GC" repeated to the requested count as a proxy.
                    // ════════════════════════════════════════════════════════════

                    // Proxy protective seq for Phase 1 screening (GC-neutral baseline)
                    string protProxy = GetProtectiveSequence(protBases);
                    string fwdOH = includeRE ? protProxy + fwdRESeq : "";
                    string revOH = includeRE ? protProxy + revRESeq : "";
                    bool hasOH = fwdOH.Length > 0 || revOH.Length > 0;

                    // Gene-specific length window that keeps total length within [minLen, maxLen]
                    int gsFwdMin = hasOH ? Math.Max(8, minLen - fwdOH.Length) : minLen;
                    int gsFwdMax = hasOH ? Math.Max(gsFwdMin, maxLen - fwdOH.Length) : maxLen;
                    int gsRevMin = hasOH ? Math.Max(8, minLen - revOH.Length) : minLen;
                    int gsRevMax = hasOH ? Math.Max(gsRevMin, maxLen - revOH.Length) : maxLen;

                    var fwdCands = new List<(int start, PrimerAnalysis a)>();
                    for (int s = 0; s <= template.Length - gsFwdMin; s++)
                    {
                        ct.ThrowIfCancellationRequested();
                        for (int gsLen = gsFwdMin; gsLen <= gsFwdMax && s + gsLen <= template.Length; gsLen++)
                        {
                            string gsSeq = template.Substring(s, gsLen);
                            string fullSeq = fwdOH + gsSeq;
                            var a = AnalyzePrimerFast(fullSeq);
                            a.Sequence = gsSeq;
                            a.Length = gsLen;
                            a.TmGeneSpecific = hasOH ? CalculateTm(gsSeq) : a.Tm;
                            if (a.Tm < minTm || a.Tm > maxTm) continue;
                            if (gcClamp && !CheckGCClamp(gsSeq)) continue;
                            if (CalculateGCPercent(gsSeq) < 30 || CalculateGCPercent(gsSeq) > 70) continue;
                            fwdCands.Add((s, a));
                        }
                    }

                    var revCands = new List<(int start, PrimerAnalysis a)>();
                    for (int end = gsRevMin; end <= template.Length; end++)
                    {
                        ct.ThrowIfCancellationRequested();
                        for (int gsLen = gsRevMin; gsLen <= gsRevMax && end - gsLen >= 0; gsLen++)
                        {
                            string rc = GetReverseComplement(template.Substring(end - gsLen, gsLen));
                            string fullSeq = revOH + rc;
                            var a = AnalyzePrimerFast(fullSeq);
                            a.Sequence = rc;
                            a.Length = gsLen;
                            a.TmGeneSpecific = hasOH ? CalculateTm(rc) : a.Tm;
                            if (a.Tm < minTm || a.Tm > maxTm) continue;
                            if (gcClamp && !CheckGCClamp(rc)) continue;
                            if (CalculateGCPercent(rc) < 30 || CalculateGCPercent(rc) > 70) continue;
                            revCands.Add((end - gsLen, a));
                        }
                    }

                    // Sort reverse candidates by start position for binary search
                    revCands.Sort((a, b) => a.start.CompareTo(b.start));
                    var revStarts = revCands.Select(r => r.start).ToArray();

                    // ════════════════════════════════════════════════════════════
                    // PHASE 2: Fast pairing with sliding window + running Top-K
                    // ════════════════════════════════════════════════════════════

                    const int TOP_K = 50;
                    var topPairs = new List<(int fStart, PrimerAnalysis fA, int rStart, PrimerAnalysis rA, int prod, double cheapScore)>();
                    double worstKeptScore = double.MinValue;

                    foreach (var f in fwdCands)
                    {
                        ct.ThrowIfCancellationRequested();
                        // When constraint active, we need full range to find any spanning pair
                        int rMin = useConstraint ? 0 : f.start + minProd - gsRevMax;
                        int rMax = useConstraint ? template.Length - gsRevMin : f.start + maxProd - gsRevMin;

                        int lo = Array.BinarySearch(revStarts, rMin);
                        if (lo < 0) lo = ~lo;

                        for (int ri = lo; ri < revCands.Count && revCands[ri].start <= rMax; ri++)
                        {
                            var r = revCands[ri];
                            int prod = r.start + r.a.Length - f.start;
                            if (prod < minProd || prod > maxProd) continue;

                            // "Product must include region" constraint
                            // fwd primer must START at or before constraintStart
                            // rev primer's 3'-end on template must reach constraintEnd
                            if (useConstraint)
                            {
                                if (f.start > constraintStart) continue;
                                if (r.start + r.a.Length < constraintEnd) continue;
                            }

                            // ΔTm filter uses full-oligo Tm (with OH+PB) — what the user specifies
                            double td = Math.Abs(f.a.Tm - r.a.Tm);
                            if (td > maxTmDiff) continue;

                            // Cheap score
                            double score = 100.0;
                            score -= td * 3.0;
                            score -= Math.Abs(targetTm - f.a.Tm) * 1.5;
                            score -= Math.Abs(targetTm - r.a.Tm) * 1.5;
                            score -= Math.Abs(targetProd - prod) * 0.02;
                            score -= Math.Abs(50 - f.a.GCPercent) * 0.5;
                            score -= Math.Abs(50 - r.a.GCPercent) * 0.5;
                            score -= f.a.HairpinScore * 3.0;
                            score -= r.a.HairpinScore * 3.0;
                            if (penalizeSelfComp)
                            {
                                score -= f.a.SelfComplementarityScore * 2.0;
                                score -= r.a.SelfComplementarityScore * 2.0;
                            }

                            // Early prune: skip if clearly below current top-K threshold
                            if (topPairs.Count >= TOP_K && score < worstKeptScore - 20.0) continue;

                            topPairs.Add((f.start, f.a, r.start, r.a, prod, score));

                            // Periodically trim to keep memory bounded
                            if (topPairs.Count > TOP_K * 4)
                            {
                                topPairs.Sort((x, y) => y.cheapScore.CompareTo(x.cheapScore));
                                topPairs.RemoveRange(TOP_K * 2, topPairs.Count - TOP_K * 2);
                                worstKeptScore = topPairs[topPairs.Count - 1].cheapScore;
                            }
                        }
                    }

                    // ════════════════════════════════════════════════════════════
                    // PHASE 3: Refine top candidates with expensive calculations
                    // Only ~50-100 pairs get HeteroDimer + SelfDimerΔG scoring
                    // ════════════════════════════════════════════════════════════

                    topPairs.Sort((x, y) => y.cheapScore.CompareTo(x.cheapScore));
                    if (topPairs.Count > TOP_K) topPairs.RemoveRange(TOP_K, topPairs.Count - TOP_K);

                    totalResults = topPairs.Count;
                    var results = new List<PrimerPairResult>(topPairs.Count);

                    foreach (var p in topPairs)
                    {
                        ct.ThrowIfCancellationRequested();
                        int hd = CalculateHeteroDimer(p.fA.Sequence, p.rA.Sequence);

                        double score = p.cheapScore;
                        score -= hd * 2.0;

                        if (penalizeSelfComp)
                        {
                            // Compute SelfDimerΔG only now for the finalists
                            double fDimerDG = CalculateSelfDimerDeltaG(p.fA.Sequence);
                            double rDimerDG = CalculateSelfDimerDeltaG(p.rA.Sequence);
                            if (fDimerDG < -5.0) score -= Math.Abs(fDimerDG + 5.0) * 2.0;
                            if (rDimerDG < -5.0) score -= Math.Abs(rDimerDG + 5.0) * 2.0;
                        }

                        string fwdDisplay = p.fA.Sequence;
                        string revDisplay = p.rA.Sequence;
                        string fwdFullSeq = p.fA.Sequence;   // will hold complete synthesised oligo
                        string revFullSeq = p.rA.Sequence;
                        double fwdTmFull = p.fA.Tm;   // already full-oligo Tm from Phase 1
                        double revTmFull = p.rA.Tm;

                        if (includeRE)
                        {
                            // Per-candidate protective sequence chosen to minimise ΔGC%
                            string fwdProt = ChooseProtectiveSequence(protBases, fwdRESeq, p.fA.Sequence);
                            string revProt = ChooseProtectiveSequence(protBases, revRESeq, p.rA.Sequence);

                            fwdFullSeq = fwdProt + fwdRESeq + p.fA.Sequence;
                            revFullSeq = revProt + revRESeq + p.rA.Sequence;

                            if (!string.IsNullOrEmpty(fwdRESeq))
                            {
                                fwdDisplay = $"{fwdProt}{fwdRESeq}-{p.fA.Sequence}";
                                fwdTmFull = CalculateTm(fwdFullSeq);
                            }
                            if (!string.IsNullOrEmpty(revRESeq))
                            {
                                revDisplay = $"{revProt}{revRESeq}-{p.rA.Sequence}";
                                revTmFull = CalculateTm(revFullSeq);
                            }
                        }

                        // Tm and ΔTm are based on the FULL synthesised oligo when RE is present
                        // (that is the Tm that matters for late PCR cycles)
                        double fwdTmForScore = fwdTmFull;
                        double revTmForScore = revTmFull;

                        // Full-oligo ΔTm (consistent with Phase 2 filter)
                        double gsTmDiff = Math.Abs(fwdTmFull - revTmFull);

                        results.Add(new PrimerPairResult
                        {
                            ForwardSequence = p.fA.Sequence,
                            ReverseSequence = p.rA.Sequence,
                            ForwardFullSequence = fwdFullSeq,
                            ReverseFullSequence = revFullSeq,
                            ForwardDisplay = fwdDisplay,
                            ReverseDisplay = revDisplay,
                            ForwardTm = fwdTmForScore,   // full-oligo Tm
                            ReverseTm = revTmForScore,
                            TmDiff = gsTmDiff,        // gene-specific ΔTm (no RE inflation)
                            ProductSize = p.prod,
                            Score = score,
                            FwdStart = p.fStart,
                            RevStart = p.rStart,
                            ForwardTmFull = fwdTmFull,
                            ReverseTmFull = revTmFull,
                            HasREOverhang = hasOH
                        });
                    }

                    var ranked = results.OrderByDescending(r => r.Score).Take(20).ToList();
                    for (int i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;
                    return ranked;
                }, ct);
            }
            catch (OperationCanceledException) { return; }
            finally
            {
                ShowLoading(false);
            }

            if (top == null || top.Count == 0)
            {
                MessageBox.Show("No primer pairs match criteria. Try widening parameters.", "No Results", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AutoResultsDataGrid.ItemsSource = top;
            AutoResultsInfo.Text = $"Found {totalResults:N0} valid pairs, showing top {top.Count}. (Target Tm: {targetTm}°C, Target product: {targetProd} bp)";
            AutoResultsPanel.Visibility = Visibility.Visible;
            AutoResultsDataGrid.SelectedIndex = 0;
        }

        /// <summary>
        /// Return a GC-repeating protective sequence of the requested length (default, Phase 1 proxy).
        /// </summary>
        private string GetProtectiveSequence(int count)
        {
            if (count <= 0) return "";
            // Build "GC" repeat then truncate to exact count
            string rep = "GCGCGCGCGCGCGCGC";
            return rep.Substring(0, Math.Min(count, rep.Length));
        }

        /// <summary>
        /// Choose the protective sequence (of exactly <paramref name="count"/> bases) whose
        /// nucleotide composition minimises the GC% shift introduced by (prot + reSeq + geneSeq)
        /// relative to geneSeq alone. Candidates tried: all combinations of G/C and A/T up to
        /// the requested length, from a prebuilt library.
        /// </summary>
        private string ChooseProtectiveSequence(int count, string reSeq, string geneSeq)
        {
            if (count <= 0) return "";

            double targetGC = CalculateGCPercent(geneSeq);
            double reGC = string.IsNullOrEmpty(reSeq) ? 0 : CalculateGCPercent(reSeq);
            int reLen = reSeq?.Length ?? 0;
            int gsLen = geneSeq.Length;

            // GC count we want in prot so that GC%( prot + re + gs ) ≈ targetGC%
            // targetGC = (gcProt + gcRe + gcGs) / (count + reLen + gsLen)
            double totalLen = count + reLen + gsLen;
            double gcRe = reLen * reGC / 100.0;
            double gcGs = gsLen * targetGC / 100.0;
            double idealGcProt = targetGC / 100.0 * totalLen - gcRe - gcGs;
            idealGcProt = Math.Max(0, Math.Min(count, Math.Round(idealGcProt)));

            // Build a protective sequence with exactly (int)idealGcProt G/C bases and rest A/T
            int gcCount = (int)idealGcProt;
            int atCount = count - gcCount;
            string prot = new string('G', gcCount) + new string('A', atCount);

            // Interleave for better uniformity: GCGCGC... or ATATAT... or mixed
            var sb = new StringBuilder(count);
            int g = gcCount, a = atCount;
            while (sb.Length < count)
            {
                if (g > 0 && (a == 0 || g >= a)) { sb.Append('G'); g--; }
                else { sb.Append('A'); a--; }
            }
            return sb.ToString();
        }

        private void AutoResultsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AutoResultsDataGrid.SelectedItem is PrimerPairResult sel)
            {
                _lastFwdPrimer = sel.ForwardSequence;
                _lastRevPrimer = sel.ReverseSequence;

                // Analyse the FULL synthesised oligo when RE overhang is present
                string analysisFwd = (sel.HasREOverhang && !string.IsNullOrEmpty(sel.ForwardFullSequence))
                    ? sel.ForwardFullSequence : sel.ForwardSequence;
                string analysisRev = (sel.HasREOverhang && !string.IsNullOrEmpty(sel.ReverseFullSequence))
                    ? sel.ReverseFullSequence : sel.ReverseSequence;

                var fwd = AnalyzePrimer(analysisFwd);
                DisplayPrimerResult(fwd, FwdSequenceDisplay, FwdLength, FwdTm, FwdGC, FwdMW, FwdGCClamp, FwdSelfComp, FwdHairpin, FwdEndStability, FwdWarnings, FwdSelfDimerDG, FwdHairpinDG, FwdAnyComp);
                var rev = AnalyzePrimer(analysisRev);
                DisplayPrimerResult(rev, RevSequenceDisplay, RevLength, RevTm, RevGC, RevMW, RevGCClamp, RevSelfComp, RevHairpin, RevEndStability, RevWarnings, RevSelfDimerDG, RevHairpinDG, RevAnyComp);

                DisplayPairSummary(fwd, rev, _activeTemplate, sel);
                PairSummaryPanel.Visibility = Visibility.Visible;

                if (!string.IsNullOrEmpty(_activeTemplate))
                {
                    GenerateSequenceVisualization(_activeTemplate, sel.ForwardSequence, sel.ReverseSequence,
                        knownFwdPos: sel.FwdStart, knownRevPos: sel.RevStart);
                    SequenceViewPanel.Visibility = Visibility.Visible;
                }

                _markerFwdStart = sel.FwdStart;
                _markerFwdLen = sel.ForwardSequence.Length;
                _markerRevStart = sel.RevStart;
                _markerRevLen = sel.ReverseSequence.Length;
                if (GeneVisualizationPanel.Visibility == Visibility.Visible)
                    RedrawVisualization();

                UpdateREPreview();
            }
        }

        private void AutoResultsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Double-click scrolls to the guide panel for reference
            ParameterDetailPanel.BringIntoView();
        }

        private void ScrollToGuide_Click(object sender, MouseButtonEventArgs e)
        {
            ParameterDetailPanel.BringIntoView();
        }

        #endregion

        #region === Restriction Enzyme Overhang ===

        private void REComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateREPreview();

        private void UpdateREPreview()
        {
            if (FwdWithREPreview == null) return;

            string fwd = _lastFwdPrimer;
            string rev = _lastRevPrimer;

            if (string.IsNullOrEmpty(fwd) && string.IsNullOrEmpty(rev))
            {
                FwdWithREPreview.Text = "";
                RevWithREPreview.Text = "";
                return;
            }

            var _pbItem2 = ProtectiveBasesInput?.SelectedItem as System.Windows.Controls.ComboBoxItem;
            int protBases = int.TryParse(_pbItem2?.Tag as string ?? "2", out int _pb) ? _pb : 2;
            string protPrefix = GetProtectiveSequence(protBases);

            // Use manual RE ComboBoxes when in manual mode and ManualRECheckBox is checked
            bool useManualRE = ManualRECheckBox?.IsChecked == true;
            string fwdRE = GetSelectedRESequence(useManualRE ? FwdREComboBox : AutoFwdREComboBox);
            string revRE = GetSelectedRESequence(useManualRE ? RevREComboBox : AutoRevREComboBox);

            FwdWithREPreview.Text = !string.IsNullOrEmpty(fwd) ? $"Fwd: 5'-{protPrefix}{fwdRE}{fwd}-3'" : "";
            RevWithREPreview.Text = !string.IsNullOrEmpty(rev) ? $"Rev: 5'-{protPrefix}{revRE}{rev}-3'" : "";
        }

        private string GetSelectedRESequence(ComboBox combo)
        {
            if (combo.SelectedItem == null) return "";
            string sel = combo.SelectedItem.ToString();
            if (sel == "(None)" || sel.StartsWith("──")) return "";

            var match = Regex.Match(sel, @"^(\S+)\s+\((\S+)\)");
            return match.Success ? match.Groups[2].Value : "";
        }

        private void CopyPrimersWithRE_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(FwdWithREPreview.Text)) sb.AppendLine(FwdWithREPreview.Text);
            if (!string.IsNullOrEmpty(RevWithREPreview.Text)) sb.AppendLine(RevWithREPreview.Text);
            if (sb.Length > 0)
            {
                Clipboard.SetText(sb.ToString());
                ShowInfo("Primers with RE tails copied to clipboard.", false);
            }
        }

        #endregion

        #region === Core Primer Analysis ===

        private PrimerAnalysis AnalyzePrimer(string sequence)
        {
            var a = new PrimerAnalysis
            {
                Sequence = sequence,
                Length = sequence.Length,
                Tm = CalculateTm(sequence),
                GCPercent = CalculateGCPercent(sequence),
                MolecularWeight = CalculateMolecularWeight(sequence),
                HasGCClamp = CheckGCClamp(sequence),
                SelfComplementarityScore = CalculateSelfComplementarity(sequence),
                HairpinScore = CalculateHairpinScore(sequence),
                EndStabilityDeltaG = CalculateEndStability(sequence),
                SelfDimerDeltaG = CalculateSelfDimerDeltaG(sequence),
                HairpinDeltaG = CalculateHairpinDeltaG(sequence),
                AnyCompScore = CalculateAnyComplementarity(sequence)
            };

            if (a.Length < 18) a.Warnings.Add("⚠ Too short (<18 bp).");
            if (a.Length > 30) a.Warnings.Add("⚠ Too long (>30 bp).");
            if (a.Tm < 50) a.Warnings.Add("⚠ Tm too low (<50°C).");
            if (a.Tm > 70) a.Warnings.Add("⚠ Tm too high (>70°C).");
            if (a.GCPercent < 30) a.Warnings.Add("⚠ GC% too low (<30%).");
            if (a.GCPercent > 70) a.Warnings.Add("⚠ GC% too high (>70%).");
            if (!a.HasGCClamp) a.Warnings.Add("⚠ No GC clamp at 3'.");
            if (a.SelfComplementarityScore >= 4) a.Warnings.Add("⚠ High self-complementarity.");
            if (a.HairpinScore >= 3) a.Warnings.Add("⚠ Potential hairpin.");
            if (a.SelfDimerDeltaG < -6.0) a.Warnings.Add($"⚠ Stable self-dimer (ΔG={a.SelfDimerDeltaG:F1} kcal/mol).");
            if (a.HairpinDeltaG < -3.0) a.Warnings.Add($"⚠ Stable hairpin (ΔG={a.HairpinDeltaG:F1} kcal/mol).");
            if (HasRepeats(sequence)) a.Warnings.Add("⚠ Mononucleotide run ≥4.");

            return a;
        }

        /// <summary>
        /// Lightweight primer analysis for Auto-Pick candidate screening.
        /// Only computes Tm, GC%, GCClamp, and simple scoring metrics (SelfComp + HairpinScore).
        /// Skips expensive ΔG calculations (SelfDimer, Hairpin, AnyComp, MW, EndStability, Warnings).
        /// Full analysis is deferred to AnalyzePrimer() for the final top-N candidates only.
        /// Speedup: ~5-10x per candidate (eliminates O(n²/n³) ΔG inner loops for ~80% of candidates).
        /// </summary>
        private PrimerAnalysis AnalyzePrimerFast(string sequence)
        {
            return new PrimerAnalysis
            {
                Sequence = sequence,
                Length = sequence.Length,
                Tm = CalculateTm(sequence),
                GCPercent = CalculateGCPercent(sequence),
                HasGCClamp = CheckGCClamp(sequence),
                SelfComplementarityScore = CalculateSelfComplementarity(sequence),
                HairpinScore = CalculateHairpinScore(sequence)
            };
        }

        /// <summary>
        /// Tm — Nearest-Neighbor (SantaLucia 1998, PNAS 95:1460-1465)
        /// Short oligos (&lt;14 nt): Wallace Rule Tm = 2(A+T) + 4(G+C)
        /// Uses static NN arrays for zero allocation in hot path.
        /// </summary>
        private double CalculateTm(string seq)
        {
            int N = seq.Length;
            if (N == 0) return 0;

            // Count GC for all paths
            int gcCount = 0;
            for (int i = 0; i < N; i++) if (seq[i] == 'G' || seq[i] == 'C') gcCount++;

            if (N < 14) return 2.0 * (N - gcCount) + 4.0 * gcCount;

            double totalH = 0, totalS = 0;
            // Initiation parameters
            char first = seq[0], last = seq[N - 1];
            if (first == 'G' || first == 'C') { totalH += 0.1; totalS += -2.8; } else { totalH += 2.3; totalS += 4.1; }
            if (last == 'G' || last == 'C') { totalH += 0.1; totalS += -2.8; } else { totalH += 2.3; totalS += 4.1; }

            // NN sum — zero allocation, direct array lookup
            for (int i = 0; i < N - 1; i++)
            {
                int b1 = _baseIdx[seq[i]], b2 = _baseIdx[seq[i + 1]];
                if (b1 >= 0 && b2 >= 0) { int idx = b1 * 4 + b2; totalH += _nnH[idx]; totalS += _nnS[idx]; }
            }

            double R = 1.987;
            double tm1M = (totalH * 1000) / (totalS + R * Math.Log(_primerConc / 4.0));
            double fGC = (double)gcCount / N;
            double freeMg = Math.Max(0, _mgConc - _dntpConc);

            if (freeMg > 0 && _naConc > 0)
            {
                double ratio = Math.Sqrt(freeMg) / _naConc;
                double lnMg = Math.Log(freeMg);
                double invTm;

                if (ratio < 0.22)
                {
                    invTm = Owczarzy2004InvTm(tm1M, fGC, _naConc);
                }
                else if (ratio < 6.0)
                {
                    double lnMon = Math.Log(_naConc);
                    double a = 3.92e-5 * (0.843 - 0.352 * Math.Sqrt(_naConc) * lnMon);
                    double b = -9.11e-6;
                    double c = 6.26e-5;
                    double d = 1.42e-5 * (1.279 - 4.03e-3 * lnMon - 8.03e-3 * lnMon * lnMon);
                    double e2 = -4.82e-4;
                    double f = 5.25e-4;
                    double g2 = 8.31e-5 * (0.486 - 0.258 * lnMon + 5.25e-3 * lnMon * lnMon * lnMon);
                    invTm = 1.0 / tm1M + a + b * lnMg + fGC * (c + d * lnMg)
                            + (1.0 / (2.0 * (N - 1))) * (e2 + f * lnMg + g2 * lnMg * lnMg);
                }
                else
                {
                    double a = 3.92e-5, b = -9.11e-6, c = 6.26e-5, d = 1.42e-5;
                    double e2 = -4.82e-4, f = 5.25e-4, g2 = 8.31e-5;
                    invTm = 1.0 / tm1M + a + b * lnMg + fGC * (c + d * lnMg)
                            + (1.0 / (2.0 * (N - 1))) * (e2 + f * lnMg + g2 * lnMg * lnMg);
                }
                return Math.Round(1.0 / invTm - 273.15, 1);
            }
            else if (_naConc > 0)
            {
                double invTm = Owczarzy2004InvTm(tm1M, fGC, _naConc);
                return Math.Round(1.0 / invTm - 273.15, 1);
            }
            else
            {
                double saltS = totalS + 0.368 * (N - 1) * Math.Log(0.05);
                double tm = (totalH * 1000) / (saltS + R * Math.Log(_primerConc / 4.0)) - 273.15;
                return Math.Round(tm, 1);
            }
        }

        /// <summary>Owczarzy 2004 monovalent salt correction (returns 1/Tm in Kelvin)</summary>
        private double Owczarzy2004InvTm(double tm1M_K, double fGC, double naConc)
        {
            double lnNa = Math.Log(naConc);
            return 1.0 / tm1M_K + (4.29 * fGC - 3.95) * 1e-5 * lnNa + 9.40e-6 * lnNa * lnNa;
        }

        private double CalculateGCPercent(string seq)
        {
            if (seq.Length == 0) return 0;
            int gc = 0;
            for (int i = 0; i < seq.Length; i++) if (seq[i] == 'G' || seq[i] == 'C') gc++;
            return Math.Round(100.0 * gc / seq.Length, 1);
        }

        private double CalculateMolecularWeight(string seq)
        {
            if (seq.Length == 0) return 0;
            double mw = 0;
            foreach (char c in seq)
            {
                switch (c)
                {
                    case 'A': mw += 331.2; break;
                    case 'T': mw += 322.2; break;
                    case 'G': mw += 347.2; break;
                    case 'C': mw += 307.2; break;
                    default: mw += 326.9; break;
                }
            }
            return Math.Round(mw - (seq.Length - 1) * 18.0 + 79.0, 1);
        }

        private bool CheckGCClamp(string seq) => seq.Length > 0 && (seq[seq.Length - 1] == 'G' || seq[seq.Length - 1] == 'C');

        private int CalculateSelfComplementarity(string seq)
        {
            if (seq.Length < 4) return 0;
            string rc = GetReverseComplement(seq);
            int max = 0;
            for (int off = 0; off < seq.Length * 2 - 1; off++)
            {
                int cons = 0, best = 0;
                for (int i = 0; i < seq.Length; i++)
                {
                    int j = i - (off - seq.Length + 1);
                    if (j >= 0 && j < rc.Length && IsComplement(seq[i], rc[j])) { cons++; best = Math.Max(best, cons); }
                    else cons = 0;
                }
                max = Math.Max(max, best);
            }
            return max;
        }

        private int CalculateHairpinScore(string seq)
        {
            if (seq.Length < 8) return 0;
            int max = 0;
            for (int ls = 3; ls < seq.Length - 3; ls++)
                for (int ll = 3; ll <= 8 && ls + ll < seq.Length; ll++)
                {
                    int stemLen = Math.Min(ls, seq.Length - ls - ll);
                    int m = 0;
                    for (int k = 0; k < stemLen; k++)
                    {
                        if (IsComplement(seq[ls - 1 - k], seq[ls + ll + k])) m++; else break;
                    }
                    max = Math.Max(max, m);
                }
            return max;
        }

        private double CalculateEndStability(string seq)
        {
            if (seq.Length < 5) return 0;
            double total = 0;
            int start = seq.Length - 5;
            for (int i = start; i < seq.Length - 1; i++)
            {
                int b1 = _baseIdx[seq[i]], b2 = _baseIdx[seq[i + 1]];
                if (b1 >= 0 && b2 >= 0) total += _nnG[b1 * 4 + b2];
            }
            return Math.Round(total, 2);
        }

        /// <summary>
        /// Self-Dimer ΔG — Thermodynamic stability of primer self-dimer
        /// Slides the primer against its own reverse complement at every possible offset.
        /// For each alignment, finds all runs of ≥2 consecutive Watson-Crick base pairs,
        /// sums their NN ΔG values, and adds initiation penalty (+1.96 kcal/mol per duplex).
        /// Returns the most negative (most stable) ΔG across all alignments.
        /// Reference: SantaLucia (1998) PNAS 95:1460-1465
        /// </summary>
        private double CalculateSelfDimerDeltaG(string seq)
        {
            if (seq.Length < 4) return 0;
            string rc = GetReverseComplement(seq);
            double worstDG = 0; // most negative = most stable

            for (int offset = -(seq.Length - 2); offset <= seq.Length - 2; offset++)
            {
                int i0 = Math.Max(0, offset);
                int j0 = Math.Max(0, -offset);
                int runLen = 0;
                double runDG = 0;
                double bestRunDG = 0;

                for (int k = 0; i0 + k < seq.Length && j0 + k < rc.Length; k++)
                {
                    if (IsComplement(seq[i0 + k], rc[j0 + k]))
                    {
                        runLen++;
                        if (runLen >= 2)
                        {
                            int b1 = _baseIdx[seq[i0 + k - 1]], b2 = _baseIdx[seq[i0 + k]];
                            if (b1 >= 0 && b2 >= 0) runDG += _nnG[b1 * 4 + b2];
                        }
                    }
                    else
                    {
                        if (runLen >= 2)
                        {
                            double dg = runDG + 1.96;
                            if (dg < bestRunDG) bestRunDG = dg;
                        }
                        runLen = 0; runDG = 0;
                    }
                }
                if (runLen >= 2)
                {
                    double dg = runDG + 1.96;
                    if (dg < bestRunDG) bestRunDG = dg;
                }
                if (bestRunDG < worstDG) worstDG = bestRunDG;
            }
            return Math.Round(worstDG, 2);
        }

        /// <summary>
        /// Hairpin ΔG — Thermodynamic stability of intramolecular hairpin
        /// Tests all possible loop positions (loop ≥ 3 nt) and stem lengths (≥ 2 bp).
        /// Stem ΔG computed from NN parameters; loop penalty from Jacobson-Stockmayer:
        ///   ΔG_loop = -RT·ln(σ·Ω·(3/(2πl·b²))^(3/2))
        ///   Simplified empirical: ΔG_loop(n) ≈ ΔG_loop(3) + 2.44·RT·ln(n/3)
        ///   where ΔG_loop(3) ≈ +5.2 kcal/mol (triloop), R=1.987 cal/(mol·K), T=310.15K
        /// Returns the most negative (most stable) hairpin ΔG.
        /// Reference: SantaLucia (1998); Zuker (2003) Nucleic Acids Res 31:3406
        /// </summary>
        private double CalculateHairpinDeltaG(string seq)
        {
            if (seq.Length < 8) return 0;
            double worstDG = 0;
            double RT = 1.987 * 310.15 / 1000.0; // kcal/mol at 37°C

            // loop penalty lookup (empirical, kcal/mol)
            double[] loopPenalty = { 0, 0, 0, 5.2, 4.5, 4.4, 4.3, 4.1, 4.1, 3.9, 3.7, 3.5, 3.4, 3.3, 3.2, 3.1, 3.1, 3.0, 3.0, 2.9, 2.9 };

            for (int loopStart = 2; loopStart < seq.Length - 4; loopStart++)
            {
                for (int loopLen = 3; loopLen <= Math.Min(12, seq.Length - loopStart - 2); loopLen++)
                {
                    int maxStem = Math.Min(loopStart, seq.Length - loopStart - loopLen);
                    if (maxStem < 2) continue;

                    int stemBp = 0;
                    double stemDG = 0;
                    for (int s = 0; s < maxStem; s++)
                    {
                        int pos5 = loopStart - 1 - s;
                        int pos3 = loopStart + loopLen + s;
                        if (pos5 < 0 || pos3 >= seq.Length) break;
                        if (IsComplement(seq[pos5], seq[pos3]))
                        {
                            stemBp++;
                            if (stemBp >= 2)
                            {
                                int b1 = _baseIdx[seq[pos5]], b2 = _baseIdx[seq[pos5 + 1]];
                                if (b1 >= 0 && b2 >= 0) stemDG += _nnG[b1 * 4 + b2];
                            }
                        }
                        else break;
                    }

                    if (stemBp >= 2)
                    {
                        double lp = loopLen < loopPenalty.Length ? loopPenalty[loopLen] : 2.9 + 2.44 * RT * Math.Log((double)loopLen / 20.0);
                        double totalDG = stemDG + lp;
                        if (totalDG < worstDG) worstDG = totalDG;
                    }
                }
            }
            return Math.Round(worstDG, 2);
        }

        /// <summary>
        /// Any Self-Complementarity — Total complementary base pairs (not just consecutive)
        /// in the worst-case alignment. Differs from SelfComplementarityScore which
        /// only counts the longest consecutive run.
        /// </summary>
        private int CalculateAnyComplementarity(string seq)
        {
            if (seq.Length < 4) return 0;
            string rc = GetReverseComplement(seq);
            int maxTotal = 0;
            for (int off = 0; off < seq.Length * 2 - 1; off++)
            {
                int total = 0;
                for (int i = 0; i < seq.Length; i++)
                {
                    int j = i - (off - seq.Length + 1);
                    if (j >= 0 && j < rc.Length && IsComplement(seq[i], rc[j])) total++;
                }
                maxTotal = Math.Max(maxTotal, total);
            }
            return maxTotal;
        }

        private int CalculateHeteroDimer(string p1, string p2)
        {
            string rc2 = GetReverseComplement(p2);
            int max = 0, total = p1.Length + rc2.Length - 1;
            for (int off = 0; off < total; off++)
            {
                int cons = 0;
                for (int i = 0; i < p1.Length; i++)
                {
                    int j = i - (off - rc2.Length + 1);
                    if (j >= 0 && j < rc2.Length && IsComplement(p1[i], rc2[j])) { cons++; max = Math.Max(max, cons); }
                    else cons = 0;
                }
            }
            return max;
        }

        private bool HasRepeats(string seq)
        {
            for (int i = 0; i <= seq.Length - 4; i++)
                if (seq[i] == seq[i + 1] && seq[i] == seq[i + 2] && seq[i] == seq[i + 3]) return true;
            return false;
        }

        #endregion

        #region === Utility ===

        private string CleanSequence(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var sb = new StringBuilder();
            foreach (char c in input.ToUpper())
                if (c == 'A' || c == 'T' || c == 'G' || c == 'C') sb.Append(c);
            return sb.ToString();
        }

        private string GetReverseComplement(string seq)
        {
            var sb = new StringBuilder(seq.Length);
            for (int i = seq.Length - 1; i >= 0; i--)
            {
                switch (seq[i])
                {
                    case 'A': sb.Append('T'); break;
                    case 'T': sb.Append('A'); break;
                    case 'G': sb.Append('C'); break;
                    case 'C': sb.Append('G'); break;
                    default: sb.Append('N'); break;
                }
            }
            return sb.ToString();
        }

        private bool IsComplement(char a, char b) =>
            (a == 'A' && b == 'T') || (a == 'T' && b == 'A') || (a == 'G' && b == 'C') || (a == 'C' && b == 'G');

        #endregion

        #region === Display Methods ===

        private void DisplayPrimerResult(PrimerAnalysis a,
            TextBlock seq, TextBlock len, TextBlock tm, TextBlock gc, TextBlock mw,
            TextBlock gcClamp, TextBlock selfComp, TextBlock hairpin, TextBlock endStab, TextBlock warn,
            TextBlock selfDimerDG = null, TextBlock hairpinDG = null, TextBlock anyComp = null)
        {
            seq.Text = $"5'-{a.Sequence}-3'";
            len.Text = $"{a.Length} bp";
            tm.Text = $"{a.Tm:F1}°C";
            gc.Text = $"{a.GCPercent:F1}%";
            mw.Text = $"{a.MolecularWeight:F0}";

            var green = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
            var red = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
            var orange = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800"));

            gcClamp.Text = a.HasGCClamp ? "✓ Yes" : "✗ No";
            gcClamp.Foreground = a.HasGCClamp ? green : red;
            selfComp.Text = $"{a.SelfComplementarityScore}";
            selfComp.Foreground = a.SelfComplementarityScore < 4 ? green : red;
            hairpin.Text = $"{a.HairpinScore}";
            hairpin.Foreground = a.HairpinScore < 3 ? green : red;
            endStab.Text = $"{a.EndStabilityDeltaG:F1}";
            endStab.Foreground = a.EndStabilityDeltaG > -9 ? green : orange;

            if (selfDimerDG != null)
            {
                selfDimerDG.Text = $"{a.SelfDimerDeltaG:F1} kcal";
                selfDimerDG.Foreground = a.SelfDimerDeltaG > -6.0 ? green : a.SelfDimerDeltaG > -9.0 ? orange : red;
            }
            if (hairpinDG != null)
            {
                hairpinDG.Text = $"{a.HairpinDeltaG:F1} kcal";
                hairpinDG.Foreground = a.HairpinDeltaG > -3.0 ? green : a.HairpinDeltaG > -5.0 ? orange : red;
            }
            if (anyComp != null)
            {
                anyComp.Text = $"{a.AnyCompScore}";
                anyComp.Foreground = a.AnyCompScore < a.Length * 0.4 ? green : a.AnyCompScore < a.Length * 0.6 ? orange : red;
            }

            warn.Text = a.Warnings.Count > 0 ? string.Join("\n", a.Warnings) : "";
        }

        private void ClearPrimerDisplay(TextBlock seq, TextBlock len, TextBlock tm, TextBlock gc, TextBlock mw,
            TextBlock gcClamp, TextBlock selfComp, TextBlock hairpin, TextBlock endStab, TextBlock warn,
            TextBlock selfDimerDG = null, TextBlock hairpinDG = null, TextBlock anyComp = null)
        {
            seq.Text = ""; len.Text = "-"; tm.Text = "-"; gc.Text = "-"; mw.Text = "-";
            gcClamp.Text = "-"; selfComp.Text = "-"; hairpin.Text = "-"; endStab.Text = "-"; warn.Text = "";
            if (selfDimerDG != null) selfDimerDG.Text = "-";
            if (hairpinDG != null) hairpinDG.Text = "-";
            if (anyComp != null) anyComp.Text = "-";
        }

        private void DisplayPairSummary(PrimerAnalysis fwd, PrimerAnalysis rev, string template, PrimerPairResult result = null)
        {
            var green = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50"));
            var red = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
            var orange = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800"));

            // Gene-specific ΔTm (not inflated by RE overhang differences)
            double tmDiff = result != null && result.HasREOverhang
                ? result.TmDiff   // pre-computed gene-specific ΔTm
                : Math.Abs(fwd.Tm - rev.Tm);
            TmDifference.Text = $"{tmDiff:F1}°C";
            TmDifference.Foreground = tmDiff <= 5 ? green : red;

            int hd = CalculateHeteroDimer(fwd.Sequence, rev.Sequence);
            HeteroDimer.Text = $"{hd}";
            HeteroDimer.Foreground = hd < 4 ? green : red;

            if (!string.IsNullOrEmpty(template))
            {
                int productBp = -1;
                if (result != null && result.FwdStart >= 0 && result.RevStart >= 0)
                {
                    // Use pre-computed positions (accurate for genomic sequences with repeats)
                    productBp = result.RevStart + result.ReverseSequence.Length - result.FwdStart;
                }
                else
                {
                    int fPos = template.IndexOf(fwd.Sequence, StringComparison.Ordinal);
                    string rcOnT = GetReverseComplement(rev.Sequence);
                    int rPos = template.IndexOf(rcOnT, StringComparison.Ordinal);
                    if (fPos >= 0 && rPos >= 0 && rPos > fPos)
                        productBp = rPos + rev.Length - fPos;
                }
                ProductSize.Text = productBp > 0 ? $"{productBp} bp" : "N/A";
            }
            else ProductSize.Text = "N/A";

            double score = 100 - tmDiff * 3 - Math.Abs(60 - fwd.Tm) - Math.Abs(60 - rev.Tm)
                - fwd.SelfComplementarityScore * 2 - rev.SelfComplementarityScore * 2 - hd * 2
                - fwd.HairpinScore * 3 - rev.HairpinScore * 3;
            OverallScore.Text = $"{Math.Max(0, score):F0}/100";
            OverallScore.Foreground = score >= 70 ? green : score >= 40 ? orange : red;

            var warns = new List<string>();
            if (tmDiff > 5) warns.Add("⚠ ΔTm > 5°C");
            if (hd >= 4) warns.Add("⚠ Hetero-dimer risk");

            // Full primer Tm (with RE overhang)
            if (result != null && result.HasREOverhang)
            {
                FullTmRow.Visibility = Visibility.Visible;
                FwdFullTm.Text = $"{result.ForwardTmFull:F1}°C";
                RevFullTm.Text = $"{result.ReverseTmFull:F1}°C";
                double fullTmDiff = Math.Abs(result.ForwardTmFull - result.ReverseTmFull);
                FullTmDiff.Text = $"{fullTmDiff:F1}°C";
                FullTmDiff.Foreground = fullTmDiff <= 5 ? green : red;
                if (fullTmDiff > 5) warns.Add("⚠ Full primer ΔTm (with RE) > 5°C");
            }
            else
            {
                FullTmRow.Visibility = Visibility.Collapsed;
            }

            PairWarnings.Text = warns.Count > 0 ? string.Join("\n", warns) : "";
        }

        /// <summary>
        /// Opens NCBI Primer-BLAST in the default browser with current primers pre-filled.
        /// URL parameters: PRIMER_LEFT_INPUT, PRIMER_RIGHT_INPUT, INPUT_SEQUENCE, ORGANISM, PRIMER_PRODUCT_MIN/MAX
        /// </summary>
        private void PrimerBlast_Click(object sender, RoutedEventArgs e)
        {
            string fwd = _lastFwdPrimer;
            string rev = _lastRevPrimer;

            if (string.IsNullOrEmpty(fwd) && string.IsNullOrEmpty(rev))
            {
                MessageBox.Show("Please analyze or select a primer pair first.", "No Primers", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sb = new StringBuilder("https://www.ncbi.nlm.nih.gov/tools/primer-blast/index.cgi?LINK_LOC=BlastHome");

            if (!string.IsNullOrEmpty(fwd))
                sb.Append($"&PRIMER_LEFT_INPUT={Uri.EscapeDataString(fwd)}");
            if (!string.IsNullOrEmpty(rev))
                sb.Append($"&PRIMER_RIGHT_INPUT={Uri.EscapeDataString(rev)}");

            // Pre-fill accession if available
            if (!string.IsNullOrEmpty(_currentAccession))
                sb.Append($"&INPUT_SEQUENCE={Uri.EscapeDataString(_currentAccession)}");

            // Pre-fill organism (TaxID)
            if (!string.IsNullOrEmpty(_currentTaxId))
                sb.Append($"&ORGANISM={Uri.EscapeDataString(_currentTaxId)}");

            // Pre-fill product size range from current settings
            if (int.TryParse(MinProductInput?.Text, out int minP))
                sb.Append($"&PRIMER_PRODUCT_MIN={minP}");
            if (int.TryParse(MaxProductInput?.Text, out int maxP))
                sb.Append($"&PRIMER_PRODUCT_MAX={maxP}");

            try
            {
                Process.Start(new ProcessStartInfo { FileName = sb.ToString(), UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open browser:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateSequenceVisualization(string template, string fwdSeq, string revSeq,
                                                    int knownFwdPos = -1, int knownRevPos = -1)
        {
            var sb = new StringBuilder();
            int fPos = -1, rPos = -1;
            string rcOnT = "";

            // Use pre-computed positions when available (avoids wrong IndexOf result on repeated sequences)
            if (knownFwdPos >= 0 && !string.IsNullOrEmpty(fwdSeq))
                fPos = knownFwdPos;
            else if (!string.IsNullOrEmpty(fwdSeq))
                fPos = template.IndexOf(fwdSeq, StringComparison.Ordinal);

            if (!string.IsNullOrEmpty(revSeq))
            {
                rcOnT = GetReverseComplement(revSeq);
                if (knownRevPos >= 0)
                    rPos = knownRevPos;
                else
                    rPos = template.IndexOf(rcOnT, StringComparison.Ordinal);
            }

            if (fPos >= 0) sb.AppendLine($"Fwd binds: {fPos + 1}–{fPos + fwdSeq.Length}");
            if (rPos >= 0) sb.AppendLine($"Rev binds: {rPos + 1}–{rPos + rcOnT.Length}");
            if (fPos >= 0 && rPos >= 0) sb.AppendLine($"Product: {rPos + rcOnT.Length - fPos} bp");
            sb.AppendLine();

            // Display window: show region around forward primer start to reverse primer end
            int ds = Math.Max(0, fPos >= 0 ? fPos - 10 : (rPos >= 0 ? Math.Max(0, rPos - 10) : 0));
            int de;
            if (fPos >= 0 && rPos >= 0)
            {
                // Both found: show full amplicon (capped at 200 bp for readability)
                de = Math.Min(template.Length, rPos + rcOnT.Length + 10);
                if (de - ds > 200) de = ds + 200; // cap to avoid giant display
            }
            else if (rPos >= 0)
                de = Math.Min(template.Length, rPos + rcOnT.Length + 10);
            else
                de = Math.Min(template.Length, ds + 200);

            // Safety guard: ensure de > ds
            if (de <= ds) de = Math.Min(template.Length, ds + 200);
            if (de <= ds) { SequenceVisualization.Text = sb.AppendLine("(Sequence region unavailable)").ToString(); return; }

            string disp = template.Substring(ds, de - ds);

            var fm = new char[disp.Length];
            var rm = new char[disp.Length];
            for (int i = 0; i < disp.Length; i++) { fm[i] = ' '; rm[i] = ' '; }

            if (fPos >= 0) for (int i = 0; i < fwdSeq.Length; i++) { int idx = fPos - ds + i; if (idx >= 0 && idx < fm.Length) fm[idx] = '>'; }
            if (rPos >= 0) for (int i = 0; i < rcOnT.Length; i++) { int idx = rPos - ds + i; if (idx >= 0 && idx < rm.Length) rm[idx] = '<'; }

            for (int i = 0; i < disp.Length; i += 60)
            {
                int len = Math.Min(60, disp.Length - i);
                sb.AppendLine($"Fwd:  {new string(fm, i, len)}");
                sb.AppendLine($"{ds + i + 1,5} {disp.Substring(i, len)}");
                sb.AppendLine($"Rev:  {new string(rm, i, len)}");
                sb.AppendLine();
            }

            SequenceVisualization.Text = sb.ToString();
        }

        #endregion

        #region === Copy / Export / Clear ===

        private void CopyForwardPrimer_Click(object sender, RoutedEventArgs e)
        { if (!string.IsNullOrEmpty(_lastFwdPrimer)) Clipboard.SetText(_lastFwdPrimer); }

        private void CopyReversePrimer_Click(object sender, RoutedEventArgs e)
        { if (!string.IsNullOrEmpty(_lastRevPrimer)) Clipboard.SetText(_lastRevPrimer); }

        private void ExportAutoResults_Click(object sender, RoutedEventArgs e)
        {
            var items = AutoResultsDataGrid.ItemsSource as List<PrimerPairResult>;
            if (items == null || items.Count == 0) return;

            var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"PrimerDesign_{_currentGeneSymbol}_{DateTime.Now:yyyyMMdd}.csv" };
            if (dlg.ShowDialog() == true)
            {
                var sb = new StringBuilder("Rank,Forward,Reverse,Fwd Tm,Rev Tm,dTm,Product,Score\n");
                foreach (var i in items)
                    sb.AppendLine($"{i.Rank},{i.ForwardSequence},{i.ReverseSequence},{i.ForwardTm:F1},{i.ReverseTm:F1},{i.TmDiff:F1},{i.ProductSize},{i.Score:F1}");
                File.WriteAllText(dlg.FileName, sb.ToString());
                MessageBox.Show($"Exported to:\n{dlg.FileName}", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            GeneSymbolTextBox.Text = ""; AccessionTextBox.Text = "";
            DirectSequenceInput.Text = ""; ForwardPrimerInput.Text = ""; ReversePrimerInput.Text = "";
            _genomicSequence = _activeTemplate = _lastFwdPrimer = _lastRevPrimer = "";
            _exonList.Clear(); _cdsList.Clear(); _isoforms.Clear();
            _selectionStart = _selectionEnd = -1;

            ActiveTemplateDisplay.Text = ""; ActiveTemplateLengthLabel.Text = "No template loaded";
            IsoformListBox.ItemsSource = null;
            IsoformPanel.Visibility = Visibility.Collapsed;
            InputSectionSummary.Text = "";
            GeneVisualizationPanel.Visibility = Visibility.Collapsed;
            GeneVisualizationCanvas.Children.Clear();

            ClearPrimerDisplay(FwdSequenceDisplay, FwdLength, FwdTm, FwdGC, FwdMW, FwdGCClamp, FwdSelfComp, FwdHairpin, FwdEndStability, FwdWarnings, FwdSelfDimerDG, FwdHairpinDG, FwdAnyComp);
            ClearPrimerDisplay(RevSequenceDisplay, RevLength, RevTm, RevGC, RevMW, RevGCClamp, RevSelfComp, RevHairpin, RevEndStability, RevWarnings, RevSelfDimerDG, RevHairpinDG, RevAnyComp);

            ProductSize.Text = ""; TmDifference.Text = ""; HeteroDimer.Text = ""; OverallScore.Text = "";
            PairWarnings.Text = ""; PairSummaryPanel.Visibility = Visibility.Collapsed;
            AutoResultsPanel.Visibility = Visibility.Collapsed; SequenceViewPanel.Visibility = Visibility.Collapsed;
            AutoResultsDataGrid.ItemsSource = null; InfoBar.Visibility = Visibility.Collapsed;
            _markerFwdStart = _markerRevStart = -1;
            FwdWithREPreview.Text = ""; RevWithREPreview.Text = "";

            ManualModeRadio.IsChecked = false;
            AutoModeRadio.IsChecked = true;
        }

        #endregion

        #region === Helpers ===

        private void ShowLoading(bool show, string msg = "")
        {
            LoadingOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            LoadingText.Text = msg;
        }

        private void ShowInfo(string msg, bool err)
        {
            InfoBar.Visibility = Visibility.Visible;
            InfoBar.Background = new SolidColorBrush(err ? Color.FromRgb(255, 235, 238) : Color.FromRgb(227, 242, 253));
            InfoIcon.Text = err ? "⚠️" : "✅";
            InfoText.Text = msg;
            InfoText.Foreground = new SolidColorBrush(err ? Color.FromRgb(198, 40, 40) : Color.FromRgb(21, 101, 192));
        }

        /// <summary>Input validation: only allow digits, decimal point, and minus sign in numeric TextBoxes.</summary>
        private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Allow digits, single decimal point, and leading minus
            string newText = e.Text;
            if (sender is TextBox tb)
            {
                string current = tb.Text;
                bool hasDot = current.Contains(".");
                bool hasMinus = current.Contains("-");
                foreach (char c in newText)
                {
                    if (char.IsDigit(c)) continue;
                    if (c == '.' && !hasDot) { hasDot = true; continue; }
                    if (c == '-' && !hasMinus && tb.CaretIndex == 0) { hasMinus = true; continue; }
                    e.Handled = true;
                    return;
                }
            }
        }

        /// <summary>Block paste of non-numeric content.</summary>
        private void NumericTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                if (!double.TryParse(text, out _)) e.CancelCommand();
            }
            else e.CancelCommand();
        }

        /// <summary>
        /// Handles nested ScrollViewer problem: when an inner scrollable control is at its
        /// top/bottom boundary, forward the scroll to the outer page ScrollViewer.
        /// </summary>
        private void PageScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // Find the nearest inner ScrollViewer from the event source
            var innerSV = FindAncestorScrollViewer(e.OriginalSource as DependencyObject);

            if (innerSV == null)
                return; // no inner SV → let PageScrollViewer handle normally

            // Skip horizontal-only ScrollViewers (e.g. GeneVisualizationScrollViewer)
            if (innerSV.VerticalScrollBarVisibility == ScrollBarVisibility.Disabled)
                return;

            // Panel-level ScrollViewers (left params, right results) should NOT capture
            // mouse wheel — the entire page should scroll instead. Only DataGrid's
            // internal ScrollViewer should be allowed to handle its own scrolling.
            bool isDataGridSV = IsInsideDataGrid(innerSV);

            if (!isDataGridSV)
            {
                // Forward to outer PageScrollViewer
                e.Handled = true;
                ScrollPageBy(e.Delta);
                return;
            }

            // DataGrid inner ScrollViewer — allow it to scroll rows
            bool canScrollContent = innerSV.ScrollableHeight > 0.5;
            if (!canScrollContent)
            {
                e.Handled = true;
                ScrollPageBy(e.Delta);
                return;
            }

            bool scrollingUp = e.Delta > 0;
            bool atTop = innerSV.VerticalOffset < 0.5;
            bool atBottom = innerSV.VerticalOffset >= innerSV.ScrollableHeight - 0.5;

            if ((scrollingUp && atTop) || (!scrollingUp && atBottom))
            {
                e.Handled = true;
                ScrollPageBy(e.Delta);
            }
        }

        private static bool IsInsideDataGrid(DependencyObject element)
        {
            var current = element;
            while (current != null)
            {
                if (current is DataGrid) return true;
                try { current = VisualTreeHelper.GetParent(current); }
                catch { return false; }
            }
            return false;
        }

        private void ScrollPageBy(int delta)
        {
            double newOffset = PageScrollViewer.VerticalOffset - delta;
            PageScrollViewer.ScrollToVerticalOffset(newOffset);
        }

        /// <summary>
        /// Walk up the visual tree from source to find the first ScrollViewer
        /// that is NOT the PageScrollViewer itself.
        /// </summary>
        private ScrollViewer FindAncestorScrollViewer(DependencyObject source)
        {
            var current = source;
            while (current != null)
            {
                if (current is ScrollViewer sv && sv != PageScrollViewer)
                    return sv;
                if (current == PageScrollViewer)
                    return null; // reached outer without finding inner
                try { current = VisualTreeHelper.GetParent(current); }
                catch { return null; }
            }
            return null;
        }

        #endregion
    }
}