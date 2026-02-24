using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using BioSAK.Models;
using BioSAK.Services;

namespace BioSAK.Pages
{
    public partial class VectorDesignerPage : Page
    {
        // ===== Data =====
        private ObservableCollection<DnaConstruct> _constructs = new ObservableCollection<DnaConstruct>();
        private DnaConstruct _selectedConstruct;
        private List<RestrictionEnzyme> _allEnzymes;
        private RestrictionEnzyme _selectedEnzyme;
        private readonly RestrictionEnzymeCutter _cutter = new RestrictionEnzymeCutter();
        private List<CutSite> _currentCutSites;
        private int _customSequenceCounter = 1;

        // Undo
        private Stack<List<DnaConstruct>> _undoStack = new Stack<List<DnaConstruct>>();

        // Visualization
        private double _zoom = 1.0;
        private bool _useCircularView = false;
        private const double FEATURE_HEIGHT = 36, FEATURE_GAP = 4;
        private const double LEFT_MARGIN = 60, RIGHT_MARGIN = 60;
        private const double FONT_TITLE = 18, FONT_INFO = 13, FONT_FEATURE = 12;
        private const double FONT_RULER = 11, FONT_CUT = 11, FONT_END = 11;

        // Feature drag
        private bool _isDragging = false;
        private SequenceFeature _dragFeature;
        private double _dragStartX;
        private int _dragOrigStart, _dragOrigEnd;

        // ===== Selection drag for sequence viewer =====
        private bool _isSelecting = false;
        private double _selectStartX;
        private int _selectStartBp = -1, _selectEndBp = -1;
        private System.Windows.Shapes.Rectangle _selectionRect;
        private double _lastTrackY, _lastDrawW;
        private int _lastSeqLen;

        // ===== Constructor =====
        public VectorDesignerPage()
        {
            InitializeComponent();
            LoadEnzymeDatabase();
            LoadVectorLibrary();
            InitializeFeatureTypeComboBox();
            PopulateFlankingComboBoxes();
            ConstructListBox.ItemsSource = _constructs;
            _constructs.CollectionChanged += (s, e) => UpdateCloningComboBoxes();
            CustomSequenceTextBox.TextChanged += (s, e) =>
            {
                string seq = CleanSequence(CustomSequenceTextBox.Text);
                CustomSeqLengthText.Text = $"{seq.Length} bp";
            };
        }

        // ===============================================================
        //  Init
        // ===============================================================
        private void LoadEnzymeDatabase()
        {
            try { _allEnzymes = RebaseParser.LoadEnzymes(); }
            catch { _allEnzymes = new List<RestrictionEnzyme>(); }
            EnzymeListBox.ItemsSource = _allEnzymes;
        }

        private void LoadVectorLibrary()
        {
            try
            {
                var templates = VectorLibrary.GetAllTemplates();
                VectorLibraryListBox.ItemsSource = templates;
                foreach (var c in VectorLibrary.GetCategories())
                    CategoryComboBox.Items.Add(new ComboBoxItem { Content = c });
            }
            catch (Exception ex) { StatusText.Text = $"Library: {ex.Message}"; }
        }

        private void InitializeFeatureTypeComboBox()
        {
            foreach (FeatureType t in Enum.GetValues(typeof(FeatureType)))
                FeatureTypeComboBox.Items.Add(t);
            FeatureTypeComboBox.SelectedIndex = 0;
        }

        private void PopulateFlankingComboBoxes()
        {
            if (_allEnzymes == null) return;
            var common = new[] { "EcoRI", "BamHI", "HindIII", "XhoI", "NheI", "NcoI", "NotI", "XbaI", "SalI", "KpnI", "NdeI", "BglII" };
            var sorted = common.Select(n => _allEnzymes.FirstOrDefault(e => e.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                .Where(e => e != null).Concat(_allEnzymes.Where(e => !common.Contains(e.Name))).ToList();
            var items5 = new List<object> { "(None)" }; items5.AddRange(sorted.Cast<object>());
            var items3 = new List<object> { "(None)" }; items3.AddRange(sorted.Cast<object>());
            Flank5ComboBox.ItemsSource = items5; Flank3ComboBox.ItemsSource = items3;
            Flank5ComboBox.SelectedIndex = 0; Flank3ComboBox.SelectedIndex = 0;
        }

        // Undo
        private void SaveUndoState()
        {
            var snap = _constructs.Select(c => c.Clone()).ToList();
            for (int i = 0; i < snap.Count; i++) snap[i].Name = _constructs[i].Name;
            _undoStack.Push(snap);
            if (_undoStack.Count > 20) { var l = _undoStack.ToList(); l.RemoveAt(l.Count - 1); _undoStack = new Stack<List<DnaConstruct>>(l.AsEnumerable().Reverse()); }
        }
        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            if (_undoStack.Count == 0) { StatusText.Text = "Nothing to undo."; return; }
            var prev = _undoStack.Pop(); _constructs.Clear();
            foreach (var c in prev) _constructs.Add(c);
            if (_constructs.Count > 0) ConstructListBox.SelectedIndex = 0;
            StatusText.Text = "Undo completed.";
        }

        private void AutoAnnotateConstruct(DnaConstruct c)
        {
            try { FeatureDetector.AutoAnnotate(c, _allEnzymes, _cutter); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AutoAnnotate: {ex.Message}"); }
        }

        // ===============================================================
        //  Vector Library
        // ===============================================================
        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VectorLibraryListBox == null) return;
            if (CategoryComboBox.SelectedIndex == 0) VectorLibraryListBox.ItemsSource = VectorLibrary.GetAllTemplates();
            else if (CategoryComboBox.SelectedItem is ComboBoxItem item) VectorLibraryListBox.ItemsSource = VectorLibrary.GetByCategory(item.Content.ToString());
        }

