using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace BioSAK
{
    public partial class FlowCytometryAnalyzer : Page
    {
        // Data structures
        private List<FcsFile> fcsFiles = new List<FcsFile>();
        private FcsFile? selectedFile;
        private FcsFile? overlayFile;

        // Gate templates (polygon/range definitions - independent of files)
        private ObservableCollection<GateTemplate> gateTemplates = new ObservableCollection<GateTemplate>();

        // Per-file gate results (event indices for each gate in each file)
        private Dictionary<string, Dictionary<string, HashSet<int>>> fileGateResults = new Dictionary<string, Dictionary<string, HashSet<int>>>();

        // Statistics records
        private ObservableCollection<StatsRecord> statsRecords = new ObservableCollection<StatsRecord>();

        private List<Point> currentPolygonPoints = new List<Point>();
        private Point? quadrantPosition = null;

        // Histogram range gate in progress
        private double? histRangeStart = null;
        private double? histRangeEnd = null;

        // Histogram threshold for showing above/below percentages
        private double? histThreshold = null;

        // Cached histogram range for coordinate conversion
        private double histXMin = 0.1;
        private double histXMax = 262144;

        // View state
        private string currentView = "scatter";
        private string gatingMode = "view";
        private string histGatingMode = "view";
        private string plotType = "dot";
        private bool xLogScale = true;
        private bool yLogScale = true;
        private bool histLogScale = true;
        private bool autoApplyGates = false;

        // Colormap selections
        private string dotColormap = "Turbo";
        private string contourColormap = "YlOrRd";
        private string histColor = "Blue";

        // Axis range
        private double? customXMin = null;
        private double? customXMax = null;
        private double? customYMin = null;
        private double? customYMax = null;

        // Parent gate for filtering
        private int parentGateIndex = -1;
        private int histParentGateIndex = -1;

        // Plot dimensions - updated dynamically
        private double plotWidth = 500;
        private double plotHeight = 500;
        private readonly Thickness plotMargin = new Thickness(60, 30, 30, 50);

        // Double-click detection
        private DispatcherTimer clickTimer;
        private Point pendingClickPoint;
        private bool isWaitingForDoubleClick = false;

        // Multi-analysis support
        private int analysisTabCount = 1;
        private Dictionary<TabItem, Canvas> tabCanvases = new Dictionary<TabItem, Canvas>();

        // Preserve histogram settings across file switches
        private string lastHistParamName = "";

        // Initialization flag
        private bool isInitialized = false;

        public FlowCytometryAnalyzer()
        {
            InitializeComponent();
            lstGates.ItemsSource = gateTemplates;
            dgStats.ItemsSource = statsRecords;

            clickTimer = new DispatcherTimer();
            clickTimer.Interval = TimeSpan.FromMilliseconds(250);
            clickTimer.Tick += ClickTimer_Tick;

            // Register first tab's canvas
            tabCanvases[tabItem1] = plotCanvas;
            tabAnalysis.SelectionChanged += TabAnalysis_SelectionChanged;

            isInitialized = true;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePlotSize();
            plotCanvas.SizeChanged += (s, args) => { UpdatePlotSize(); DrawPlot(); };
            this.Focus();
        }

        private void UpdatePlotSize()
        {
            var canvas = GetCurrentCanvas();
            if (canvas == null) return;
            plotWidth = Math.Max(400, canvas.ActualWidth);
            plotHeight = Math.Max(300, canvas.ActualHeight);
        }

        private Canvas? GetCurrentCanvas()
        {
            if (tabAnalysis.SelectedItem is TabItem selectedTab && tabCanvases.ContainsKey(selectedTab))
            {
                return tabCanvases[selectedTab];
            }
            return plotCanvas;
        }

        private void TabAnalysis_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized) return;
            UpdatePlotSize();
            DrawPlot();
        }

        #region Gate Inheritance System

        private Dictionary<string, HashSet<int>> GetCurrentFileGateResults()
        {
            if (selectedFile == null) return new Dictionary<string, HashSet<int>>();

            if (!fileGateResults.ContainsKey(selectedFile.Filename))
            {
                fileGateResults[selectedFile.Filename] = new Dictionary<string, HashSet<int>>();
            }
            return fileGateResults[selectedFile.Filename];
        }

        private void ApplyGateToCurrentFile(GateTemplate gate)
        {
            if (selectedFile == null) return;

            var results = GetCurrentFileGateResults();
            var eventIndices = new HashSet<int>();

            // Get parent gate events if specified
            HashSet<int>? parentEvents = null;
            if (!string.IsNullOrEmpty(gate.ParentGateName) && results.ContainsKey(gate.ParentGateName))
            {
                parentEvents = results[gate.ParentGateName];
            }

            if (gate.GateType == GateType.Polygon)
            {
                // Polygon gate - need both X and Y parameters
                int xIndex = FindParameterIndex(selectedFile, gate.XParamName);
                int yIndex = FindParameterIndex(selectedFile, gate.YParamName);

                if (xIndex < 0 || yIndex < 0) return;

                for (int i = 0; i < selectedFile.Events.Count; i++)
                {
                    if (parentEvents != null && !parentEvents.Contains(i))
                        continue;

                    var ev = selectedFile.Events[i];
                    if (ev[xIndex] > 0 && ev[yIndex] > 0)
                    {
                        var point = new Point(ev[xIndex], ev[yIndex]);
                        if (PointInPolygon(point, gate.Points))
                        {
                            eventIndices.Add(i);
                        }
                    }
                }
            }
            else if (gate.GateType == GateType.Range)
            {
                // Range gate - single parameter
                int paramIndex = FindParameterIndex(selectedFile, gate.XParamName);
                if (paramIndex < 0) return;

                for (int i = 0; i < selectedFile.Events.Count; i++)
                {
                    if (parentEvents != null && !parentEvents.Contains(i))
                        continue;

                    var ev = selectedFile.Events[i];
                    double value = ev[paramIndex];
                    if (value >= gate.RangeMin && value <= gate.RangeMax)
                    {
                        eventIndices.Add(i);
                    }
                }
            }

            results[gate.Name] = eventIndices;
        }

        private void ApplyAllGatesToCurrentFile()
        {
            if (selectedFile == null) return;

            fileGateResults[selectedFile.Filename] = new Dictionary<string, HashSet<int>>();

            foreach (var gate in gateTemplates)
            {
                ApplyGateToCurrentFile(gate);
            }

            UpdateGateDisplayNames();
            // Note: DrawPlot() is NOT called here to prevent recursion
            // Callers should call DrawPlot() after this method if needed
        }

        private void ApplyAllGatesToFile(FcsFile file)
        {
            if (file == null) return;

            fileGateResults[file.Filename] = new Dictionary<string, HashSet<int>>();

            foreach (var gate in gateTemplates)
            {
                ApplyGateToFile(file, gate);
            }
        }

        private void ApplyGateToFile(FcsFile file, GateTemplate gate)
        {
            if (file == null) return;

            if (!fileGateResults.ContainsKey(file.Filename))
            {
                fileGateResults[file.Filename] = new Dictionary<string, HashSet<int>>();
            }

            var results = fileGateResults[file.Filename];
            var eventIndices = new HashSet<int>();

            // Get parent gate events if specified
            HashSet<int>? parentEvents = null;
            if (!string.IsNullOrEmpty(gate.ParentGateName) && results.ContainsKey(gate.ParentGateName))
            {
                parentEvents = results[gate.ParentGateName];
            }

            if (gate.GateType == GateType.Polygon)
            {
                int xIndex = FindParameterIndex(file, gate.XParamName);
                int yIndex = FindParameterIndex(file, gate.YParamName);

                if (xIndex < 0 || yIndex < 0) return;

                for (int i = 0; i < file.Events.Count; i++)
                {
                    if (parentEvents != null && !parentEvents.Contains(i))
                        continue;

                    var ev = file.Events[i];
                    if (ev[xIndex] > 0 && ev[yIndex] > 0)
                    {
                        var point = new Point(ev[xIndex], ev[yIndex]);
                        if (PointInPolygon(point, gate.Points))
                        {
                            eventIndices.Add(i);
                        }
                    }
                }
            }
            else if (gate.GateType == GateType.Range)
            {
                int paramIndex = FindParameterIndex(file, gate.XParamName);
                if (paramIndex < 0) return;

                for (int i = 0; i < file.Events.Count; i++)
                {
                    if (parentEvents != null && !parentEvents.Contains(i))
                        continue;

                    var ev = file.Events[i];
                    double value = ev[paramIndex];
                    if (value >= gate.RangeMin && value <= gate.RangeMax)
                    {
                        eventIndices.Add(i);
                    }
                }
            }

            results[gate.Name] = eventIndices;
        }

        private int FindParameterIndex(FcsFile file, string paramName)
        {
            for (int i = 0; i < file.Parameters.Count; i++)
            {
                if (file.Parameters[i].Name.Equals(paramName, StringComparison.OrdinalIgnoreCase) ||
                    file.Parameters[i].Label.Equals(paramName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private List<int> GetDisplayEventIndices()
        {
            if (selectedFile == null) return new List<int>();
            return GetDisplayEventIndicesForFile(selectedFile);
        }

        private List<int> GetDisplayEventIndicesForFile(FcsFile file)
        {
            if (file == null) return new List<int>();

            int gateIndex = currentView == "histogram" ? histParentGateIndex : parentGateIndex;

            if (gateIndex < 0 || gateIndex >= gateTemplates.Count)
            {
                // No parent gate selected - return all events
                return Enumerable.Range(0, file.Events.Count).ToList();
            }
            else
            {
                var gateName = gateTemplates[gateIndex].Name;

                // Ensure gates are applied to this file
                if (!fileGateResults.ContainsKey(file.Filename))
                {
                    ApplyAllGatesToFile(file);
                }

                var results = fileGateResults.ContainsKey(file.Filename)
                    ? fileGateResults[file.Filename]
                    : new Dictionary<string, HashSet<int>>();

                // If gate not yet applied to this file, apply it now
                if (!results.ContainsKey(gateName))
                {
                    ApplyAllGatesToFile(file);
                    results = fileGateResults.ContainsKey(file.Filename)
                        ? fileGateResults[file.Filename]
                        : new Dictionary<string, HashSet<int>>();
                }

                if (results.ContainsKey(gateName))
                {
                    // Return the gated events (could be empty if no events match)
                    return results[gateName].ToList();
                }

                // Still no results - return empty (gate doesn't match any events)
                return new List<int>();
            }
        }

        private int GetGateEventCount(string gateName)
        {
            var results = GetCurrentFileGateResults();
            return results.ContainsKey(gateName) ? results[gateName].Count : 0;
        }

        private void UpdateGateDisplayNames()
        {
            if (selectedFile == null) return;

            var results = GetCurrentFileGateResults();
            int totalEvents = selectedFile.Events.Count;

            foreach (var gate in gateTemplates)
            {
                int count = results.ContainsKey(gate.Name) ? results[gate.Name].Count : 0;
                double percent = totalEvents > 0 ? (count * 100.0 / totalEvents) : 0;

                if (gate.GateType == GateType.Range)
                {
                    gate.DisplayName = $"{gate.Name}: {count:N0} ({percent:F2}%) [Range: {gate.XParamName}]";
                }
                else
                {
                    gate.DisplayName = $"{gate.Name}: {count:N0} ({percent:F2}%) [{gate.XParamName}/{gate.YParamName}]";
                }
            }

            lstGates.Items.Refresh();
        }

        private void CreatePolygonGateAndApply(string gateName, List<Point> polygonPoints, string parentGateName = "")
        {
            if (selectedFile == null || cboXParam.SelectedIndex < 0 || cboYParam.SelectedIndex < 0) return;

            int xIndex = cboXParam.SelectedIndex;
            int yIndex = cboYParam.SelectedIndex;

            var gate = new GateTemplate
            {
                Name = gateName,
                GateType = GateType.Polygon,
                Points = new List<Point>(polygonPoints),
                XParamIndex = xIndex,
                YParamIndex = yIndex,
                XParamName = selectedFile.Parameters[xIndex].Label,
                YParamName = selectedFile.Parameters[yIndex].Label,
                ParentGateName = parentGateName
            };

            gateTemplates.Add(gate);
            ApplyGateToCurrentFile(gate);
            UpdateGateDisplayNames();
            UpdateParentGateComboBoxes();

            int count = GetGateEventCount(gateName);
            double percent = selectedFile.Events.Count > 0 ? (count * 100.0 / selectedFile.Events.Count) : 0;
            txtStatus.Text = $"Gate '{gateName}' created: {count:N0} events ({percent:F2}%)";
        }

        private void CreateRangeGateAndApply(string gateName, double minValue, double maxValue, string parentGateName = "")
        {
            if (selectedFile == null || cboHistParam.SelectedIndex < 0) return;

            int paramIndex = cboHistParam.SelectedIndex;

            var gate = new GateTemplate
            {
                Name = gateName,
                GateType = GateType.Range,
                XParamIndex = paramIndex,
                XParamName = selectedFile.Parameters[paramIndex].Label,
                YParamName = "",
                RangeMin = minValue,
                RangeMax = maxValue,
                ParentGateName = parentGateName
            };

            gateTemplates.Add(gate);
            ApplyGateToCurrentFile(gate);
            UpdateGateDisplayNames();
            UpdateParentGateComboBoxes();

            int count = GetGateEventCount(gateName);
            double percent = selectedFile.Events.Count > 0 ? (count * 100.0 / selectedFile.Events.Count) : 0;
            txtStatus.Text = $"Range gate '{gateName}' created: {count:N0} events ({percent:F2}%)";
        }

        private void UpdateParentGateComboBoxes()
        {
            // Update scatter plot parent gate combo
            int previousSelection = cboParentGate.SelectedIndex;
            cboParentGate.Items.Clear();
            cboParentGate.Items.Add(new ComboBoxItem { Content = "All Events" });

            foreach (var gate in gateTemplates)
            {
                cboParentGate.Items.Add(new ComboBoxItem { Content = gate.Name });
            }

            if (previousSelection >= 0 && previousSelection < cboParentGate.Items.Count)
                cboParentGate.SelectedIndex = previousSelection;
            else
                cboParentGate.SelectedIndex = 0;

            // Update histogram parent gate combo
            int histPrevSelection = cboHistParentGate.SelectedIndex;
            cboHistParentGate.Items.Clear();
            cboHistParentGate.Items.Add(new ComboBoxItem { Content = "All Events" });

            foreach (var gate in gateTemplates)
            {
                cboHistParentGate.Items.Add(new ComboBoxItem { Content = gate.Name });
            }

            if (histPrevSelection >= 0 && histPrevSelection < cboHistParentGate.Items.Count)
                cboHistParentGate.SelectedIndex = histPrevSelection;
            else
                cboHistParentGate.SelectedIndex = 0;
        }

        private void BtnApplyGates_Click(object sender, RoutedEventArgs e)
        {
            if (selectedFile == null)
            {
                MessageBox.Show("Please load a file first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (gateTemplates.Count == 0)
            {
                MessageBox.Show("No gates defined. Create gates first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ApplyAllGatesToCurrentFile();
            DrawPlot();
            txtStatus.Text = $"Applied {gateTemplates.Count} gate(s) to {selectedFile.Filename}";
        }

        private void ChkAutoApplyGates_Changed(object sender, RoutedEventArgs e)
        {
            autoApplyGates = chkAutoApplyGates.IsChecked == true;
        }

        #endregion

        #region Multi-Analysis Tab Support

        private void BtnAddAnalysis_Click(object sender, RoutedEventArgs e)
        {
            analysisTabCount++;
            var newTab = new TabItem { Header = $"Panel {analysisTabCount}" };

            var grid = new Grid { Background = Brushes.White };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var canvas = new Canvas { Background = Brushes.White, ClipToBounds = true };
            canvas.MouseLeftButtonDown += PlotCanvas_MouseLeftButtonDown;
            canvas.MouseLeftButtonUp += PlotCanvas_MouseLeftButtonUp;
            canvas.MouseMove += PlotCanvas_MouseMove;
            canvas.SizeChanged += (s, args) => {
                if (tabAnalysis.SelectedItem == newTab)
                {
                    UpdatePlotSize();
                    DrawPlot();
                }
            };
            Grid.SetRow(canvas, 0);
            grid.Children.Add(canvas);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
            Grid.SetRow(buttonPanel, 1);

            var btnExport = new Button { Content = "📷 Export Plot", Padding = new Thickness(10, 5, 10, 5), Background = new SolidColorBrush(Color.FromRgb(223, 240, 216)) };
            btnExport.Click += (s, args) => ExportCurrentTab();
            buttonPanel.Children.Add(btnExport);

            grid.Children.Add(buttonPanel);
            newTab.Content = grid;

            // Register this tab's canvas
            tabCanvases[newTab] = canvas;

            tabAnalysis.Items.Add(newTab);
            tabAnalysis.SelectedItem = newTab;

            txtStatus.Text = $"Added Panel {analysisTabCount}";
        }

        private void BtnRemoveAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (tabAnalysis.Items.Count > 1)
            {
                var currentTab = tabAnalysis.SelectedItem as TabItem;
                if (currentTab != null)
                {
                    // Remove from dictionary
                    tabCanvases.Remove(currentTab);

                    tabAnalysis.Items.Remove(currentTab);
                    if (tabAnalysis.SelectedIndex < 0)
                        tabAnalysis.SelectedIndex = tabAnalysis.Items.Count - 1;
                    txtStatus.Text = "Panel removed";
                }
            }
            else
            {
                MessageBox.Show("At least one panel must remain.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ExportCurrentTab()
        {
            if (currentView == "scatter")
                ExportPlot(plotType);
            else
                ExportHistogram();
        }

        #endregion

        #region File Operations

        private void BtnLoadFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "FCS Files (*.fcs)|*.fcs|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var filename in dialog.FileNames)
                {
                    try
                    {
                        var fcs = ParseFcsFile(filename);
                        fcsFiles.Add(fcs);
                        cboFiles.Items.Add(fcs.Filename);
                        cboOverlay.Items.Add(fcs.Filename);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to load {filename}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                if (selectedFile == null && fcsFiles.Count > 0)
                {
                    cboFiles.SelectedIndex = 0;
                }

                txtStatus.Text = $"Loaded {fcsFiles.Count} file(s)";
            }
        }

        private void BtnLoadDemo_Click(object sender, RoutedEventArgs e)
        {
            fcsFiles.Clear();
            cboFiles.Items.Clear();
            cboOverlay.Items.Clear();

            var demo1 = GenerateDemoData("Demo_Sample.fcs");
            var demo2 = GenerateDemoData2("Demo_Treatment.fcs");

            fcsFiles.Add(demo1);
            fcsFiles.Add(demo2);

            cboFiles.Items.Add(demo1.Filename);
            cboFiles.Items.Add(demo2.Filename);
            cboOverlay.Items.Add(demo1.Filename);
            cboOverlay.Items.Add(demo2.Filename);

            cboFiles.SelectedIndex = 0;
            cboOverlay.SelectedIndex = 1;

            txtStatus.Text = "Demo data loaded (2 samples for testing gate inheritance)";
        }

        private FcsFile ParseFcsFile(string filepath)
        {
            using (var fs = new FileStream(filepath, FileMode.Open, FileAccess.Read))
            using (var reader = new BinaryReader(fs))
            {
                var header = Encoding.ASCII.GetString(reader.ReadBytes(58));
                var textStart = int.Parse(header.Substring(10, 8).Trim());
                var textEnd = int.Parse(header.Substring(18, 8).Trim());
                var dataStart = int.Parse(header.Substring(26, 8).Trim());
                var dataEnd = int.Parse(header.Substring(34, 8).Trim());

                fs.Seek(textStart, SeekOrigin.Begin);
                var textSegment = Encoding.UTF8.GetString(reader.ReadBytes(textEnd - textStart + 1));
                var delimiter = textSegment[0];
                var parts = textSegment.Substring(1).Split(delimiter);

                var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < parts.Length - 1; i += 2)
                {
                    if (!string.IsNullOrEmpty(parts[i]))
                        metadata[parts[i]] = parts[i + 1];
                }

                int numParams = int.Parse(metadata.GetValueOrDefault("$PAR") ?? "0");
                int numEvents = int.Parse(metadata.GetValueOrDefault("$TOT") ?? "0");
                var byteOrder = metadata.GetValueOrDefault("$BYTEORD") ?? "1,2,3,4";
                bool isLittleEndian = byteOrder.StartsWith("1");

                var parameters = new List<FcsParameter>();
                for (int i = 1; i <= numParams; i++)
                {
                    var name = metadata.GetValueOrDefault($"$P{i}N") ?? $"P{i}";
                    var label = metadata.GetValueOrDefault($"$P{i}S") ?? name;
                    parameters.Add(new FcsParameter { Name = name, Label = label });
                }

                fs.Seek(dataStart, SeekOrigin.Begin);
                var events = new List<float[]>();
                int bytesPerEvent = numParams * 4;
                int dataLength = dataEnd - dataStart + 1;
                int actualEvents = Math.Min(numEvents, dataLength / bytesPerEvent);

                for (int ev = 0; ev < actualEvents; ev++)
                {
                    var eventData = new float[numParams];
                    for (int p = 0; p < numParams; p++)
                    {
                        var bytes = reader.ReadBytes(4);
                        if (!isLittleEndian) Array.Reverse(bytes);
                        var value = BitConverter.ToSingle(bytes, 0);
                        eventData[p] = float.IsNaN(value) || float.IsInfinity(value) ? 0 : value;
                    }
                    events.Add(eventData);
                }

                return new FcsFile
                {
                    Filename = System.IO.Path.GetFileName(filepath),
                    Parameters = parameters,
                    Events = events
                };
            }
        }

        private FcsFile GenerateDemoData(string filename)
        {
            var parameters = new List<FcsParameter>
            {
                new FcsParameter { Name = "FSC-A", Label = "FSC-A" },
                new FcsParameter { Name = "SSC-A", Label = "SSC-A" },
                new FcsParameter { Name = "FITC-A", Label = "CD4 FITC" },
                new FcsParameter { Name = "PE-A", Label = "CD8 PE" },
                new FcsParameter { Name = "APC-A", Label = "CD3 APC" },
                new FcsParameter { Name = "Pacific Blue-A", Label = "CD45 Pacific Blue" }
            };

            var events = new List<float[]>();
            var rand = new Random(42);

            for (int i = 0; i < 6000; i++)
            {
                events.Add(new float[]
                {
                    (float)(50000 + rand.NextDouble() * 30000 + (rand.NextDouble() - 0.5) * 20000),
                    (float)(30000 + rand.NextDouble() * 25000 + (rand.NextDouble() - 0.5) * 15000),
                    (float)(rand.NextDouble() < 0.4 ? 5000 + rand.NextDouble() * 50000 : 100 + rand.NextDouble() * 2000),
                    (float)(rand.NextDouble() < 0.3 ? 8000 + rand.NextDouble() * 60000 : 100 + rand.NextDouble() * 2000),
                    (float)(20000 + rand.NextDouble() * 100000),
                    (float)(50000 + rand.NextDouble() * 80000)
                });
            }

            for (int i = 0; i < 2000; i++)
            {
                events.Add(new float[]
                {
                    (float)(80000 + rand.NextDouble() * 40000),
                    (float)(60000 + rand.NextDouble() * 40000),
                    (float)(200 + rand.NextDouble() * 3000),
                    (float)(150 + rand.NextDouble() * 2000),
                    (float)(1000 + rand.NextDouble() * 5000),
                    (float)(80000 + rand.NextDouble() * 60000)
                });
            }

            for (int i = 0; i < 2000; i++)
            {
                events.Add(new float[]
                {
                    (float)(100000 + rand.NextDouble() * 60000),
                    (float)(120000 + rand.NextDouble() * 80000),
                    (float)(100 + rand.NextDouble() * 1000),
                    (float)(100 + rand.NextDouble() * 1000),
                    (float)(500 + rand.NextDouble() * 3000),
                    (float)(30000 + rand.NextDouble() * 40000)
                });
            }

            return new FcsFile { Filename = filename, Parameters = parameters, Events = events };
        }

        private FcsFile GenerateDemoData2(string filename)
        {
            var parameters = new List<FcsParameter>
            {
                new FcsParameter { Name = "FSC-A", Label = "FSC-A" },
                new FcsParameter { Name = "SSC-A", Label = "SSC-A" },
                new FcsParameter { Name = "FITC-A", Label = "CD4 FITC" },
                new FcsParameter { Name = "PE-A", Label = "CD8 PE" },
                new FcsParameter { Name = "APC-A", Label = "CD3 APC" },
                new FcsParameter { Name = "Pacific Blue-A", Label = "CD45 Pacific Blue" }
            };

            var events = new List<float[]>();
            var rand = new Random(123);

            for (int i = 0; i < 5600; i++)
            {
                events.Add(new float[]
                {
                    (float)(55000 + rand.NextDouble() * 25000),
                    (float)(35000 + rand.NextDouble() * 20000),
                    (float)(rand.NextDouble() < 0.6 ? 15000 + rand.NextDouble() * 80000 : 100 + rand.NextDouble() * 1500),
                    (float)(rand.NextDouble() < 0.2 ? 5000 + rand.NextDouble() * 40000 : 100 + rand.NextDouble() * 1500),
                    (float)(30000 + rand.NextDouble() * 120000),
                    (float)(60000 + rand.NextDouble() * 70000)
                });
            }

            for (int i = 0; i < 2400; i++)
            {
                events.Add(new float[]
                {
                    (float)(90000 + rand.NextDouble() * 50000),
                    (float)(80000 + rand.NextDouble() * 60000),
                    (float)(150 + rand.NextDouble() * 2000),
                    (float)(100 + rand.NextDouble() * 1500),
                    (float)(800 + rand.NextDouble() * 4000),
                    (float)(40000 + rand.NextDouble() * 50000)
                });
            }

            return new FcsFile { Filename = filename, Parameters = parameters, Events = events };
        }

        #endregion

        #region Event Handlers

        private void CboFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboFiles.SelectedIndex >= 0 && cboFiles.SelectedIndex < fcsFiles.Count)
            {
                // Save current histogram parameter name before switching
                if (selectedFile != null && cboHistParam.SelectedIndex >= 0)
                {
                    lastHistParamName = selectedFile.Parameters[cboHistParam.SelectedIndex].Label;
                }

                // Note: histThreshold is preserved across file switches (not cleared here)

                selectedFile = fcsFiles[cboFiles.SelectedIndex];

                // Apply gates FIRST before updating comboboxes
                // This ensures gate results exist before any DrawPlot is triggered
                if (gateTemplates.Count > 0)
                {
                    ApplyAllGatesToCurrentFile();
                }

                UpdateParameterComboBoxes();

                // Restore histogram parameter selection by name
                RestoreHistogramParameter();

                txtFileInfo.Text = $"Events: {selectedFile.Events.Count:N0} | Parameters: {selectedFile.Parameters.Count}";

                if (gateTemplates.Count > 0)
                {
                    if (autoApplyGates)
                        txtStatus.Text = $"Switched to {selectedFile.Filename} - Gates auto-applied";
                    else
                        txtStatus.Text = $"Switched to {selectedFile.Filename} - Gates applied";
                }
                else
                {
                    txtStatus.Text = $"Switched to {selectedFile.Filename}";
                }

                DrawPlot();
            }
        }

        private void RestoreHistogramParameter()
        {
            if (string.IsNullOrEmpty(lastHistParamName) || selectedFile == null) return;

            // Find parameter by name
            for (int i = 0; i < selectedFile.Parameters.Count; i++)
            {
                if (selectedFile.Parameters[i].Label.Equals(lastHistParamName, StringComparison.OrdinalIgnoreCase) ||
                    selectedFile.Parameters[i].Name.Equals(lastHistParamName, StringComparison.OrdinalIgnoreCase))
                {
                    cboHistParam.SelectedIndex = i;
                    return;
                }
            }
        }

        private void CboOverlay_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cboOverlay.SelectedIndex >= 0 && cboOverlay.SelectedIndex < fcsFiles.Count)
            {
                overlayFile = fcsFiles[cboOverlay.SelectedIndex];
                DrawPlot();
            }
        }

        private void ChkOverlay_Changed(object sender, RoutedEventArgs e)
        {
            if (!isInitialized) return;
            cboOverlay.Visibility = chkOverlay.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            DrawPlot();
        }

        private void UpdateParameterComboBoxes()
        {
            if (selectedFile == null) return;

            cboXParam.Items.Clear();
            cboYParam.Items.Clear();
            cboHistParam.Items.Clear();

            foreach (var param in selectedFile.Parameters)
            {
                var label = string.IsNullOrEmpty(param.Label) ? param.Name : param.Label;
                cboXParam.Items.Add(label);
                cboYParam.Items.Add(label);
                cboHistParam.Items.Add(label);
            }

            if (selectedFile.Parameters.Count > 0)
            {
                cboXParam.SelectedIndex = 0;
                cboYParam.SelectedIndex = Math.Min(1, selectedFile.Parameters.Count - 1);
                cboHistParam.SelectedIndex = 0;
            }
        }

        private void Param_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized) return;
            DrawPlot();
        }

        private void Scale_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized || cboXScale == null || cboYScale == null) return;
            xLogScale = cboXScale.SelectedIndex == 1;
            yLogScale = cboYScale.SelectedIndex == 1;
            DrawPlot();
        }

        private void HistParam_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized) return;
            histRangeStart = null;
            histRangeEnd = null;
            // Note: histThreshold is preserved when changing parameters
            DrawPlot();
        }

        private void HistScale_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized || cboHistScale == null) return;
            histLogScale = cboHistScale.SelectedIndex == 1;
            histRangeStart = null;
            histRangeEnd = null;
            // Note: histThreshold is preserved when changing scale
            DrawPlot();
        }

        private void Colormap_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized) return;

            if (cboDotColormap?.SelectedItem is ComboBoxItem dotItem)
                dotColormap = dotItem.Content?.ToString() ?? "Turbo";
            if (cboContourColormap?.SelectedItem is ComboBoxItem contourItem)
                contourColormap = contourItem.Content?.ToString() ?? "YlOrRd";
            if (cboHistColor?.SelectedItem is ComboBoxItem histItem)
                histColor = histItem.Content?.ToString() ?? "Blue";

            DrawPlot();
        }

        private void ViewType_Changed(object sender, RoutedEventArgs e)
        {
            if (!isInitialized || pnlScatterControls == null || pnlHistogramControls == null) return;

            currentView = rbScatter.IsChecked == true ? "scatter" : "histogram";
            pnlScatterControls.Visibility = currentView == "scatter" ? Visibility.Visible : Visibility.Collapsed;
            pnlHistogramControls.Visibility = currentView == "histogram" ? Visibility.Visible : Visibility.Collapsed;
            btnExportDot.Visibility = currentView == "scatter" ? Visibility.Visible : Visibility.Collapsed;
            btnExportContour.Visibility = currentView == "scatter" ? Visibility.Visible : Visibility.Collapsed;
            btnExportHistogram.Visibility = currentView == "histogram" ? Visibility.Visible : Visibility.Collapsed;
            DrawPlot();
        }

        private void PlotType_Changed(object sender, RoutedEventArgs e)
        {
            if (!isInitialized || rbDotPlot == null || rbContourPlot == null) return;
            plotType = rbDotPlot.IsChecked == true ? "dot" : "contour";
            DrawPlot();
        }

        private void GatingMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!isInitialized || rbModeView == null || rbModePolygon == null || rbModeQuadrant == null) return;

            if (rbModeView.IsChecked == true)
            {
                gatingMode = "view";
                pnlPolygonTip.Visibility = Visibility.Collapsed;
            }
            else if (rbModePolygon.IsChecked == true)
            {
                gatingMode = "polygon";
                pnlPolygonTip.Visibility = Visibility.Visible;
            }
            else if (rbModeQuadrant.IsChecked == true)
            {
                gatingMode = "quadrant";
                pnlPolygonTip.Visibility = Visibility.Collapsed;
            }

            var canvas = GetCurrentCanvas();
            if (canvas != null) canvas.Cursor = gatingMode == "view" ? Cursors.Arrow : Cursors.Cross;
        }

        private void HistGatingMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!isInitialized) return;

            var canvas = GetCurrentCanvas();

            if (rbHistModeView.IsChecked == true)
            {
                histGatingMode = "view";
                pnlHistGateTip.Visibility = Visibility.Collapsed;
                if (canvas != null) canvas.Cursor = Cursors.Arrow;
                // Note: histThreshold is preserved but not displayed in View mode
                DrawPlot();
            }
            else if (rbHistModeThreshold.IsChecked == true)
            {
                histGatingMode = "threshold";
                pnlHistGateTip.Visibility = Visibility.Visible;
                txtHistGateTip.Text = "💡 Click to show above/below cell percentages";
                if (canvas != null) canvas.Cursor = Cursors.Cross;
                // histThreshold is preserved and displayed
                DrawPlot();
            }
            else if (rbHistModeRange.IsChecked == true)
            {
                histGatingMode = "range";
                pnlHistGateTip.Visibility = Visibility.Visible;
                txtHistGateTip.Text = "💡 Click to set left boundary, click again to set right boundary and create gate";
                if (canvas != null) canvas.Cursor = Cursors.Cross;
                histRangeStart = null;
                histRangeEnd = null;
                // Note: histThreshold is preserved but not displayed in Range mode
                DrawPlot();
            }
        }

        private void ParentGate_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized) return;
            parentGateIndex = cboParentGate.SelectedIndex - 1;

            if (parentGateIndex >= 0 && parentGateIndex < gateTemplates.Count)
            {
                var gate = gateTemplates[parentGateIndex];
                int count = GetGateEventCount(gate.Name);
                pnlGateInfo.Visibility = Visibility.Visible;

                if (gate.GateType == GateType.Range)
                {
                    txtGateInfo.Text = $"Showing {count:N0} events from {gate.Name}\n(Range gate on {gate.XParamName})";
                }
                else
                {
                    txtGateInfo.Text = $"Showing {count:N0} events from {gate.Name}\n(Created on {gate.XParamName} vs {gate.YParamName})";
                }
            }
            else
            {
                pnlGateInfo.Visibility = Visibility.Collapsed;
            }

            DrawPlot();
        }

        private void HistParentGate_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized) return;
            histParentGateIndex = cboHistParentGate.SelectedIndex - 1;

            if (histParentGateIndex >= 0 && histParentGateIndex < gateTemplates.Count)
            {
                var gate = gateTemplates[histParentGateIndex];
                int count = GetGateEventCount(gate.Name);
                pnlHistGateInfo.Visibility = Visibility.Visible;

                if (gate.GateType == GateType.Range)
                {
                    txtHistGateInfo.Text = $"Showing {count:N0} events from {gate.Name}\n(Range gate on {gate.XParamName})";
                }
                else
                {
                    txtHistGateInfo.Text = $"Showing {count:N0} events from {gate.Name}\n(Created on {gate.XParamName} vs {gate.YParamName})";
                }
            }
            else
            {
                pnlHistGateInfo.Visibility = Visibility.Collapsed;
            }

            DrawPlot();
        }

        private void AxisRange_Changed(object sender, TextChangedEventArgs e)
        {
            if (!isInitialized) return;
            if (sender is TextBox tb)
            {
                double? value = null;
                if (double.TryParse(tb.Text, out double parsed))
                    value = parsed;

                switch (tb.Tag?.ToString())
                {
                    case "xmin": customXMin = value; break;
                    case "xmax": customXMax = value; break;
                    case "ymin": customYMin = value; break;
                    case "ymax": customYMax = value; break;
                }
                DrawPlot();
            }
        }

        private void BtnResetRange_Click(object sender, RoutedEventArgs e)
        {
            txtXMin.Text = "";
            txtXMax.Text = "";
            txtYMin.Text = "";
            txtYMax.Text = "";
            customXMin = customXMax = customYMin = customYMax = null;
            DrawPlot();
        }

        private void BtnRemoveGate_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is GateTemplate gate)
            {
                foreach (var fileResults in fileGateResults.Values)
                {
                    fileResults.Remove(gate.Name);
                }

                gateTemplates.Remove(gate);
                UpdateParentGateComboBoxes();

                if (parentGateIndex >= gateTemplates.Count)
                {
                    parentGateIndex = -1;
                    cboParentGate.SelectedIndex = 0;
                }
                if (histParentGateIndex >= gateTemplates.Count)
                {
                    histParentGateIndex = -1;
                    cboHistParentGate.SelectedIndex = 0;
                }

                DrawPlot();
                txtStatus.Text = $"Gate '{gate.Name}' removed";
            }
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            gateTemplates.Clear();
            fileGateResults.Clear();
            currentPolygonPoints.Clear();
            quadrantPosition = null;
            histRangeStart = null;
            histRangeEnd = null;
            histThreshold = null;
            parentGateIndex = -1;
            histParentGateIndex = -1;
            UpdateParentGateComboBoxes();
            DrawPlot();
            txtStatus.Text = "All gates cleared";
        }

        #endregion

        #region Mouse Handling

        private void ClickTimer_Tick(object? sender, EventArgs e)
        {
            clickTimer.Stop();
            isWaitingForDoubleClick = false;

            if (gatingMode == "polygon")
            {
                currentPolygonPoints.Add(pendingClickPoint);
                DrawPlot();
            }
        }

        private void PlotCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (selectedFile == null) return;

            var canvas = sender as Canvas ?? GetCurrentCanvas();
            if (canvas == null) return;

            var pos = e.GetPosition(canvas);

            // Update plot size from actual canvas
            double actualWidth = canvas.ActualWidth;
            double actualHeight = canvas.ActualHeight;
            if (actualWidth > 0) plotWidth = actualWidth;
            if (actualHeight > 0) plotHeight = actualHeight;

            if (currentView == "histogram")
            {
                HandleHistogramClick(pos);
                return;
            }

            // Scatter plot handling
            if (pos.X < plotMargin.Left || pos.X > plotWidth - plotMargin.Right ||
                pos.Y < plotMargin.Top || pos.Y > plotHeight - plotMargin.Bottom)
                return;

            var dataPoint = ScreenToData(pos);

            if (gatingMode == "quadrant")
            {
                quadrantPosition = dataPoint;
                DrawPlot();
            }
            else if (gatingMode == "polygon")
            {
                if (isWaitingForDoubleClick)
                {
                    clickTimer.Stop();
                    isWaitingForDoubleClick = false;

                    if (currentPolygonPoints.Count >= 3)
                    {
                        string gateName = $"P{gateTemplates.Count + 1}";
                        string parentName = parentGateIndex >= 0 && parentGateIndex < gateTemplates.Count
                            ? gateTemplates[parentGateIndex].Name : "";
                        CreatePolygonGateAndApply(gateName, currentPolygonPoints, parentName);
                        currentPolygonPoints.Clear();
                        DrawPlot();
                    }
                    else
                    {
                        txtStatus.Text = "Need at least 3 points to create a polygon gate";
                    }
                }
                else
                {
                    isWaitingForDoubleClick = true;
                    pendingClickPoint = dataPoint;
                    clickTimer.Start();
                }
            }
        }

        private void HandleHistogramClick(Point pos)
        {
            if (pos.X < plotMargin.Left || pos.X > plotWidth - plotMargin.Right)
                return;

            double dataValue = HistScreenToDataX(pos.X);

            if (histGatingMode == "range")
            {
                if (!histRangeStart.HasValue)
                {
                    // First click - set start
                    histRangeStart = dataValue;
                    txtStatus.Text = $"Range start set at {dataValue:F1}. Click again to set end.";
                    DrawPlot();
                }
                else
                {
                    // Second click - set end and create gate
                    histRangeEnd = dataValue;

                    double minVal = Math.Min(histRangeStart.Value, histRangeEnd.Value);
                    double maxVal = Math.Max(histRangeStart.Value, histRangeEnd.Value);

                    string gateName = $"R{gateTemplates.Count + 1}";
                    string parentName = histParentGateIndex >= 0 && histParentGateIndex < gateTemplates.Count
                        ? gateTemplates[histParentGateIndex].Name : "";

                    CreateRangeGateAndApply(gateName, minVal, maxVal, parentName);

                    histRangeStart = null;
                    histRangeEnd = null;
                    DrawPlot();
                }
            }
            else if (histGatingMode == "threshold")
            {
                // Threshold mode - show above/below percentages
                histThreshold = dataValue;
                DrawPlot();
            }
        }

        private void PlotCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Not used
        }

        private void PlotCanvas_MouseMove(object sender, MouseEventArgs e) { }

        private Point ScreenToData(Point screen)
        {
            return new Point(ScreenToDataX(screen.X), ScreenToDataY(screen.Y));
        }

        private double ScreenToDataX(double screenX)
        {
            if (selectedFile == null) return 0;
            int xIndex = cboXParam.SelectedIndex;
            if (xIndex < 0) return 0;

            var displayIndices = GetDisplayEventIndices();
            var xValues = displayIndices.Select(i => selectedFile.Events[i][xIndex]).Where(v => v > 0).ToList();
            double dataMax = xValues.Count > 0 ? xValues.Max() : 262144;

            double xMin = customXMin ?? (xLogScale ? 0.1 : 0);
            double xMax = customXMax ?? dataMax * 2;

            double plotLeft = plotMargin.Left;
            double plotRight = plotWidth - plotMargin.Right;
            double t = (screenX - plotLeft) / (plotRight - plotLeft);

            if (xLogScale)
            {
                double logMin = Math.Log10(Math.Max(0.1, xMin));
                double logMax = Math.Log10(xMax);
                return Math.Pow(10, logMin + t * (logMax - logMin));
            }
            return xMin + t * (xMax - xMin);
        }

        private double ScreenToDataY(double screenY)
        {
            if (selectedFile == null) return 0;
            int yIndex = cboYParam.SelectedIndex;
            if (yIndex < 0) return 0;

            var displayIndices = GetDisplayEventIndices();
            var yValues = displayIndices.Select(i => selectedFile.Events[i][yIndex]).Where(v => v > 0).ToList();
            double dataMax = yValues.Count > 0 ? yValues.Max() : 262144;

            double yMin = customYMin ?? (yLogScale ? 0.1 : 0);
            double yMax = customYMax ?? dataMax * 2;

            double plotTop = plotMargin.Top;
            double plotBottom = plotHeight - plotMargin.Bottom;
            double t = 1 - (screenY - plotTop) / (plotBottom - plotTop);

            if (yLogScale)
            {
                double logMin = Math.Log10(Math.Max(0.1, yMin));
                double logMax = Math.Log10(yMax);
                return Math.Pow(10, logMin + t * (logMax - logMin));
            }
            return yMin + t * (yMax - yMin);
        }

        private double HistScreenToDataX(double screenX)
        {
            double plotLeft = plotMargin.Left;
            double plotRight = plotWidth - plotMargin.Right;
            double t = (screenX - plotLeft) / (plotRight - plotLeft);
            t = Math.Max(0, Math.Min(1, t));

            if (histLogScale)
            {
                double logMin = Math.Log10(Math.Max(0.1, histXMin));
                double logMax = Math.Log10(histXMax);
                return Math.Pow(10, logMin + t * (logMax - logMin));
            }
            return histXMin + t * (histXMax - histXMin);
        }

        #endregion

        #region Drawing

        private void DrawPlot()
        {
            var canvas = GetCurrentCanvas();
            if (selectedFile == null || canvas == null) return;

            if (canvas.ActualWidth > 0) plotWidth = canvas.ActualWidth;
            if (canvas.ActualHeight > 0) plotHeight = canvas.ActualHeight;

            canvas.Children.Clear();

            if (currentView == "scatter")
                DrawScatterPlot(canvas, false, plotType);
            else
                DrawHistogram(canvas, false);
        }

        private void DrawScatterPlot(Canvas canvas, bool isExport, string exportPlotType)
        {
            if (selectedFile == null || cboXParam.SelectedIndex < 0 || cboYParam.SelectedIndex < 0) return;

            int xIndex = cboXParam.SelectedIndex;
            int yIndex = cboYParam.SelectedIndex;

            var displayIndices = GetDisplayEventIndices();

            // Show message if no events to display
            if (displayIndices.Count == 0)
            {
                canvas.Children.Add(new Rectangle { Width = plotWidth, Height = plotHeight, Fill = Brushes.White });
                var noDataMsg = new TextBlock
                {
                    Text = "No events to display\n(Gate may not contain events in this sample)",
                    Foreground = Brushes.Gray,
                    FontSize = 14,
                    TextAlignment = TextAlignment.Center
                };
                noDataMsg.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(noDataMsg, (plotWidth - noDataMsg.DesiredSize.Width) / 2);
                Canvas.SetTop(noDataMsg, plotHeight / 2 - 20);
                canvas.Children.Add(noDataMsg);
                txtStatus.Text = "No events match the selected gate";
                return;
            }

            // Downsample for display if too many events (saves memory and improves performance)
            const int MaxDisplayEvents = 50000;
            if (displayIndices.Count > MaxDisplayEvents && !isExport)
            {
                var rand = new Random(42); // Fixed seed for consistent display
                displayIndices = displayIndices.OrderBy(x => rand.Next()).Take(MaxDisplayEvents).ToList();
            }

            var xValues = displayIndices.Select(i => selectedFile.Events[i][xIndex]).Where(v => v > 0).ToList();
            var yValues = displayIndices.Select(i => selectedFile.Events[i][yIndex]).Where(v => v > 0).ToList();

            double xMin = customXMin ?? (xLogScale ? 0.1 : 0);
            double xMax = customXMax ?? (xValues.Count > 0 ? xValues.Max() * 2 : 262144);
            double yMin = customYMin ?? (yLogScale ? 0.1 : 0);
            double yMax = customYMax ?? (yValues.Count > 0 ? yValues.Max() * 2 : 262144);

            var plotData = displayIndices.Select(i => selectedFile.Events[i]).ToList();

            canvas.Children.Add(new Rectangle { Width = plotWidth, Height = plotHeight, Fill = Brushes.White });

            string usePlotType = isExport ? exportPlotType : plotType;
            if (usePlotType == "contour")
                DrawContourPlot(canvas, plotData, xIndex, yIndex, xMin, xMax, yMin, yMax);
            else
                DrawDotPlot(canvas, plotData, xIndex, yIndex, xMin, xMax, yMin, yMax);

            foreach (var gate in gateTemplates)
            {
                if (gate.GateType != GateType.Polygon) continue;

                int gateXIndex = FindParameterIndex(selectedFile, gate.XParamName);
                int gateYIndex = FindParameterIndex(selectedFile, gate.YParamName);
                if (gateXIndex == xIndex && gateYIndex == yIndex)
                {
                    DrawPolygonGate(canvas, gate, xMin, xMax, yMin, yMax);
                }
            }

            if (currentPolygonPoints.Count > 0 && !isExport)
                DrawCurrentPolygon(canvas, xMin, xMax, yMin, yMax);

            if (quadrantPosition.HasValue)
                DrawQuadrant(canvas, plotData, xIndex, yIndex, xMin, xMax, yMin, yMax);

            DrawAxes(canvas, xMin, xMax, yMin, yMax);

            int validCount = plotData.Count(ev => ev[xIndex] > 0 && ev[yIndex] > 0);
            int totalEvents = GetDisplayEventIndices().Count;
            if (validCount < totalEvents)
                txtStatus.Text = $"Displaying {validCount:N0} of {totalEvents:N0} events (downsampled)";
            else
                txtStatus.Text = $"Displaying {validCount:N0} events";
        }

        private void DrawDotPlot(Canvas canvas, List<float[]> data, int xIndex, int yIndex, double xMin, double xMax, double yMin, double yMax)
        {
            var densityGrid = new Dictionary<string, int>();
            int gridSize = 3;

            foreach (var ev in data)
            {
                if (ev[xIndex] <= 0 || ev[yIndex] <= 0) continue;
                double px = DataToScreenX(ev[xIndex], xMin, xMax);
                double py = DataToScreenY(ev[yIndex], yMin, yMax);
                int gx = (int)(px / gridSize) * gridSize;
                int gy = (int)(py / gridSize) * gridSize;
                string key = $"{gx},{gy}";
                if (!densityGrid.ContainsKey(key)) densityGrid[key] = 0;
                densityGrid[key]++;
            }

            int maxDensity = densityGrid.Values.Count > 0 ? densityGrid.Values.Max() : 1;

            foreach (var kvp in densityGrid)
            {
                var parts = kvp.Key.Split(',');
                int px = int.Parse(parts[0]);
                int py = int.Parse(parts[1]);
                double t = Math.Log(kvp.Value + 1) / Math.Log(maxDensity + 1);
                var rect = new Rectangle
                {
                    Width = gridSize,
                    Height = gridSize,
                    Fill = new SolidColorBrush(GetDotColor(t)),
                    Opacity = 0.9
                };
                Canvas.SetLeft(rect, px);
                Canvas.SetTop(rect, py);
                canvas.Children.Add(rect);
            }
        }

        private void DrawContourPlot(Canvas canvas, List<float[]> data, int xIndex, int yIndex, double xMin, double xMax, double yMin, double yMax)
        {
            int gridResolution = 50;
            var densityGrid = new double[gridResolution, gridResolution];

            double plotLeft = plotMargin.Left;
            double plotRight = plotWidth - plotMargin.Right;
            double plotTop = plotMargin.Top;
            double plotBottom = plotHeight - plotMargin.Bottom;

            foreach (var ev in data)
            {
                if (ev[xIndex] <= 0 || ev[yIndex] <= 0) continue;
                double px = DataToScreenX(ev[xIndex], xMin, xMax);
                double py = DataToScreenY(ev[yIndex], yMin, yMax);

                int gx = (int)((px - plotLeft) / (plotRight - plotLeft) * (gridResolution - 1));
                int gy = (int)((py - plotTop) / (plotBottom - plotTop) * (gridResolution - 1));
                gx = Math.Max(0, Math.Min(gridResolution - 1, gx));
                gy = Math.Max(0, Math.Min(gridResolution - 1, gy));

                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        int nx = gx + dx, ny = gy + dy;
                        if (nx >= 0 && nx < gridResolution && ny >= 0 && ny < gridResolution)
                        {
                            double dist = Math.Sqrt(dx * dx + dy * dy);
                            densityGrid[nx, ny] += Math.Exp(-dist * dist / 2);
                        }
                    }
            }

            double maxDensity = 0;
            for (int i = 0; i < gridResolution; i++)
                for (int j = 0; j < gridResolution; j++)
                    maxDensity = Math.Max(maxDensity, densityGrid[i, j]);

            if (maxDensity == 0) return;

            double cellWidth = (plotRight - plotLeft) / gridResolution;
            double cellHeight = (plotBottom - plotTop) / gridResolution;

            for (int i = 0; i < gridResolution; i++)
                for (int j = 0; j < gridResolution; j++)
                {
                    double density = densityGrid[i, j];
                    if (density > maxDensity * 0.02)
                    {
                        var rect = new Rectangle
                        {
                            Width = cellWidth + 1,
                            Height = cellHeight + 1,
                            Fill = new SolidColorBrush(GetContourColor(density / maxDensity)),
                            Opacity = 0.8
                        };
                        Canvas.SetLeft(rect, plotLeft + i * cellWidth);
                        Canvas.SetTop(rect, plotTop + j * cellHeight);
                        canvas.Children.Add(rect);
                    }
                }
        }

        private void DrawPolygonGate(Canvas canvas, GateTemplate gate, double xMin, double xMax, double yMin, double yMax)
        {
            var screenPoints = new PointCollection();
            foreach (var pt in gate.Points)
            {
                double sx = DataToScreenX(pt.X, xMin, xMax);
                double sy = DataToScreenY(pt.Y, yMin, yMax);
                screenPoints.Add(new Point(sx, sy));
            }

            canvas.Children.Add(new Polygon
            {
                Points = screenPoints,
                Fill = new SolidColorBrush(Color.FromArgb(40, 0, 128, 0)),
                Stroke = new SolidColorBrush(Color.FromRgb(0, 128, 0)),
                StrokeThickness = 2
            });

            if (screenPoints.Count > 0)
            {
                int count = GetGateEventCount(gate.Name);
                var label = new TextBlock
                {
                    Text = $"{gate.Name}: {count:N0}",
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 100, 0)),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
                };
                Canvas.SetLeft(label, screenPoints[0].X + 5);
                Canvas.SetTop(label, screenPoints[0].Y - 18);
                canvas.Children.Add(label);
            }
        }

        private void DrawCurrentPolygon(Canvas canvas, double xMin, double xMax, double yMin, double yMax)
        {
            for (int i = 0; i < currentPolygonPoints.Count - 1; i++)
            {
                var p1 = currentPolygonPoints[i];
                var p2 = currentPolygonPoints[i + 1];
                canvas.Children.Add(new Line
                {
                    X1 = DataToScreenX(p1.X, xMin, xMax),
                    Y1 = DataToScreenY(p1.Y, yMin, yMax),
                    X2 = DataToScreenX(p2.X, xMin, xMax),
                    Y2 = DataToScreenY(p2.Y, yMin, yMax),
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 140, 0)),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 5, 5 }
                });
            }

            for (int i = 0; i < currentPolygonPoints.Count; i++)
            {
                var pt = currentPolygonPoints[i];
                var ellipse = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = new SolidColorBrush(i == 0 ? Colors.Red : Color.FromRgb(255, 140, 0)),
                    Stroke = Brushes.Black,
                    StrokeThickness = 1
                };
                Canvas.SetLeft(ellipse, DataToScreenX(pt.X, xMin, xMax) - 5);
                Canvas.SetTop(ellipse, DataToScreenY(pt.Y, yMin, yMax) - 5);
                canvas.Children.Add(ellipse);
            }
        }

        private void DrawQuadrant(Canvas canvas, List<float[]> data, int xIndex, int yIndex, double xMin, double xMax, double yMin, double yMax)
        {
            if (!quadrantPosition.HasValue) return;

            double qx = DataToScreenX(quadrantPosition.Value.X, xMin, xMax);
            double qy = DataToScreenY(quadrantPosition.Value.Y, yMin, yMax);

            canvas.Children.Add(new Line { X1 = qx, Y1 = plotMargin.Top, X2 = qx, Y2 = plotHeight - plotMargin.Bottom, Stroke = Brushes.Black, StrokeThickness = 1.5 });
            canvas.Children.Add(new Line { X1 = plotMargin.Left, Y1 = qy, X2 = plotWidth - plotMargin.Right, Y2 = qy, Stroke = Brushes.Black, StrokeThickness = 1.5 });

            var validData = data.Where(ev => ev[xIndex] > 0 && ev[yIndex] > 0).ToList();
            int total = validData.Count;
            if (total > 0)
            {
                int q1 = validData.Count(ev => ev[xIndex] >= quadrantPosition.Value.X && ev[yIndex] >= quadrantPosition.Value.Y);
                int q2 = validData.Count(ev => ev[xIndex] < quadrantPosition.Value.X && ev[yIndex] >= quadrantPosition.Value.Y);
                int q3 = validData.Count(ev => ev[xIndex] < quadrantPosition.Value.X && ev[yIndex] < quadrantPosition.Value.Y);
                int q4 = validData.Count(ev => ev[xIndex] >= quadrantPosition.Value.X && ev[yIndex] < quadrantPosition.Value.Y);

                AddQuadrantLabel(canvas, $"Q1: {(q1 * 100.0 / total):F1}%", plotWidth - plotMargin.Right - 10, plotMargin.Top + 10, Colors.Red, HorizontalAlignment.Right);
                AddQuadrantLabel(canvas, $"Q2: {(q2 * 100.0 / total):F1}%", plotMargin.Left + 10, plotMargin.Top + 10, Colors.Blue, HorizontalAlignment.Left);
                AddQuadrantLabel(canvas, $"Q3: {(q3 * 100.0 / total):F1}%", plotMargin.Left + 10, plotHeight - plotMargin.Bottom - 25, Colors.Gray, HorizontalAlignment.Left);
                AddQuadrantLabel(canvas, $"Q4: {(q4 * 100.0 / total):F1}%", plotWidth - plotMargin.Right - 10, plotHeight - plotMargin.Bottom - 25, Color.FromRgb(255, 140, 0), HorizontalAlignment.Right);
            }
        }

        private void AddQuadrantLabel(Canvas canvas, string text, double x, double y, Color color, HorizontalAlignment align)
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(color),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
            };
            if (align == HorizontalAlignment.Right)
            {
                label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                x -= label.DesiredSize.Width;
            }
            Canvas.SetLeft(label, x);
            Canvas.SetTop(label, y);
            canvas.Children.Add(label);
        }

        private void DrawAxes(Canvas canvas, double xMin, double xMax, double yMin, double yMax)
        {
            if (selectedFile == null) return;

            double plotLeft = plotMargin.Left;
            double plotRight = plotWidth - plotMargin.Right;
            double plotTop = plotMargin.Top;
            double plotBottom = plotHeight - plotMargin.Bottom;

            canvas.Children.Add(new Line { X1 = plotLeft, Y1 = plotBottom, X2 = plotRight, Y2 = plotBottom, Stroke = Brushes.Black, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = plotLeft, Y1 = plotTop, X2 = plotLeft, Y2 = plotBottom, Stroke = Brushes.Black, StrokeThickness = 1 });

            foreach (var tick in GetAxisTicks(xMin, xMax, xLogScale))
            {
                double x = DataToScreenX(tick, xMin, xMax);
                if (x >= plotLeft && x <= plotRight)
                {
                    canvas.Children.Add(new Line { X1 = x, Y1 = plotBottom, X2 = x, Y2 = plotBottom + 5, Stroke = Brushes.Black, StrokeThickness = 1 });
                    var label = new TextBlock { Text = FormatAxisValue(tick), Foreground = Brushes.Black, FontSize = 10 };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(label, x - label.DesiredSize.Width / 2);
                    Canvas.SetTop(label, plotBottom + 7);
                    canvas.Children.Add(label);
                }
            }

            foreach (var tick in GetAxisTicks(yMin, yMax, yLogScale))
            {
                double y = DataToScreenY(tick, yMin, yMax);
                if (y >= plotTop && y <= plotBottom)
                {
                    canvas.Children.Add(new Line { X1 = plotLeft - 5, Y1 = y, X2 = plotLeft, Y2 = y, Stroke = Brushes.Black, StrokeThickness = 1 });
                    var label = new TextBlock { Text = FormatAxisValue(tick), Foreground = Brushes.Black, FontSize = 10 };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(label, plotLeft - label.DesiredSize.Width - 8);
                    Canvas.SetTop(label, y - label.DesiredSize.Height / 2);
                    canvas.Children.Add(label);
                }
            }

            if (cboXParam.SelectedIndex >= 0)
            {
                var xLabel = new TextBlock { Text = selectedFile.Parameters[cboXParam.SelectedIndex].Label, Foreground = Brushes.Black, FontSize = 12, FontWeight = FontWeights.SemiBold };
                xLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(xLabel, (plotLeft + plotRight) / 2 - xLabel.DesiredSize.Width / 2);
                Canvas.SetTop(xLabel, plotHeight - 18);
                canvas.Children.Add(xLabel);
            }

            if (cboYParam.SelectedIndex >= 0)
            {
                var yLabel = new TextBlock { Text = selectedFile.Parameters[cboYParam.SelectedIndex].Label, Foreground = Brushes.Black, FontSize = 12, FontWeight = FontWeights.SemiBold, RenderTransform = new RotateTransform(-90) };
                yLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(yLabel, 5);
                Canvas.SetTop(yLabel, (plotTop + plotBottom) / 2 + yLabel.DesiredSize.Width / 2);
                canvas.Children.Add(yLabel);
            }
        }

        private void DrawHistogram(Canvas canvas, bool isExport)
        {
            if (selectedFile == null || cboHistParam.SelectedIndex < 0) return;

            int paramIndex = cboHistParam.SelectedIndex;
            var displayIndices = GetDisplayEventIndices();
            var values = displayIndices.Select(i => selectedFile.Events[i][paramIndex]).Where(v => v > 0).ToList();

            // Show message if no events to display
            if (values.Count == 0)
            {
                canvas.Children.Add(new Rectangle { Width = plotWidth, Height = plotHeight, Fill = Brushes.White });
                var noDataMsg = new TextBlock
                {
                    Text = "No events to display\n(Gate may not contain events in this sample)",
                    Foreground = Brushes.Gray,
                    FontSize = 14,
                    TextAlignment = TextAlignment.Center
                };
                noDataMsg.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(noDataMsg, (plotWidth - noDataMsg.DesiredSize.Width) / 2);
                Canvas.SetTop(noDataMsg, plotHeight / 2 - 20);
                canvas.Children.Add(noDataMsg);
                txtStatus.Text = "No events match the selected gate";
                return;
            }

            double dataMax = values.Max();

            histXMin = histLogScale ? 0.1 : 0;
            histXMax = dataMax * 2;

            canvas.Children.Add(new Rectangle { Width = plotWidth, Height = plotHeight, Fill = Brushes.White });

            int numBins = 256;
            var bins = new int[numBins];
            double[] binEdges = new double[numBins + 1];

            if (histLogScale)
            {
                double logMin = Math.Log10(Math.Max(0.1, histXMin));
                double logMax = Math.Log10(histXMax);
                for (int i = 0; i <= numBins; i++)
                    binEdges[i] = Math.Pow(10, logMin + (logMax - logMin) * i / numBins);
            }
            else
            {
                for (int i = 0; i <= numBins; i++)
                    binEdges[i] = histXMin + (histXMax - histXMin) * i / numBins;
            }

            foreach (var val in values)
                for (int i = 0; i < numBins; i++)
                    if (val >= binEdges[i] && val < binEdges[i + 1]) { bins[i]++; break; }

            int maxBin = bins.Max();
            var mainColor = GetHistogramColor();

            DrawHistogramCurve(canvas, bins, binEdges, maxBin, histXMin, histXMax, Color.FromArgb(150, mainColor.R, mainColor.G, mainColor.B), mainColor);

            int overlayEventCount = 0;
            if (chkOverlay.IsChecked == true && overlayFile != null)
            {
                // Find the same parameter in overlay file by name
                string paramName = selectedFile.Parameters[paramIndex].Label;
                int overlayParamIndex = FindParameterIndex(overlayFile, paramName);

                if (overlayParamIndex >= 0)
                {
                    // Get gated events for overlay file (apply same parent gate)
                    var overlayDisplayIndices = GetDisplayEventIndicesForFile(overlayFile);
                    var overlayValues = overlayDisplayIndices
                        .Select(i => overlayFile.Events[i][overlayParamIndex])
                        .Where(v => v > 0)
                        .ToList();

                    overlayEventCount = overlayValues.Count;

                    var overlayBins = new int[numBins];
                    foreach (var val in overlayValues)
                        for (int i = 0; i < numBins; i++)
                            if (val >= binEdges[i] && val < binEdges[i + 1]) { overlayBins[i]++; break; }

                    if (overlayBins.Length > 0 && overlayBins.Max() > 0)
                    {
                        maxBin = Math.Max(maxBin, overlayBins.Max());
                        canvas.Children.Clear();
                        canvas.Children.Add(new Rectangle { Width = plotWidth, Height = plotHeight, Fill = Brushes.White });
                        DrawHistogramCurve(canvas, bins, binEdges, maxBin, histXMin, histXMax, Color.FromArgb(150, mainColor.R, mainColor.G, mainColor.B), mainColor);
                        var overlayColor = Color.FromRgb(220, 20, 60);
                        DrawHistogramCurve(canvas, overlayBins, binEdges, maxBin, histXMin, histXMax, Color.FromArgb(150, overlayColor.R, overlayColor.G, overlayColor.B), overlayColor);
                    }
                }
            }

            // Draw range gates on histogram
            DrawHistogramRangeGates(canvas, values, paramIndex, histXMin, histXMax);

            // Draw range gate in progress
            if (histRangeStart.HasValue && !isExport)
            {
                DrawHistogramRangeMarker(canvas, histRangeStart.Value, histXMin, histXMax, Colors.Orange, "Start");
            }

            // Draw threshold line with above/below percentages (only in threshold mode)
            if (histThreshold.HasValue && !isExport && histGatingMode == "threshold")
            {
                DrawHistogramThreshold(canvas, values, histThreshold.Value, histXMin, histXMax);
            }

            DrawHistogramAxes(canvas, histXMin, histXMax, maxBin, paramIndex);

            if (overlayEventCount > 0)
                txtStatus.Text = $"Displaying {values.Count:N0} events (overlay: {overlayEventCount:N0})";
            else
                txtStatus.Text = $"Displaying {values.Count:N0} events";
        }

        private void DrawHistogramRangeGates(Canvas canvas, List<float> values, int paramIndex, double xMin, double xMax)
        {
            string currentParamName = selectedFile!.Parameters[paramIndex].Label;

            foreach (var gate in gateTemplates)
            {
                if (gate.GateType != GateType.Range) continue;
                if (!gate.XParamName.Equals(currentParamName, StringComparison.OrdinalIgnoreCase)) continue;

                double leftX = HistDataToScreenX(gate.RangeMin, xMin, xMax);
                double rightX = HistDataToScreenX(gate.RangeMax, xMin, xMax);
                double plotTop = plotMargin.Top;
                double plotBottom = plotHeight - plotMargin.Bottom;

                // Draw shaded region
                var rect = new Rectangle
                {
                    Width = rightX - leftX,
                    Height = plotBottom - plotTop,
                    Fill = new SolidColorBrush(Color.FromArgb(50, 0, 128, 0)),
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 128, 0)),
                    StrokeThickness = 2
                };
                Canvas.SetLeft(rect, leftX);
                Canvas.SetTop(rect, plotTop);
                canvas.Children.Add(rect);

                // Draw gate label
                int count = GetGateEventCount(gate.Name);
                double percent = values.Count > 0 ? (count * 100.0 / values.Count) : 0;
                var label = new TextBlock
                {
                    Text = $"{gate.Name}: {count:N0} ({percent:F1}%)",
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 100, 0)),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
                };
                Canvas.SetLeft(label, leftX + 5);
                Canvas.SetTop(label, plotTop + 5);
                canvas.Children.Add(label);
            }
        }

        private void DrawHistogramRangeMarker(Canvas canvas, double dataValue, double xMin, double xMax, Color color, string text)
        {
            double screenX = HistDataToScreenX(dataValue, xMin, xMax);
            double plotTop = plotMargin.Top;
            double plotBottom = plotHeight - plotMargin.Bottom;

            canvas.Children.Add(new Line
            {
                X1 = screenX,
                Y1 = plotTop,
                X2 = screenX,
                Y2 = plotBottom,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 }
            });

            var label = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(color),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255))
            };
            Canvas.SetLeft(label, screenX + 3);
            Canvas.SetTop(label, plotTop + 5);
            canvas.Children.Add(label);
        }

        private void DrawHistogramThreshold(Canvas canvas, List<float> values, double threshold, double xMin, double xMax)
        {
            double screenX = HistDataToScreenX(threshold, xMin, xMax);
            double plotTop = plotMargin.Top;
            double plotBottom = plotHeight - plotMargin.Bottom;
            double plotLeft = plotMargin.Left;
            double plotRight = plotWidth - plotMargin.Right;

            // Calculate counts above and below threshold
            int total = values.Count;
            int aboveCount = values.Count(v => v >= threshold);
            int belowCount = total - aboveCount;
            double abovePercent = total > 0 ? (aboveCount * 100.0 / total) : 0;
            double belowPercent = total > 0 ? (belowCount * 100.0 / total) : 0;

            // Draw shaded regions
            // Below threshold (left side) - light blue
            var belowRect = new Rectangle
            {
                Width = Math.Max(0, screenX - plotLeft),
                Height = plotBottom - plotTop,
                Fill = new SolidColorBrush(Color.FromArgb(40, 0, 100, 200))
            };
            Canvas.SetLeft(belowRect, plotLeft);
            Canvas.SetTop(belowRect, plotTop);
            canvas.Children.Add(belowRect);

            // Above threshold (right side) - light red
            var aboveRect = new Rectangle
            {
                Width = Math.Max(0, plotRight - screenX),
                Height = plotBottom - plotTop,
                Fill = new SolidColorBrush(Color.FromArgb(40, 200, 50, 50))
            };
            Canvas.SetLeft(aboveRect, screenX);
            Canvas.SetTop(aboveRect, plotTop);
            canvas.Children.Add(aboveRect);

            // Draw threshold line
            canvas.Children.Add(new Line
            {
                X1 = screenX,
                Y1 = plotTop,
                X2 = screenX,
                Y2 = plotBottom,
                Stroke = new SolidColorBrush(Color.FromRgb(128, 0, 128)),
                StrokeThickness = 2.5
            });

            // Draw labels
            // Below label (left side)
            var belowLabel = new TextBlock
            {
                Text = $"< {belowCount:N0}\n({belowPercent:F1}%)",
                Foreground = new SolidColorBrush(Color.FromRgb(0, 80, 160)),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4, 2, 4, 2)
            };
            Canvas.SetLeft(belowLabel, plotLeft + 10);
            Canvas.SetTop(belowLabel, plotTop + 20);
            canvas.Children.Add(belowLabel);

            // Above label (right side)
            var aboveLabel = new TextBlock
            {
                Text = $"≥ {aboveCount:N0}\n({abovePercent:F1}%)",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 40, 40)),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                TextAlignment = TextAlignment.Center,
                Padding = new Thickness(4, 2, 4, 2)
            };
            aboveLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(aboveLabel, plotRight - aboveLabel.DesiredSize.Width - 10);
            Canvas.SetTop(aboveLabel, plotTop + 20);
            canvas.Children.Add(aboveLabel);

            // Threshold value label
            var thresholdLabel = new TextBlock
            {
                Text = $"Threshold: {FormatAxisValue(threshold)}",
                Foreground = new SolidColorBrush(Color.FromRgb(128, 0, 128)),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                Padding = new Thickness(3, 1, 3, 1)
            };
            Canvas.SetLeft(thresholdLabel, screenX + 5);
            Canvas.SetTop(thresholdLabel, plotBottom - 20);
            canvas.Children.Add(thresholdLabel);

            // Update status bar
            txtStatus.Text = $"Threshold at {FormatAxisValue(threshold)}: Below = {belowCount:N0} ({belowPercent:F1}%), Above = {aboveCount:N0} ({abovePercent:F1}%)";
        }

        private void DrawHistogramCurve(Canvas canvas, int[] bins, double[] binEdges, int maxBin, double xMin, double xMax, Color fillColor, Color strokeColor)
        {
            double plotLeft = plotMargin.Left;
            double plotRight = plotWidth - plotMargin.Right;
            double plotTop = plotMargin.Top;
            double plotBottom = plotHeight - plotMargin.Bottom;

            var points = new List<Point> { new Point(plotLeft, plotBottom) };
            for (int i = 0; i < bins.Length; i++)
            {
                double x = HistDataToScreenX((binEdges[i] + binEdges[i + 1]) / 2, xMin, xMax);
                double y = plotBottom - (bins[i] / (double)maxBin) * (plotBottom - plotTop - 20);
                points.Add(new Point(x, y));
            }
            points.Add(new Point(plotRight, plotBottom));

            var fillGeometry = new PathGeometry();
            var fillFigure = new PathFigure { StartPoint = points[0] };
            fillFigure.Segments.Add(new PolyLineSegment(points.Skip(1), true));
            fillGeometry.Figures.Add(fillFigure);
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = fillGeometry, Fill = new SolidColorBrush(fillColor) });

            var strokeGeometry = new PathGeometry();
            var strokeFigure = new PathFigure { StartPoint = points[1] };
            strokeFigure.Segments.Add(new PolyLineSegment(points.Skip(2).Take(points.Count - 3), true));
            strokeGeometry.Figures.Add(strokeFigure);
            canvas.Children.Add(new System.Windows.Shapes.Path { Data = strokeGeometry, Stroke = new SolidColorBrush(strokeColor), StrokeThickness = 1.5 });
        }

        private void DrawHistogramAxes(Canvas canvas, double xMin, double xMax, int maxBin, int paramIndex)
        {
            if (selectedFile == null) return;
            double plotLeft = plotMargin.Left;
            double plotRight = plotWidth - plotMargin.Right;
            double plotBottom = plotHeight - plotMargin.Bottom;

            canvas.Children.Add(new Line { X1 = plotLeft, Y1 = plotBottom, X2 = plotRight, Y2 = plotBottom, Stroke = Brushes.Black, StrokeThickness = 1 });
            canvas.Children.Add(new Line { X1 = plotLeft, Y1 = plotMargin.Top, X2 = plotLeft, Y2 = plotBottom, Stroke = Brushes.Black, StrokeThickness = 1 });

            foreach (var tick in GetAxisTicks(xMin, xMax, histLogScale))
            {
                double x = HistDataToScreenX(tick, xMin, xMax);
                if (x >= plotLeft && x <= plotRight)
                {
                    var label = new TextBlock { Text = FormatAxisValue(tick), Foreground = Brushes.Black, FontSize = 10 };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    Canvas.SetLeft(label, x - label.DesiredSize.Width / 2);
                    Canvas.SetTop(label, plotBottom + 7);
                    canvas.Children.Add(label);
                }
            }

            var xLabel = new TextBlock { Text = selectedFile.Parameters[paramIndex].Label, Foreground = Brushes.Black, FontSize = 12, FontWeight = FontWeights.SemiBold };
            xLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(xLabel, (plotLeft + plotRight) / 2 - xLabel.DesiredSize.Width / 2);
            Canvas.SetTop(xLabel, plotHeight - 18);
            canvas.Children.Add(xLabel);

            var yLabel = new TextBlock { Text = "Count", Foreground = Brushes.Black, FontSize = 12, FontWeight = FontWeights.SemiBold, RenderTransform = new RotateTransform(-90) };
            Canvas.SetLeft(yLabel, 5);
            Canvas.SetTop(yLabel, plotHeight / 2 + 20);
            canvas.Children.Add(yLabel);
        }

        #endregion

        #region Export

        private void BtnExportDot_Click(object sender, RoutedEventArgs e) => ExportPlot("dot");
        private void BtnExportContour_Click(object sender, RoutedEventArgs e) => ExportPlot("contour");
        private void BtnExportHistogram_Click(object sender, RoutedEventArgs e) => ExportHistogram();

        private void ExportPlot(string type)
        {
            if (selectedFile == null) return;
            var canvas = GetCurrentCanvas();
            if (canvas == null) return;

            var dialog = new SaveFileDialog { Filter = "PNG Image (*.png)|*.png", FileName = $"scatter_{type}_{DateTime.Now:yyyyMMdd_HHmmss}.png" };
            if (dialog.ShowDialog() == true)
            {
                var currentChildren = canvas.Children.Cast<UIElement>().ToList();
                canvas.Children.Clear();
                DrawScatterPlot(canvas, true, type);

                var renderBitmap = new RenderTargetBitmap((int)(plotWidth * 2), (int)(plotHeight * 2), 192, 192, PixelFormats.Pbgra32);
                canvas.Measure(new Size(plotWidth, plotHeight));
                canvas.Arrange(new Rect(0, 0, plotWidth, plotHeight));
                renderBitmap.Render(canvas);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                using (var fs = new FileStream(dialog.FileName, FileMode.Create)) encoder.Save(fs);

                canvas.Children.Clear();
                foreach (var child in currentChildren) canvas.Children.Add(child);
                txtStatus.Text = $"Exported to {dialog.FileName}";
            }
        }

        private void ExportHistogram()
        {
            if (selectedFile == null) return;
            var canvas = GetCurrentCanvas();
            if (canvas == null) return;

            var dialog = new SaveFileDialog { Filter = "PNG Image (*.png)|*.png", FileName = $"histogram_{DateTime.Now:yyyyMMdd_HHmmss}.png" };
            if (dialog.ShowDialog() == true)
            {
                var currentChildren = canvas.Children.Cast<UIElement>().ToList();
                canvas.Children.Clear();
                DrawHistogram(canvas, true);

                var renderBitmap = new RenderTargetBitmap((int)(plotWidth * 2), (int)(plotHeight * 2), 192, 192, PixelFormats.Pbgra32);
                canvas.Measure(new Size(plotWidth, plotHeight));
                canvas.Arrange(new Rect(0, 0, plotWidth, plotHeight));
                renderBitmap.Render(canvas);

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                using (var fs = new FileStream(dialog.FileName, FileMode.Create)) encoder.Save(fs);

                canvas.Children.Clear();
                foreach (var child in currentChildren) canvas.Children.Add(child);
                txtStatus.Text = $"Exported to {dialog.FileName}";
            }
        }

        #endregion

        #region Statistics Recording

        private void BtnRecordStats_Click(object sender, RoutedEventArgs e)
        {
            if (selectedFile == null)
            {
                MessageBox.Show("Please load a file first.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (currentView == "histogram")
            {
                RecordHistogramStats();
            }
            else
            {
                RecordScatterStats();
            }

            // Auto-expand the stats panel
            pnlStats.Visibility = Visibility.Visible;
        }

        private void RecordHistogramStats()
        {
            if (selectedFile == null || cboHistParam.SelectedIndex < 0) return;

            int paramIndex = cboHistParam.SelectedIndex;
            string paramName = selectedFile.Parameters[paramIndex].Label;
            var displayIndices = GetDisplayEventIndices();
            var values = displayIndices.Select(i => selectedFile.Events[i][paramIndex]).Where(v => v > 0).ToList();
            int totalEvents = values.Count;

            string parentGate = histParentGateIndex >= 0 && histParentGateIndex < gateTemplates.Count
                ? gateTemplates[histParentGateIndex].Name : "All Events";

            // Record Threshold if active
            if (histThreshold.HasValue && histGatingMode == "threshold")
            {
                int aboveCount = values.Count(v => v >= histThreshold.Value);
                int belowCount = totalEvents - aboveCount;
                double abovePercent = totalEvents > 0 ? (aboveCount * 100.0 / totalEvents) : 0;
                double belowPercent = totalEvents > 0 ? (belowCount * 100.0 / totalEvents) : 0;

                statsRecords.Add(new StatsRecord
                {
                    SampleName = selectedFile.Filename,
                    ViewType = "Histogram",
                    Parameters = paramName,
                    GateRegion = $"< {FormatAxisValue(histThreshold.Value)}",
                    Count = belowCount.ToString("N0"),
                    Percentage = $"{belowPercent:F2}%",
                    ParentGate = parentGate,
                    Details = $"Threshold: {FormatAxisValue(histThreshold.Value)}"
                });

                statsRecords.Add(new StatsRecord
                {
                    SampleName = selectedFile.Filename,
                    ViewType = "Histogram",
                    Parameters = paramName,
                    GateRegion = $"≥ {FormatAxisValue(histThreshold.Value)}",
                    Count = aboveCount.ToString("N0"),
                    Percentage = $"{abovePercent:F2}%",
                    ParentGate = parentGate,
                    Details = $"Threshold: {FormatAxisValue(histThreshold.Value)}"
                });

                txtStatus.Text = $"Recorded threshold stats for {selectedFile.Filename}";
            }
            // Record Range Gates
            else
            {
                bool recorded = false;
                foreach (var gate in gateTemplates)
                {
                    if (gate.GateType != GateType.Range) continue;
                    if (!gate.XParamName.Equals(paramName, StringComparison.OrdinalIgnoreCase)) continue;

                    int count = GetGateEventCount(gate.Name);
                    double percent = totalEvents > 0 ? (count * 100.0 / totalEvents) : 0;

                    statsRecords.Add(new StatsRecord
                    {
                        SampleName = selectedFile.Filename,
                        ViewType = "Histogram",
                        Parameters = paramName,
                        GateRegion = gate.Name,
                        Count = count.ToString("N0"),
                        Percentage = $"{percent:F2}%",
                        ParentGate = string.IsNullOrEmpty(gate.ParentGateName) ? "All Events" : gate.ParentGateName,
                        Details = $"Range: {FormatAxisValue(gate.RangeMin)} ~ {FormatAxisValue(gate.RangeMax)}"
                    });
                    recorded = true;
                }

                if (!recorded)
                {
                    // Just record total events
                    statsRecords.Add(new StatsRecord
                    {
                        SampleName = selectedFile.Filename,
                        ViewType = "Histogram",
                        Parameters = paramName,
                        GateRegion = "Total",
                        Count = totalEvents.ToString("N0"),
                        Percentage = "100%",
                        ParentGate = parentGate,
                        Details = ""
                    });
                }

                txtStatus.Text = $"Recorded histogram stats for {selectedFile.Filename}";
            }
        }

        private void RecordScatterStats()
        {
            if (selectedFile == null || cboXParam.SelectedIndex < 0 || cboYParam.SelectedIndex < 0) return;

            int xIndex = cboXParam.SelectedIndex;
            int yIndex = cboYParam.SelectedIndex;
            string xParam = selectedFile.Parameters[xIndex].Label;
            string yParam = selectedFile.Parameters[yIndex].Label;

            var displayIndices = GetDisplayEventIndices();
            var plotData = displayIndices.Select(i => selectedFile.Events[i]).ToList();
            var validData = plotData.Where(ev => ev[xIndex] > 0 && ev[yIndex] > 0).ToList();
            int totalEvents = validData.Count;

            string parentGate = parentGateIndex >= 0 && parentGateIndex < gateTemplates.Count
                ? gateTemplates[parentGateIndex].Name : "All Events";

            // Record Quadrant if active
            if (quadrantPosition.HasValue)
            {
                int q1 = validData.Count(ev => ev[xIndex] >= quadrantPosition.Value.X && ev[yIndex] >= quadrantPosition.Value.Y);
                int q2 = validData.Count(ev => ev[xIndex] < quadrantPosition.Value.X && ev[yIndex] >= quadrantPosition.Value.Y);
                int q3 = validData.Count(ev => ev[xIndex] < quadrantPosition.Value.X && ev[yIndex] < quadrantPosition.Value.Y);
                int q4 = validData.Count(ev => ev[xIndex] >= quadrantPosition.Value.X && ev[yIndex] < quadrantPosition.Value.Y);

                string qDetails = $"X={FormatAxisValue(quadrantPosition.Value.X)}, Y={FormatAxisValue(quadrantPosition.Value.Y)}";

                statsRecords.Add(new StatsRecord
                {
                    SampleName = selectedFile.Filename,
                    ViewType = "Scatter",
                    Parameters = $"{xParam} / {yParam}",
                    GateRegion = "Q1 (+/+)",
                    Count = q1.ToString("N0"),
                    Percentage = $"{(totalEvents > 0 ? q1 * 100.0 / totalEvents : 0):F2}%",
                    ParentGate = parentGate,
                    Details = qDetails
                });

                statsRecords.Add(new StatsRecord
                {
                    SampleName = selectedFile.Filename,
                    ViewType = "Scatter",
                    Parameters = $"{xParam} / {yParam}",
                    GateRegion = "Q2 (-/+)",
                    Count = q2.ToString("N0"),
                    Percentage = $"{(totalEvents > 0 ? q2 * 100.0 / totalEvents : 0):F2}%",
                    ParentGate = parentGate,
                    Details = qDetails
                });

                statsRecords.Add(new StatsRecord
                {
                    SampleName = selectedFile.Filename,
                    ViewType = "Scatter",
                    Parameters = $"{xParam} / {yParam}",
                    GateRegion = "Q3 (-/-)",
                    Count = q3.ToString("N0"),
                    Percentage = $"{(totalEvents > 0 ? q3 * 100.0 / totalEvents : 0):F2}%",
                    ParentGate = parentGate,
                    Details = qDetails
                });

                statsRecords.Add(new StatsRecord
                {
                    SampleName = selectedFile.Filename,
                    ViewType = "Scatter",
                    Parameters = $"{xParam} / {yParam}",
                    GateRegion = "Q4 (+/-)",
                    Count = q4.ToString("N0"),
                    Percentage = $"{(totalEvents > 0 ? q4 * 100.0 / totalEvents : 0):F2}%",
                    ParentGate = parentGate,
                    Details = qDetails
                });

                txtStatus.Text = $"Recorded quadrant stats for {selectedFile.Filename}";
            }
            else
            {
                // Record Polygon Gates on current parameters
                bool recorded = false;
                foreach (var gate in gateTemplates)
                {
                    if (gate.GateType != GateType.Polygon) continue;

                    int gateXIndex = FindParameterIndex(selectedFile, gate.XParamName);
                    int gateYIndex = FindParameterIndex(selectedFile, gate.YParamName);
                    if (gateXIndex != xIndex || gateYIndex != yIndex) continue;

                    int count = GetGateEventCount(gate.Name);
                    double percent = totalEvents > 0 ? (count * 100.0 / totalEvents) : 0;

                    statsRecords.Add(new StatsRecord
                    {
                        SampleName = selectedFile.Filename,
                        ViewType = "Scatter",
                        Parameters = $"{xParam} / {yParam}",
                        GateRegion = gate.Name,
                        Count = count.ToString("N0"),
                        Percentage = $"{percent:F2}%",
                        ParentGate = string.IsNullOrEmpty(gate.ParentGateName) ? "All Events" : gate.ParentGateName,
                        Details = $"Polygon gate ({gate.Points.Count} vertices)"
                    });
                    recorded = true;
                }

                if (!recorded)
                {
                    // Just record total events
                    statsRecords.Add(new StatsRecord
                    {
                        SampleName = selectedFile.Filename,
                        ViewType = "Scatter",
                        Parameters = $"{xParam} / {yParam}",
                        GateRegion = "Total",
                        Count = totalEvents.ToString("N0"),
                        Percentage = "100%",
                        ParentGate = parentGate,
                        Details = ""
                    });
                }

                txtStatus.Text = $"Recorded scatter stats for {selectedFile.Filename}";
            }
        }

        private void BtnClearStats_Click(object sender, RoutedEventArgs e)
        {
            statsRecords.Clear();
            txtStatus.Text = "Statistics records cleared";
        }

        private void BtnExportStats_Click(object sender, RoutedEventArgs e)
        {
            if (statsRecords.Count == 0)
            {
                MessageBox.Show("No statistics to export.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"flow_stats_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Sample,View,Parameters,Gate/Region,Count,Percentage,Parent Gate,Details");

                foreach (var record in statsRecords)
                {
                    sb.AppendLine($"\"{record.SampleName}\",\"{record.ViewType}\",\"{record.Parameters}\",\"{record.GateRegion}\",\"{record.Count}\",\"{record.Percentage}\",\"{record.ParentGate}\",\"{record.Details}\"");
                }

                File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                txtStatus.Text = $"Statistics exported to {dialog.FileName}";
            }
        }

        private void BtnCopyStats_Click(object sender, RoutedEventArgs e)
        {
            if (statsRecords.Count == 0)
            {
                MessageBox.Show("No statistics to copy.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("Sample\tView\tParameters\tGate/Region\tCount\tPercentage\tParent Gate\tDetails");

            foreach (var record in statsRecords)
            {
                sb.AppendLine($"{record.SampleName}\t{record.ViewType}\t{record.Parameters}\t{record.GateRegion}\t{record.Count}\t{record.Percentage}\t{record.ParentGate}\t{record.Details}");
            }

            Clipboard.SetText(sb.ToString());
            txtStatus.Text = $"Copied {statsRecords.Count} records to clipboard";
        }

        private void BtnHideStats_Click(object sender, RoutedEventArgs e)
        {
            pnlStats.Visibility = Visibility.Collapsed;
        }

        private void BtnPrevSample_Click(object sender, RoutedEventArgs e)
        {
            if (fcsFiles.Count == 0) return;

            int currentIndex = cboFiles.SelectedIndex;
            if (currentIndex > 0)
            {
                cboFiles.SelectedIndex = currentIndex - 1;
            }
            else
            {
                // Wrap to last sample
                cboFiles.SelectedIndex = fcsFiles.Count - 1;
            }
        }

        private void BtnNextSample_Click(object sender, RoutedEventArgs e)
        {
            if (fcsFiles.Count == 0) return;

            int currentIndex = cboFiles.SelectedIndex;
            if (currentIndex < fcsFiles.Count - 1)
            {
                cboFiles.SelectedIndex = currentIndex + 1;
            }
            else
            {
                // Wrap to first sample
                cboFiles.SelectedIndex = 0;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.M:
                        BtnRecordStats_Click(sender, e);
                        e.Handled = true;
                        break;
                    case Key.N:
                        BtnNextSample_Click(sender, e);
                        e.Handled = true;
                        break;
                    case Key.P:
                        BtnPrevSample_Click(sender, e);
                        e.Handled = true;
                        break;
                }
            }
        }

        #endregion

        #region Color Functions

        private Color GetDotColor(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            switch (dotColormap)
            {
                case "Turbo": return GetTurboColor(t);
                case "Viridis": return GetViridisColor(t);
                case "Plasma": return GetPlasmaColor(t);
                case "Inferno": return GetInfernoColor(t);
                case "Grayscale": byte g = (byte)(255 * (1 - t)); return Color.FromRgb(g, g, g);
                default: return Color.FromRgb((byte)(255 * (1 - t * 0.9)), (byte)(255 * (1 - t * 0.7)), 255);
            }
        }

        private Color GetContourColor(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            switch (contourColormap)
            {
                case "RdYlBu": return GetRdYlBuColor(t);
                case "Spectral": return GetSpectralColor(t);
                case "Hot": return GetHotColor(t);
                case "Cool": return GetCoolColor(t);
                case "Grayscale": byte g = (byte)(255 * t); return Color.FromRgb(g, g, g);
                default: return GetYlOrRdColor(t);
            }
        }

        private Color GetHistogramColor()
        {
            switch (histColor)
            {
                case "Red": return Color.FromRgb(220, 53, 69);
                case "Green": return Color.FromRgb(40, 167, 69);
                case "Orange": return Color.FromRgb(255, 140, 0);
                case "Purple": return Color.FromRgb(128, 0, 128);
                case "Gray": return Color.FromRgb(108, 117, 125);
                default: return Color.FromRgb(65, 105, 225);
            }
        }

        private Color GetTurboColor(double t)
        {
            double r = Math.Max(0, Math.Min(1, 0.13572138 + t * (4.61539260 + t * (-42.66032258 + t * (132.13108234 + t * (-152.94239396 + t * 59.28637943))))));
            double g = Math.Max(0, Math.Min(1, 0.09140261 + t * (2.19418839 + t * (4.84296658 + t * (-14.18503333 + t * (4.27729857 + t * 2.82956604))))));
            double b = Math.Max(0, Math.Min(1, 0.10667330 + t * (12.64194608 + t * (-60.58204836 + t * (110.36276771 + t * (-89.90310912 + t * 27.34824973))))));
            return Color.FromRgb((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
        }

        private Color GetViridisColor(double t)
        {
            double r = 0.267004 + t * (0.282327 + t * (-1.441789 + t * (2.814903 - t * 1.292667)));
            double g = 0.004874 + t * (1.242610 + t * (-0.372511 + t * (-0.556710 + t * 0.692985)));
            double b = 0.329415 + t * (0.753434 + t * (-2.291062 + t * (3.215906 - t * 1.487633)));
            return Color.FromRgb((byte)(Math.Max(0, Math.Min(1, r)) * 255), (byte)(Math.Max(0, Math.Min(1, g)) * 255), (byte)(Math.Max(0, Math.Min(1, b)) * 255));
        }

        private Color GetPlasmaColor(double t)
        {
            double r = 0.050383 + t * (2.028287 + t * (-1.312458 + t * 0.289932));
            double g = 0.029803 + t * (-0.578467 + t * (2.870014 - t * 1.422450));
            double b = 0.527975 + t * (0.956655 + t * (-2.667696 + t * 1.250893));
            return Color.FromRgb((byte)(Math.Max(0, Math.Min(1, r)) * 255), (byte)(Math.Max(0, Math.Min(1, g)) * 255), (byte)(Math.Max(0, Math.Min(1, b)) * 255));
        }

        private Color GetInfernoColor(double t)
        {
            double r = 0.001462 + t * (1.201313 + t * (1.246790 - t * 1.493180));
            double g = 0.000466 + t * (-0.148193 + t * (1.853786 - t * 0.987266));
            double b = 0.013866 + t * (1.497654 + t * (-3.245667 + t * 1.732578));
            return Color.FromRgb((byte)(Math.Max(0, Math.Min(1, r)) * 255), (byte)(Math.Max(0, Math.Min(1, g)) * 255), (byte)(Math.Max(0, Math.Min(1, b)) * 255));
        }

        private Color GetYlOrRdColor(double t)
        {
            if (t < 0.5) { double t2 = t * 2; return Color.FromRgb(255, (byte)(255 - t2 * 127), (byte)(200 - t2 * 150)); }
            else { double t2 = (t - 0.5) * 2; return Color.FromRgb((byte)(255 - t2 * 55), (byte)(128 - t2 * 80), (byte)(50 - t2 * 50)); }
        }

        private Color GetRdYlBuColor(double t)
        {
            if (t < 0.5) { double t2 = t * 2; return Color.FromRgb((byte)(165 + t2 * 90), (byte)(0 + t2 * 255), (byte)(38 + t2 * 102)); }
            else { double t2 = (t - 0.5) * 2; return Color.FromRgb((byte)(255 - t2 * 206), (byte)(255 - t2 * 131), (byte)(140 + t2 * 115)); }
        }

        private Color GetSpectralColor(double t)
        {
            if (t < 0.25) return Color.FromRgb((byte)(158 + t * 4 * 55), (byte)(1 + t * 4 * 128), (byte)(66 - t * 4 * 30));
            else if (t < 0.5) return Color.FromRgb(213, (byte)(129 + (t - 0.25) * 4 * 86), (byte)(36 + (t - 0.25) * 4 * 85));
            else if (t < 0.75) return Color.FromRgb((byte)(213 - (t - 0.5) * 4 * 110), (byte)(215 - (t - 0.5) * 4 * 36), (byte)(121 + (t - 0.5) * 4 * 50));
            else return Color.FromRgb((byte)(103 - (t - 0.75) * 4 * 53), (byte)(179 - (t - 0.75) * 4 * 85), (byte)(171 + (t - 0.75) * 4 * 51));
        }

        private Color GetHotColor(double t)
        {
            if (t < 0.33) return Color.FromRgb((byte)(t * 3 * 255), 0, 0);
            else if (t < 0.67) return Color.FromRgb(255, (byte)((t - 0.33) * 3 * 255), 0);
            else return Color.FromRgb(255, 255, (byte)((t - 0.67) * 3 * 255));
        }

        private Color GetCoolColor(double t) => Color.FromRgb((byte)(t * 255), (byte)((1 - t) * 255), 255);

        #endregion

        #region Utility Functions

        private double DataToScreenX(double value, double xMin, double xMax)
        {
            double plotLeft = plotMargin.Left;
            double plotRight = plotWidth - plotMargin.Right;
            if (xLogScale)
            {
                double logMin = Math.Log10(Math.Max(0.1, xMin));
                double logMax = Math.Log10(xMax);
                double logVal = Math.Log10(Math.Max(0.1, value));
                return plotLeft + (logVal - logMin) / (logMax - logMin) * (plotRight - plotLeft);
            }
            return plotLeft + (value - xMin) / (xMax - xMin) * (plotRight - plotLeft);
        }

        private double DataToScreenY(double value, double yMin, double yMax)
        {
            double plotTop = plotMargin.Top;
            double plotBottom = plotHeight - plotMargin.Bottom;
            if (yLogScale)
            {
                double logMin = Math.Log10(Math.Max(0.1, yMin));
                double logMax = Math.Log10(yMax);
                double logVal = Math.Log10(Math.Max(0.1, value));
                return plotBottom - (logVal - logMin) / (logMax - logMin) * (plotBottom - plotTop);
            }
            return plotBottom - (value - yMin) / (yMax - yMin) * (plotBottom - plotTop);
        }

        private double HistDataToScreenX(double value, double xMin, double xMax)
        {
            double plotLeft = plotMargin.Left;
            double plotRight = plotWidth - plotMargin.Right;
            if (histLogScale)
            {
                double logMin = Math.Log10(Math.Max(0.1, xMin));
                double logMax = Math.Log10(xMax);
                double logVal = Math.Log10(Math.Max(0.1, value));
                return plotLeft + (logVal - logMin) / (logMax - logMin) * (plotRight - plotLeft);
            }
            return plotLeft + (value - xMin) / (xMax - xMin) * (plotRight - plotLeft);
        }

        private List<double> GetAxisTicks(double min, double max, bool isLog)
        {
            var ticks = new List<double>();
            if (isLog)
            {
                double logMin = Math.Floor(Math.Log10(Math.Max(0.1, min)));
                double logMax = Math.Ceiling(Math.Log10(max));
                for (double p = logMin; p <= logMax; p++)
                {
                    double val = Math.Pow(10, p);
                    if (val >= min && val <= max) ticks.Add(val);
                }
            }
            else
            {
                double range = max - min;
                double step = Math.Pow(10, Math.Floor(Math.Log10(range / 5)));
                if (range / step > 10) step *= 2;
                if (range / step < 4) step /= 2;
                for (double v = Math.Ceiling(min / step) * step; v <= max; v += step) ticks.Add(v);
            }
            return ticks;
        }

        private string FormatAxisValue(double value)
        {
            if (value >= 1000000) return $"{value / 1000000:F0}M";
            if (value >= 1000) return $"{value / 1000:F0}K";
            if (value < 1 && value > 0) return $"{value:F1}";
            return $"{value:F0}";
        }

        private bool PointInPolygon(Point point, List<Point> polygon)
        {
            if (polygon.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                    (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                    inside = !inside;
            }
            return inside;
        }

        #endregion
    }

    #region Data Classes

    public class StatsRecord
    {
        public string SampleName { get; set; } = string.Empty;
        public string ViewType { get; set; } = string.Empty;
        public string Parameters { get; set; } = string.Empty;
        public string GateRegion { get; set; } = string.Empty;
        public string Count { get; set; } = string.Empty;
        public string Percentage { get; set; } = string.Empty;
        public string ParentGate { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public class FcsFile
    {
        public string Filename { get; set; } = string.Empty;
        public List<FcsParameter> Parameters { get; set; } = new List<FcsParameter>();
        public List<float[]> Events { get; set; } = new List<float[]>();  // Changed from double[] to float[]
        public int EventCount => Events.Count;
    }

    public class FcsParameter
    {
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
    }

    public enum GateType
    {
        Polygon,
        Range
    }

    public class GateTemplate
    {
        public string Name { get; set; } = string.Empty;
        public GateType GateType { get; set; } = GateType.Polygon;
        public List<Point> Points { get; set; } = new List<Point>();
        public string DisplayName { get; set; } = string.Empty;
        public int XParamIndex { get; set; }
        public int YParamIndex { get; set; }
        public string XParamName { get; set; } = string.Empty;
        public string YParamName { get; set; } = string.Empty;
        public string ParentGateName { get; set; } = string.Empty;

        // For Range gates
        public double RangeMin { get; set; }
        public double RangeMax { get; set; }
    }

    #endregion

    #region Extension Methods

    public static class DictionaryExtensions
    {
        public static string? GetValueOrDefault(this Dictionary<string, string> dict, string key)
        {
            return dict.TryGetValue(key, out string? value) ? value : null;
        }
    }

    #endregion
}