        private void LoadVector_Click(object sender, RoutedEventArgs e)
        {
            if (VectorLibraryListBox.SelectedItem is VectorTemplate t)
            {
                SaveUndoState(); var c = t.ToConstruct(); AutoAnnotateConstruct(c);
                _constructs.Add(c); ConstructListBox.SelectedItem = c;
                StatusText.Text = $"Loaded {t.Name} ({t.Size} bp, {c.Features.Count} features)";
            }
            else StatusText.Text = "Select a vector first.";
        }

        private void ImportGenBank_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Title = "Import GenBank", Filter = "GenBank (*.gb;*.gbk)|*.gb;*.gbk|All|*.*", Multiselect = true };
            if (dlg.ShowDialog() != true) return;
            SaveUndoState(); int ok = 0;
            foreach (var path in dlg.FileNames)
            {
                try
                {
                    var t = GenBankParser.ParseFile(path);
                    if (t?.Sequence?.Length > 0) { var c = t.ToConstruct(); AutoAnnotateConstruct(c); _constructs.Add(c); ConstructListBox.SelectedItem = c; ok++; }
                }
                catch (Exception ex) { MessageBox.Show($"Failed: {System.IO.Path.GetFileName(path)}\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning); }
            }
            if (ok > 0) StatusText.Text = $"Imported {ok} vector(s).";
        }

        // ===============================================================
        //  Custom Sequence
        // ===============================================================
        private void AddCustomSequence_Click(object sender, RoutedEventArgs e)
        {
            string raw = CustomSequenceTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(raw)) { StatusText.Text = "Enter a DNA sequence."; return; }
            string seq = CleanSequence(raw);
            if (seq.Length == 0) { StatusText.Text = "No valid DNA bases."; return; }
            string name = CustomNameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) name = $"Seq_{_customSequenceCounter++}";
            bool circ = IsCircularCheckBox.IsChecked == true;
            RestrictionEnzyme re5 = Flank5ComboBox.SelectedItem as RestrictionEnzyme;
            RestrictionEnzyme re3 = Flank3ComboBox.SelectedItem as RestrictionEnzyme;
            string pre = re5?.RecognitionSequence ?? "", suf = re3?.RecognitionSequence ?? "";
            string finalSeq = pre + seq + suf;
            SaveUndoState();
            var c = new DnaConstruct(name, finalSeq, circ);
            int pos = 0;
            if (pre.Length > 0) { c.Features.Add(new SequenceFeature { Name = $"{re5.Name} (5')", Type = FeatureType.Misc, Start = 0, End = pre.Length }); pos = pre.Length; }
            c.Features.Add(new SequenceFeature { Name = "Insert", Type = FeatureType.Gene, Start = pos, End = pos + seq.Length });
            if (suf.Length > 0) c.Features.Add(new SequenceFeature { Name = $"{re3.Name} (3')", Type = FeatureType.Misc, Start = pos + seq.Length, End = finalSeq.Length });
            if (re5 != null) c.End5 = CreateDnaEnd(re5, true);
            if (re3 != null) c.End3 = CreateDnaEnd(re3, false);
            _constructs.Add(c); ConstructListBox.SelectedItem = c;
            CustomSequenceTextBox.Clear(); CustomNameTextBox.Clear(); IsCircularCheckBox.IsChecked = false;
            StatusText.Text = $"Added {name} ({finalSeq.Length} bp)";
        }

        // ===============================================================
        //  Workspace
        // ===============================================================
        private void ConstructListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedConstruct = ConstructListBox.SelectedItem as DnaConstruct;
            if (_selectedConstruct != null)
            {
                VisualizationTitle.Text = _selectedConstruct.Name;
                VisualizationInfo.Text = $"({_selectedConstruct.LengthDisplay}, {_selectedConstruct.TopologyDisplay})";
                FeatureListBox.ItemsSource = _selectedConstruct.Features;
                CircularViewToggle.IsChecked = _selectedConstruct.IsCircular;
                _useCircularView = _selectedConstruct.IsCircular;
                UpdateCutSiteInfo(); DrawConstruct();
            }
            else
            {
                VisualizationTitle.Text = "Select a construct"; VisualizationInfo.Text = "";
                FeatureListBox.ItemsSource = null; VisualizationCanvas.Children.Clear();
                DigestButton.IsEnabled = false;
            }
        }

        private void CloneConstruct_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConstruct == null) return;
            SaveUndoState(); var cl = _selectedConstruct.Clone();
            _constructs.Add(cl); ConstructListBox.SelectedItem = cl;
        }

        private void DeleteConstruct_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConstruct == null) return;
            SaveUndoState(); string n = _selectedConstruct.Name;
            _constructs.Remove(_selectedConstruct); VisualizationCanvas.Children.Clear();
            StatusText.Text = $"Deleted {n}";
        }

        private void FeatureListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FeatureListBox.SelectedItem is SequenceFeature f) DrawConstruct(f);
        }

        private void AddFeature_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConstruct == null) return;
            string name = FeatureNameTextBox.Text?.Trim();
            if (string.IsNullOrEmpty(name)) return;
            if (!int.TryParse(FeatureStartTextBox.Text, out int s) || !int.TryParse(FeatureEndTextBox.Text, out int en)) return;
            SaveUndoState();
            _selectedConstruct.Features.Add(new SequenceFeature
            {
                Name = name,
                Type = (FeatureType)FeatureTypeComboBox.SelectedItem,
                Start = Math.Max(0, s - 1),
                End = Math.Min(_selectedConstruct.Length, en),
                IsReverse = FeatureReverseCheckBox.IsChecked == true
            });
            FeatureListBox.ItemsSource = null; FeatureListBox.ItemsSource = _selectedConstruct.Features;
            DrawConstruct(); FeatureNameTextBox.Clear(); FeatureStartTextBox.Clear(); FeatureEndTextBox.Clear();
        }

        private void CircularViewToggle_Click(object sender, RoutedEventArgs e) { _useCircularView = CircularViewToggle.IsChecked == true; DrawConstruct(); }

        // ===============================================================
        //  VISUALIZATION ROUTER
        // ===============================================================
        private void DrawConstruct(SequenceFeature hl = null)
        {
            VisualizationCanvas.Children.Clear();
            if (_selectedConstruct == null || _selectedConstruct.Length == 0) return;
            if (_useCircularView && _selectedConstruct.IsCircular) DrawCircularMap(hl);
            else DrawLinearMap(hl);
        }

        // ===============================================================
        //  LINEAR MAP (same as before, with selection rect support)
        // ===============================================================
        private void DrawLinearMap(SequenceFeature hl)
        {
            int seqLen = _selectedConstruct.Length;
            double cW = Math.Max(700, VisualizationCanvas.ActualWidth); if (cW < 200) cW = 900;
            double drawW = (cW - LEFT_MARGIN - RIGHT_MARGIN) * _zoom;
            VisualizationCanvas.Width = drawW + LEFT_MARGIN + RIGHT_MARGIN;
            VisualizationCanvas.Height = Math.Max(350, 300 * _zoom);
            double trackY = VisualizationCanvas.Height * 0.40;

            _lastTrackY = trackY; _lastDrawW = drawW; _lastSeqLen = seqLen;

            // Title
            Add(TB(_selectedConstruct.Name, FONT_TITLE, Brushes.Black, true), LEFT_MARGIN, 10);
            Add(TB($"{_selectedConstruct.LengthDisplay}  {_selectedConstruct.TopologyDisplay}", FONT_INFO, Brushes.Gray), LEFT_MARGIN, 34);

            // Backbone
            VisualizationCanvas.Children.Add(new Line { X1 = LEFT_MARGIN, Y1 = trackY, X2 = LEFT_MARGIN + drawW, Y2 = trackY, Stroke = Brushes.Black, StrokeThickness = 3 });
            if (!_selectedConstruct.IsCircular)
            {
                Add(TB("5'", FONT_END, Brushes.Black, true), LEFT_MARGIN - 22, trackY - 8);
                VisualizationCanvas.Children.Add(new Line { X1 = LEFT_MARGIN, Y1 = trackY - 10, X2 = LEFT_MARGIN, Y2 = trackY + 10, Stroke = Brushes.Black, StrokeThickness = 2 });
                VisualizationCanvas.Children.Add(new Polygon { Points = new PointCollection { new Point(LEFT_MARGIN + drawW, trackY - 7), new Point(LEFT_MARGIN + drawW + 12, trackY), new Point(LEFT_MARGIN + drawW, trackY + 7) }, Fill = Brushes.Black });
                Add(TB("3'", FONT_END, Brushes.Black, true), LEFT_MARGIN + drawW + 14, trackY - 8);
            }

            // Ruler
            int tick = seqLen < 500 ? 50 : seqLen < 2000 ? 200 : seqLen < 10000 ? 1000 : 5000;
            double rulerY = trackY + 18;
            for (int bp = 0; bp <= seqLen; bp += tick)
            {
                double x = LEFT_MARGIN + (bp / (double)seqLen) * drawW;
                VisualizationCanvas.Children.Add(new Line { X1 = x, Y1 = rulerY, X2 = x, Y2 = rulerY + 6, Stroke = Brushes.Gray, StrokeThickness = 1 });
                Add(TB(bp == 0 ? "1" : bp.ToString(), FONT_RULER, Brushes.Gray), x - 12, rulerY + 7);
            }

            // Features
            var layers = CalcLayers(_selectedConstruct.Features.ToList(), seqLen, drawW);
            foreach (var kv in layers) DrawLinearFeature(kv.Key, kv.Value, trackY, seqLen, drawW, kv.Key == hl);

            // Cut sites
            if (_currentCutSites?.Count > 0)
                foreach (var site in _currentCutSites)
                {
                    double x = LEFT_MARGIN + (site.Position / (double)seqLen) * drawW;
                    VisualizationCanvas.Children.Add(new Line { X1 = x, Y1 = trackY - 30, X2 = x, Y2 = trackY + 30, Stroke = Brushes.Red, StrokeThickness = 1.8, StrokeDashArray = new DoubleCollection { 5, 2 }, Opacity = 0.85 });
                    Add(TB("✂", FONT_CUT, Brushes.Red, true), x - 7, trackY - 30 - 18);
                    Add(TB(site.Position.ToString(), FONT_CUT, Brushes.Red), x - 14, trackY + 30 + 3);
                }

            // End labels
            if (!_selectedConstruct.IsCircular)
            {
                double ly = trackY + 40;
                if (_selectedConstruct.End5 != null) Add(TB($"5': {_selectedConstruct.End5}", FONT_END, Brushes.DarkBlue), LEFT_MARGIN, ly);
                if (_selectedConstruct.End3 != null) Add(TB($"3': {_selectedConstruct.End3}", FONT_END, Brushes.DarkBlue), LEFT_MARGIN + drawW * 0.55, ly);
            }

            // Selection highlight rectangle (drawn on top)
            if (_selectStartBp >= 0 && _selectEndBp >= 0 && _selectEndBp > _selectStartBp)
            {
                double sx = LEFT_MARGIN + (_selectStartBp / (double)seqLen) * drawW;
                double ex = LEFT_MARGIN + (_selectEndBp / (double)seqLen) * drawW;
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = Math.Max(1, ex - sx),
                    Height = VisualizationCanvas.Height * 0.6,
                    Fill = new SolidColorBrush(Color.FromArgb(30, 25, 118, 210)),
                    Stroke = new SolidColorBrush(Color.FromArgb(120, 25, 118, 210)),
                    StrokeThickness = 1,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(rect, sx); Canvas.SetTop(rect, trackY - VisualizationCanvas.Height * 0.3);
                VisualizationCanvas.Children.Add(rect);
            }
        }

        private void DrawLinearFeature(SequenceFeature f, int layer, double trackY, int seqLen, double drawW, bool isHL)
        {
            double x1 = LEFT_MARGIN + (f.Start / (double)seqLen) * drawW;
            double x2 = LEFT_MARGIN + (f.End / (double)seqLen) * drawW;
            double fw = Math.Max(x2 - x1, 12);
            bool above = layer % 2 == 0;
            double yOff = (layer / 2 + 1) * (FEATURE_HEIGHT + FEATURE_GAP);
            double fy = above ? trackY - yOff - FEATURE_HEIGHT : trackY + 24 + (layer / 2) * (FEATURE_HEIGHT + FEATURE_GAP);
            double aw = Math.Min(12, fw * 0.25); bool right = !f.IsReverse;
            Color bc = f.Color;
            Color light = Color.FromArgb(80, bc.R, bc.G, bc.B);
            Color dark = DarkenColor(bc, 0.45);

            var arrow = new Polygon();
            if (right) arrow.Points = new PointCollection { new Point(x1, fy), new Point(x1 + fw - aw, fy), new Point(x1 + fw, fy + FEATURE_HEIGHT / 2), new Point(x1 + fw - aw, fy + FEATURE_HEIGHT), new Point(x1, fy + FEATURE_HEIGHT) };
            else arrow.Points = new PointCollection { new Point(x1, fy + FEATURE_HEIGHT / 2), new Point(x1 + aw, fy), new Point(x1 + fw, fy), new Point(x1 + fw, fy + FEATURE_HEIGHT), new Point(x1 + aw, fy + FEATURE_HEIGHT) };

            arrow.Fill = new SolidColorBrush(isHL ? Color.FromArgb(140, bc.R, bc.G, bc.B) : light);
            arrow.Stroke = new SolidColorBrush(isHL ? Colors.Black : bc);
            arrow.StrokeThickness = isHL ? 2.5 : 1.5; arrow.Cursor = Cursors.Hand;
            arrow.MouseEnter += (s, ev) => { arrow.Fill = new SolidColorBrush(Color.FromArgb(140, bc.R, bc.G, bc.B)); arrow.StrokeThickness = 2.5; StatusText.Text = $"{f.Name} | {f.Type} | {f.PositionDisplay} | {f.Length} bp"; };
            arrow.MouseLeave += (s, ev) => { if (!isHL) { arrow.Fill = new SolidColorBrush(light); arrow.StrokeThickness = 1.5; } StatusText.Text = "Ready"; };

            // Feature drag
            arrow.MouseLeftButtonDown += (s, me) => { _isDragging = true; _dragFeature = f; _dragStartX = me.GetPosition(VisualizationCanvas).X; _dragOrigStart = f.Start; _dragOrigEnd = f.End; arrow.CaptureMouse(); me.Handled = true; };
            arrow.MouseMove += (s, me) => { if (!_isDragging || _dragFeature != f) return; double dx = me.GetPosition(VisualizationCanvas).X - _dragStartX; int dbp = (int)(dx / drawW * seqLen); int ns = _dragOrigStart + dbp, ne = _dragOrigEnd + dbp; if (ns >= 0 && ne <= seqLen) { f.Start = ns; f.End = ne; DrawConstruct(f); } };
            arrow.MouseLeftButtonUp += (s, me) => { if (_isDragging) { _isDragging = false; _dragFeature = null; arrow.ReleaseMouseCapture(); } };

            VisualizationCanvas.Children.Add(arrow);

            // Connector
            double cx = (x1 + x1 + fw) / 2;
            VisualizationCanvas.Children.Add(new Line { X1 = cx, Y1 = above ? fy + FEATURE_HEIGHT : fy, X2 = cx, Y2 = trackY, Stroke = new SolidColorBrush(Color.FromArgb(100, bc.R, bc.G, bc.B)), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 } });

            // Label
            if (fw > 30) { var lb = TB(f.Name, FONT_FEATURE, new SolidColorBrush(dark), true); lb.IsHitTestVisible = false; Add(lb, x1 + 5, fy + (FEATURE_HEIGHT - FONT_FEATURE - 2) / 2); }
        }

        // ===============================================================
        //  CIRCULAR MAP
        // ===============================================================
        private void DrawCircularMap(SequenceFeature hl)
        {
            int seqLen = _selectedConstruct.Length;
            double size = Math.Min(VisualizationCanvas.ActualWidth, VisualizationCanvas.ActualHeight);
            if (size < 200) size = 500; size *= _zoom;
            double radius = size * 0.30, cx = size / 2 + 20, cy = size / 2 + 30;
            VisualizationCanvas.Width = size + 40; VisualizationCanvas.Height = size + 60;
            _lastSeqLen = seqLen;

            Add(TB(_selectedConstruct.Name, FONT_TITLE, Brushes.Black, true), 20, 8);
            Add(TB($"{_selectedConstruct.LengthDisplay}  Circular", FONT_INFO, Brushes.Gray), 20, 32);

            var circle = new Ellipse { Width = radius * 2, Height = radius * 2, Stroke = Brushes.Black, StrokeThickness = 3, Fill = Brushes.Transparent };
            Canvas.SetLeft(circle, cx - radius); Canvas.SetTop(circle, cy - radius);
            VisualizationCanvas.Children.Add(circle);
            Add(TB($"{seqLen} bp", FONT_INFO, Brushes.Gray), cx - 20, cy - 8);

            foreach (var f in _selectedConstruct.Features)
                DrawCircularFeature(f, cx, cy, radius, seqLen, f == hl);

            if (_currentCutSites?.Count > 0)
                foreach (var site in _currentCutSites)
                {
                    double angle = (site.Position / (double)seqLen) * 360 - 90;
                    double rad = angle * Math.PI / 180;
                    VisualizationCanvas.Children.Add(new Line { X1 = cx + (radius - 15) * Math.Cos(rad), Y1 = cy + (radius - 15) * Math.Sin(rad), X2 = cx + (radius + 15) * Math.Cos(rad), Y2 = cy + (radius + 15) * Math.Sin(rad), Stroke = Brushes.Red, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 2 } });
                    Add(TB($"✂{site.Position}", 9, Brushes.Red, true), cx + (radius + 20) * Math.Cos(rad) - 15, cy + (radius + 20) * Math.Sin(rad) - 8);
                }
        }

        private void DrawCircularFeature(SequenceFeature f, double cx, double cy, double radius, int seqLen, bool isHL)
        {
            double sa = (f.Start / (double)seqLen) * 360 - 90;
            double sweep = ((f.End - f.Start) / (double)seqLen) * 360;
            if (sweep < 1) sweep = 1;
            Color bc = f.Color; Color light = Color.FromArgb(100, bc.R, bc.G, bc.B);
            double arcR = radius + 22, thick = 18;
            double sr = sa * Math.PI / 180, er = (sa + sweep) * Math.PI / 180;
            double oR = arcR + thick / 2, iR = arcR - thick / 2;
            var fig = new PathFigure { StartPoint = new Point(cx + oR * Math.Cos(sr), cy + oR * Math.Sin(sr)), IsClosed = true };
            fig.Segments.Add(new ArcSegment(new Point(cx + oR * Math.Cos(er), cy + oR * Math.Sin(er)), new Size(oR, oR), 0, sweep > 180, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(new Point(cx + iR * Math.Cos(er), cy + iR * Math.Sin(er)), true));
            fig.Segments.Add(new ArcSegment(new Point(cx + iR * Math.Cos(sr), cy + iR * Math.Sin(sr)), new Size(iR, iR), 0, sweep > 180, SweepDirection.Counterclockwise, true));
            var geom = new PathGeometry(); geom.Figures.Add(fig);
            var path = new Path { Data = geom, Fill = new SolidColorBrush(isHL ? Color.FromArgb(160, bc.R, bc.G, bc.B) : light), Stroke = new SolidColorBrush(isHL ? Colors.Black : bc), StrokeThickness = isHL ? 2.5 : 1.2, Cursor = Cursors.Hand };
            path.MouseEnter += (s, ev) => { path.Fill = new SolidColorBrush(Color.FromArgb(160, bc.R, bc.G, bc.B)); path.StrokeThickness = 2.5; StatusText.Text = $"{f.Name} | {f.Type} | {f.PositionDisplay} | {f.Length} bp"; };
            path.MouseLeave += (s, ev) => { if (!isHL) { path.Fill = new SolidColorBrush(light); path.StrokeThickness = 1.2; } StatusText.Text = "Ready"; };
            VisualizationCanvas.Children.Add(path);
            if (sweep > 15) { double mid = (sa + sweep / 2) * Math.PI / 180; double lr = arcR + thick / 2 + 14; var lb = TB(f.Name, 11, new SolidColorBrush(DarkenColor(bc, 0.4)), true); lb.IsHitTestVisible = false; Add(lb, cx + lr * Math.Cos(mid) - f.Name.Length * 3, cy + lr * Math.Sin(mid) - 7); }
        }

        // ===============================================================
        //  CANVAS INTERACTION — DRAG SELECT
        // ===============================================================
        private int CanvasXtoBp(double x)
        {
            if (_lastDrawW <= 0 || _lastSeqLen <= 0) return -1;
            double rel = x - LEFT_MARGIN;
            if (rel < 0 || rel > _lastDrawW) return -1;
            return Math.Max(0, Math.Min((int)(rel / _lastDrawW * _lastSeqLen), _lastSeqLen));
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging) return; // feature drag takes priority
            if (_selectedConstruct == null || _useCircularView) return;
            var pos = e.GetPosition(VisualizationCanvas);
            int bp = CanvasXtoBp(pos.X);
            if (bp < 0) return;
            _isSelecting = true; _selectStartX = pos.X; _selectStartBp = bp; _selectEndBp = bp;
            VisualizationCanvas.CaptureMouse();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_selectedConstruct == null) return;
            var pos = e.GetPosition(VisualizationCanvas);
            int bp = CanvasXtoBp(pos.X);
            if (bp >= 0) PositionText.Text = $"Position: {bp + 1}";

            if (_isSelecting && bp >= 0)
            {
                _selectEndBp = bp;
                int s = Math.Min(_selectStartBp, _selectEndBp);
                int en = Math.Max(_selectStartBp, _selectEndBp);
                StatusText.Text = $"Selecting: {s + 1} — {en} ({en - s} bp)";
                DrawConstruct(); // redraws with selection highlight
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging) { _isDragging = false; _dragFeature = null; return; }
            if (_isSelecting)
            {
                _isSelecting = false;
                VisualizationCanvas.ReleaseMouseCapture();

                int s = Math.Min(_selectStartBp, _selectEndBp);
                int en = Math.Max(_selectStartBp, _selectEndBp);

                if (en - s >= 2) ShowSequenceViewer(s, en);
                else { _selectStartBp = -1; _selectEndBp = -1; SeqViewerBorder.Visibility = Visibility.Collapsed; }
            }
        }

        private void Canvas_MouseLeave(object sender, MouseEventArgs e) { PositionText.Text = ""; }

        // ===============================================================
        //  SEQUENCE VIEWER
        // ===============================================================
        private void ShowSequenceViewer(int start, int end)
        {
            if (_selectedConstruct == null) return;
            start = Math.Max(0, start);
            end = Math.Min(_selectedConstruct.Length, end);
            if (end - start > 500) end = start + 500; // cap at 500bp for display

            string subseq = _selectedConstruct.Sequence.Substring(start, end - start).ToUpper();
            SeqViewerTitle.Text = $"Sequence: {start + 1} — {end} ({end - start} bp)";

            // Build cut site annotation line
            var cutLine = new char[subseq.Length];
            for (int i = 0; i < cutLine.Length; i++) cutLine[i] = ' ';
            var cutNames = new List<string>();

            if (_allEnzymes != null)
            {
                // Find RE sites within this subseq region in the full sequence
                var common6 = _allEnzymes.Where(en => en.RecognitionSequence.Length >= 6 && !en.RecognitionSequence.Contains("N")).Take(200);
                foreach (var enz in common6)
                {
                    try
                    {
                        var sites = _cutter.FindCutSites(_selectedConstruct.Sequence, enz, _selectedConstruct.IsCircular);
                        if (sites == null) continue;
                        foreach (var site in sites)
                        {
                            int relPos = site.Position - start;
                            if (relPos >= 0 && relPos < subseq.Length)
                            {
                                // Mark cut position with ▼
                                cutLine[relPos] = '▼';
                                cutNames.Add($"{enz.Name}@{site.Position}");
                            }
                        }
                    }
                    catch { }
                }
            }

            string cutStr = new string(cutLine);
            // Only show if there are cuts
            SeqCutSiteLine.Text = cutStr.Trim().Length > 0 ? cutStr : "";

            // Format sequence in blocks of 10
            var seqFormatted = new System.Text.StringBuilder();
            var compFormatted = new System.Text.StringBuilder();
            for (int i = 0; i < subseq.Length; i++)
            {
                seqFormatted.Append(subseq[i]);
                compFormatted.Append(Complement(subseq[i]));
                if ((i + 1) % 10 == 0 && i < subseq.Length - 1)
                {
                    seqFormatted.Append(' ');
                    compFormatted.Append(' ');
                }
            }

            SeqViewerText.Text = $"5' {seqFormatted} 3'";
            SeqComplementText.Text = $"3' {compFormatted} 5'";

            if (cutNames.Count > 0)
                SeqViewerTitle.Text += $"  |  RE: {string.Join(", ", cutNames.Take(10))}";

            SeqViewerBorder.Visibility = Visibility.Visible;
            StatusText.Text = $"Selected {end - start} bp ({start + 1}–{end})";
        }

        private void CloseSeqViewer_Click(object sender, RoutedEventArgs e)
        {
            SeqViewerBorder.Visibility = Visibility.Collapsed;
            _selectStartBp = -1; _selectEndBp = -1;
            DrawConstruct();
        }

        private static char Complement(char c)
        {
            switch (c) { case 'A': return 'T'; case 'T': return 'A'; case 'G': return 'C'; case 'C': return 'G'; default: return 'N'; }
        }

        // ===============================================================
        //  RE DIGEST
        // ===============================================================
        private void EnzymeSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string s = EnzymeSearchBox.Text?.Trim();
            EnzymeListBox.ItemsSource = string.IsNullOrEmpty(s) ? _allEnzymes :
                _allEnzymes?.Where(en => en.Name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    en.RecognitionSequence.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        private void EnzymeListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        { _selectedEnzyme = EnzymeListBox.SelectedItem as RestrictionEnzyme; UpdateCutSiteInfo(); }

        private void UpdateCutSiteInfo()
        {
            if (_selectedEnzyme == null || _selectedConstruct == null)
            { EnzymeInfoBorder.Visibility = Visibility.Collapsed; DigestButton.IsEnabled = false; _currentCutSites = null; return; }
            _currentCutSites = _cutter.FindCutSites(_selectedConstruct.Sequence, _selectedEnzyme, _selectedConstruct.IsCircular);
            EnzymeInfoBorder.Visibility = Visibility.Visible;
            EnzymeInfoText.Text = $"{_selectedEnzyme.Name}: {_selectedEnzyme.RecognitionSequence} ({_selectedEnzyme.OverhangType})";
            CutSiteCountText.Text = _currentCutSites.Count == 0 ? "No cut sites" : $"{_currentCutSites.Count} site(s): {string.Join(", ", _currentCutSites.Select(c => c.Position))}";
            DigestButton.IsEnabled = _currentCutSites.Count > 0;
            DrawConstruct();
        }

        private void Digest_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConstruct == null || _selectedEnzyme == null) return;
            SaveUndoState();
            var frags = _cutter.DigestSequence(_selectedConstruct.Sequence, _selectedEnzyme, _selectedConstruct.IsCircular);
            if (frags.Count == 0) return;
            int idx = 1; var nList = new List<DnaConstruct>();
            foreach (var f in frags)
            {
                var c = new DnaConstruct { Name = $"{_selectedConstruct.Name}_{_selectedEnzyme.Name}_f{idx}", Sequence = f.Sequence, IsCircular = false, SourceName = _selectedConstruct.Name, End5 = CreateDnaEnd(_selectedEnzyme, true), End3 = CreateDnaEnd(_selectedEnzyme, false) };
                foreach (var feat in _selectedConstruct.Features)
                    if (feat.Start >= f.StartPosition && feat.End <= f.StartPosition + f.Size)
                        c.Features.Add(new SequenceFeature { Name = feat.Name, Type = feat.Type, Start = feat.Start - f.StartPosition, End = feat.End - f.StartPosition, IsReverse = feat.IsReverse, Color = feat.Color });
                nList.Add(c); idx++;
            }
            foreach (var c in nList) _constructs.Add(c);
            DigestResultText.Text = $"→ {nList.Count} fragment(s): {string.Join(", ", nList.Select(c => $"{c.Length} bp"))}";
            if (nList.Count > 0) ConstructListBox.SelectedItem = nList[0];
        }

        private DnaEnd CreateDnaEnd(RestrictionEnzyme enz, bool is5)
        {
            int c5 = enz.CutPosition5, c3 = enz.CutPosition3;
            if (c5 == c3) return new DnaEnd { Direction = OverhangDirection.None, EnzymeName = enz.Name };
            if (c5 < c3) return new DnaEnd { OverhangSequence = enz.RecognitionSequence.Substring(c5, c3 - c5), Direction = OverhangDirection.FivePrime, EnzymeName = enz.Name };
            return new DnaEnd { OverhangSequence = enz.RecognitionSequence.Substring(c3, c5 - c3), Direction = OverhangDirection.ThreePrime, EnzymeName = enz.Name };
        }

        // ===============================================================
        //  CLONING WORKFLOW
        // ===============================================================
        private void UpdateCloningComboBoxes()
        {
            if (CloningVectorComboBox == null || CloningInsertComboBox == null) return;
            var list = _constructs.ToList();
            CloningVectorComboBox.ItemsSource = list;
            CloningInsertComboBox.ItemsSource = list;
        }

        private void CloningSelectionChanged(object sender, RoutedEventArgs e)
        { UpdateCloningREList(); }

        private void CloningSelectionChanged(object sender, SelectionChangedEventArgs e)
        { UpdateCloningREList(); }

        private void UpdateCloningREList()
        {
            if (CloningButton == null || CloningREListBox == null) return;
            var vector = CloningVectorComboBox.SelectedItem as DnaConstruct;
            var insert = CloningInsertComboBox.SelectedItem as DnaConstruct;
            CloningButton.IsEnabled = false;
            CloningInfoText.Text = "";
            CloningREListBox.ItemsSource = null;

            if (vector == null || insert == null || vector == insert) return;

            // Find shared unique single-cutters between vector and insert
            // A good cloning site: cuts vector 1x AND does NOT cut insert
            var vecSingles = FeatureDetector.FindUniqueSingleCutters(vector.Sequence, _allEnzymes, _cutter, vector.IsCircular);
            var insertSites = new HashSet<string>();
            foreach (var enz in _allEnzymes.Where(e => e.RecognitionSequence.Length >= 6 && !e.RecognitionSequence.Contains("N")))
            {
                try
                {
                    var sites = _cutter.FindCutSites(insert.Sequence, enz, insert.IsCircular);
                    if (sites?.Count > 0) insertSites.Add(enz.Name);
                }
                catch { }
            }

            // Best candidates: cut vector 1x, don't cut insert
            var candidates = vecSingles
                .Where(v => !insertSites.Contains(v.name))
                .OrderBy(v => v.pos)
                .ToList();

            // Check which are in MCS region
            var mcsFeat = vector.Features.FirstOrDefault(f => f.Type == FeatureType.MCS);
            var inMCS = mcsFeat != null
                ? candidates.Where(c => c.pos >= mcsFeat.Start && c.pos <= mcsFeat.End).ToList()
                : new List<(string name, int pos, int len)>();

            var displayList = new List<string>();
            if (inMCS.Count > 0)
            {
                displayList.Add($"── In MCS ({mcsFeat.Start + 1}–{mcsFeat.End}) ──");
                foreach (var c in inMCS)
                    displayList.Add($"  ★ {c.name}  @{c.pos} (MCS)");
            }
            if (candidates.Count > inMCS.Count)
            {
                displayList.Add("── Other unique sites ──");
                foreach (var c in candidates.Where(c => !inMCS.Any(m => m.name == c.name)).Take(15))
                    displayList.Add($"  {c.name}  @{c.pos}");
            }

            if (displayList.Count == 0)
            {
                CloningInfoText.Text = "No compatible RE sites found. Both vector and insert are cut by all shared enzymes.";
                return;
            }

            CloningREListBox.ItemsSource = displayList;
            CloningInfoText.Text = $"{candidates.Count} enzyme(s) cut vector 1× and don't cut insert.";
            CloningButton.IsEnabled = true;
        }

        private void CloningREListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Just for info display, actual RE is parsed from the selected string
        }

        private void Cloning_Click(object sender, RoutedEventArgs e)
        {
            var vector = CloningVectorComboBox.SelectedItem as DnaConstruct;
            var insert = CloningInsertComboBox.SelectedItem as DnaConstruct;
            if (vector == null || insert == null) return;

            // Parse selected RE from list
            string selected = CloningREListBox.SelectedItem as string;
            string enzName = null;
            if (!string.IsNullOrEmpty(selected))
            {
                // Extract enzyme name from "  ★ EcoRI  @123 (MCS)" or "  EcoRI  @123"
                var parts = selected.Trim().TrimStart('★').Trim().Split(new[] { ' ', '@' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) enzName = parts[0];
            }

            // If no RE selected, use first MCS candidate
            if (enzName == null || enzName.StartsWith("──"))
            {
                var vecSingles = FeatureDetector.FindUniqueSingleCutters(vector.Sequence, _allEnzymes, _cutter, vector.IsCircular);
                var mcsFeat = vector.Features.FirstOrDefault(f => f.Type == FeatureType.MCS);
                var best = mcsFeat != null
                    ? vecSingles.FirstOrDefault(v => v.pos >= mcsFeat.Start && v.pos <= mcsFeat.End)
                    : vecSingles.FirstOrDefault();
                if (best.name == null) { CloningResultText.Text = "No suitable RE site found."; return; }
                enzName = best.name;
            }

            var enzyme = _allEnzymes.FirstOrDefault(en => en.Name == enzName);
            if (enzyme == null) { CloningResultText.Text = $"Enzyme {enzName} not found."; return; }

            // Cut vector at the site
            var cutSites = _cutter.FindCutSites(vector.Sequence, enzyme, vector.IsCircular);
            if (cutSites == null || cutSites.Count != 1) { CloningResultText.Text = "Vector must have exactly 1 cut site."; return; }

            int cutPos = cutSites[0].Position;
            bool reverse = ReverseRadio.IsChecked == true;
            string insertSeq = reverse ? ReverseComplement(insert.Sequence) : insert.Sequence;

            // Insert at cut position
            SaveUndoState();
            string newSeq = vector.Sequence.Substring(0, cutPos) + insertSeq + vector.Sequence.Substring(cutPos);
            var result = new DnaConstruct
            {
                Name = $"{vector.Name}+{insert.Name}",
                Sequence = newSeq,
                IsCircular = vector.IsCircular,
                SourceName = $"{vector.Name} + {insert.Name} ({enzName}, {(reverse ? "reverse" : "forward")})"
            };

            // Transfer vector features (adjust positions after cut site)
            foreach (var f in vector.Features)
            {
                int ns = f.Start, ne = f.End;
                if (f.Start >= cutPos) { ns += insertSeq.Length; ne += insertSeq.Length; }
                result.Features.Add(new SequenceFeature { Name = f.Name, Type = f.Type, Start = ns, End = ne, IsReverse = f.IsReverse, Color = f.Color });
            }

            // Add insert as feature
            result.Features.Add(new SequenceFeature
            {
                Name = $"{insert.Name} insert{(reverse ? " (rev)" : "")}",
                Type = FeatureType.Gene,
                Start = cutPos,
                End = cutPos + insertSeq.Length,
                IsReverse = reverse
            });

            _constructs.Add(result); ConstructListBox.SelectedItem = result;
            CloningResultText.Text = $"✅ Cloned {insert.Name} into {vector.Name} at {enzName} ({cutPos}) {(reverse ? "reverse" : "forward")} → {result.Length} bp";
            StatusText.Text = CloningResultText.Text;
        }

        private static string ReverseComplement(string seq)
        {
            var sb = new System.Text.StringBuilder(seq.Length);
            for (int i = seq.Length - 1; i >= 0; i--)
                sb.Append(seq[i] == 'A' ? 'T' : seq[i] == 'T' ? 'A' : seq[i] == 'G' ? 'C' : seq[i] == 'C' ? 'G' : 'N');
            return sb.ToString();
        }

        // ===============================================================
        //  Zoom
        // ===============================================================
        private void ZoomIn_Click(object sender, RoutedEventArgs e) { _zoom = Math.Min(_zoom * 1.3, 5.0); DrawConstruct(); }
        private void ZoomOut_Click(object sender, RoutedEventArgs e) { _zoom = Math.Max(_zoom / 1.3, 0.3); DrawConstruct(); }
        private void ResetView_Click(object sender, RoutedEventArgs e) { _zoom = 1.0; DrawConstruct(); }

        // ===============================================================
        //  Utility
        // ===============================================================
        private void CopySequence_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConstruct?.Sequence != null) { Clipboard.SetText(_selectedConstruct.Sequence); StatusText.Text = $"Copied {_selectedConstruct.Length} bp."; }
        }

        private Dictionary<SequenceFeature, int> CalcLayers(List<SequenceFeature> features, int seqLen, double drawW)
        {
            var layers = new Dictionary<SequenceFeature, int>();
            var ends = new List<double>();
            foreach (var f in features.OrderBy(f => f.Start))
            {
                double x1 = (f.Start / (double)seqLen) * drawW, x2 = (f.End / (double)seqLen) * drawW;
                int layer = -1;
                for (int i = 0; i < ends.Count; i++) if (x1 >= ends[i] + 8) { layer = i; ends[i] = x2; break; }
                if (layer == -1) { layer = ends.Count; ends.Add(x2); }
                layers[f] = layer;
            }
            return layers;
        }

        private static string CleanSequence(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.ToUpper()) if ("ATGCRYKMSWBDHVN".Contains(c)) sb.Append(c);
            return sb.ToString();
        }

        private TextBlock TB(string text, double size, Brush fg, bool bold = false)
            => new TextBlock { Text = text, FontSize = size, Foreground = fg, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, FontFamily = new FontFamily("Segoe UI") };

        private void Add(UIElement el, double left, double top)
        { Canvas.SetLeft(el, left); Canvas.SetTop(el, top); VisualizationCanvas.Children.Add(el); }

        private static Color DarkenColor(Color c, double f)
            => Color.FromRgb((byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));
    }
}