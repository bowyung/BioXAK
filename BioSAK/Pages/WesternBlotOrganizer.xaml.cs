using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using IO = System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BioSAK.Services;

namespace BioSAK.Pages
{
    // ============================================================
    //  HELPERS
    // ============================================================

    static class NotifyHelper
    {
        public static void Fire(PropertyChangedEventHandler h, object sender, string prop)
            => h?.Invoke(sender, new PropertyChangedEventArgs(prop));
    }

    // ============================================================
    //  DATA MODELS
    // ============================================================

    public class BlotEntry : INotifyPropertyChanged
    {
        private string _name = "Protein";
        private string _prefix = "α";
        private BitmapImage _image;
        private int _index;

        public string FilePath { get; set; } = string.Empty;

        public string Name
        {
            get { return _name; }
            set { _name = value; Fire("Name"); Fire("DisplayLabel"); }
        }
        public string Prefix
        {
            get { return _prefix; }
            set { _prefix = value ?? string.Empty; Fire("Prefix"); Fire("DisplayLabel"); }
        }
        public BitmapImage Image
        {
            get { return _image; }
            set { _image = value; Fire("Image"); }
        }
        public int Index
        {
            get { return _index; }
            set { _index = value; Fire("Index"); Fire("IndexLabel"); }
        }

        public string DisplayLabel { get { return _prefix + _name; } }
        public string IndexLabel { get { return "#" + (_index + 1); } }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Fire(string p) { NotifyHelper.Fire(PropertyChanged, this, p); }
    }

    // ─────────────────────────────────────────────────────────────

    // 新增：上方群組標籤資料模型
    public class TopLabelEntry : INotifyPropertyChanged
    {
        private string _text = "Treatment";
        private int _startLane = 1;
        private int _endLane = 2;

        public string Text
        {
            get { return _text; }
            set { _text = value; Fire("Text"); }
        }
        public int StartLane
        {
            get { return _startLane; }
            set { _startLane = value; Fire("StartLane"); }
        }
        public int EndLane
        {
            get { return _endLane; }
            set { _endLane = value; Fire("EndLane"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Fire(string p) { NotifyHelper.Fire(PropertyChanged, this, p); }
    }

    // ─────────────────────────────────────────────────────────────

    public class ConditionCell : INotifyPropertyChanged
    {
        private string _value = "-";
        public string Value
        {
            get { return _value; }
            set { _value = value; Fire("Value"); }
        }

        public List<string> Options
        {
            get
            {
                return new List<string>
                {
                    "-", "+",
                    "0","1","2","5","10","25","50","100","200","500","1000",
                    "0.1 \u03BCM","1 \u03BCM","5 \u03BCM","10 \u03BCM","50 \u03BCM","100 \u03BCM",
                    "0 ng","1 ng","5 ng","10 ng","50 ng","100 ng",
                    "Vector","WT","KO","KD","OE","Mock","Control","Treated"
                };
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Fire(string p) { NotifyHelper.Fire(PropertyChanged, this, p); }
    }

    // ─────────────────────────────────────────────────────────────

    public class ConditionRow : INotifyPropertyChanged
    {
        private string _rowLabel = string.Empty;
        private int _patternCycle = 0;

        public string RowLabel
        {
            get { return _rowLabel; }
            set { _rowLabel = value; Fire("RowLabel"); }
        }

        public int PatternCycle
        {
            get { return _patternCycle; }
            private set { _patternCycle = value; Fire("PatternCycle"); Fire("PatternLabel"); }
        }

        /// <summary>Button label: shows current pattern abbreviation</summary>
        public string PatternLabel
        {
            get
            {
                if (_patternCycle == 0) return "-/+";
                return new string('-', _patternCycle) + new string('+', _patternCycle);
            }
        }

        public ObservableCollection<ConditionCell> Cells { get; private set; }
            = new ObservableCollection<ConditionCell>();

        /// <summary>
        /// Advance pattern cycle and apply to cells.
        /// Patterns: 1=-+  2=--++  3=---+++  ...  maxHalf=last  maxHalf+1=reset
        /// </summary>
        public void CyclePattern(int lanes)
        {
            int maxHalf = Math.Max(1, lanes / 2);
            int next = (_patternCycle + 1) % (maxHalf + 1);
            PatternCycle = next;

            if (_patternCycle == 0) return; // reset: leave values untouched

            int half = _patternCycle;
            int period = half * 2;
            for (int i = 0; i < Cells.Count; i++)
            {
                int pos = i % period;
                Cells[i].Value = pos < half ? "-" : "+";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Fire(string p) { NotifyHelper.Fire(PropertyChanged, this, p); }
    }

    // ─────────────────────────────────────────────────────────────

    public class MarkerEntry : INotifyPropertyChanged
    {
        private string _label = "50 kDa";
        private double _yFrac = 0.5;   // 0 = top of blot area, 1 = bottom

        public string Label
        {
            get { return _label; }
            set { _label = value; Fire("Label"); }
        }

        /// <summary>Y fraction within the total blot drawing area (0–1).</summary>
        public double YFraction
        {
            get { return _yFrac; }
            set { _yFrac = Math.Max(0, Math.Min(1, value)); Fire("YFraction"); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void Fire(string p) { NotifyHelper.Fire(PropertyChanged, this, p); }
    }

    // ============================================================
    //  VIEW MODEL
    // ============================================================

    public class WesternBlotViewModel
    {
        public ObservableCollection<BlotEntry> Blots { get; } = new ObservableCollection<BlotEntry>();
        public ObservableCollection<ConditionRow> ConditionRows { get; } = new ObservableCollection<ConditionRow>();
        public ObservableCollection<MarkerEntry> Markers { get; } = new ObservableCollection<MarkerEntry>();
        public ObservableCollection<TopLabelEntry> TopLabels { get; } = new ObservableCollection<TopLabelEntry>();

        /// <summary>Manual lane CENTER X fractions (0-1 of blotW). Click pos = symbol pos.</summary>
        public List<double> LaneXFractions { get; } = new List<double>();

        /// <summary>Left boundary fraction (0=blot left). null = not set.</summary>
        public double? LeftBoundFrac { get; set; } = null;
        /// <summary>Right boundary fraction (1=blot right). null = not set.</summary>
        public double? RightBoundFrac { get; set; } = null;
    }

    // ============================================================
    //  PAGE
    // ============================================================

    public partial class WesternBlotOrganizer : Page
    {
        private readonly WesternBlotViewModel _vm = new WesternBlotViewModel();

        // ── Layout ─────────────────────────────────────────────
        private double BlotW { get { return ParseD(TxtBlotWidth?.Text, 480); } }
        private double LabelW { get { return ParseD(TxtLabelWidth?.Text, 130); } }
        private double Gap { get { return ParseD(TxtRowSpacing?.Text, 6); } }
        private double FSize { get { return ParseD(TxtFontSize?.Text, 13); } }
        private int Cols { get { return Math.Max(1, ParseI(TxtColumnCount?.Text, 4)); } }

        /// <summary>1 mm border: 96dpi = ~3.78px; we use 2px (≈0.5mm visible).</summary>
        private const double BorderPx = 2.0;
        private const double Margin96 = 12.0;

        // ── Zoom ───────────────────────────────────────────────
        private double _zoom = 1.0;

        // ── Render-geometry cache (used by overlay handlers) ───
        private double _rBlotAreaX;      // figure coords (96dpi): left of blot image
        private double _rBlotAreaY;      // figure coords: top of first blot
        private double _rBlotAreaH;      // figure coords: total blot height
        private double _rBlotW;          // = BlotW at render time

        // ── Mode flags ─────────────────────────────────────────
        private bool _laneMode = false;
        private bool _markerMode = false;
        private bool _boundaryMode = false;
        private bool _boundNextLeft = true; // true=next bound click sets Left

        // ── Image Editor state ─────────────────────────────────
        private BitmapImage _editorSourceImage = null;
        private string _editorSourcePath = string.Empty;
        private BitmapSource _edRotatedImage = null;   // cached after rotation
        private double _edRotation = 0;
        private double _edCropL = 0, _edCropR = 1, _edCropT = 0, _edCropB = 1; // fractions of rotated image
        private byte _edLvMin = 0, _edLvMax = 255;
        private bool _edGrayscale = false;

        // ── Gene ID lookup ─────────────────────────────────────────
        private static readonly GeneIdService _geneIdService = new GeneIdService();
        private static bool _geneDbLoading = false;
        private string _lastLookedUpName = string.Empty;

        // ── Crop drag ──────────────────────────────────────────────
        private enum CropHandle { None, TL, TM, TR, ML, MR, BL, BM, BR, Move }
        private CropHandle _cropDrag = CropHandle.None;
        private Point _cropDragStart;
        private double _cropDragL, _cropDragR, _cropDragT, _cropDragB;
        private const double HANDLE_PX = 8.0;   // visual size
        private const double HANDLE_HIT = 12.0;  // hit area

        // ── Overlay temp-line ──────────────────────────────────
        private Line _tempLine;

        // ── Cached lane centers (96dpi figure space) for overlay ─
        private List<double> _rLaneCenters96 = new List<double>();

        private bool _suppressUpdate;

        // ──────────────────────────────────────────────────────
        public WesternBlotOrganizer()
        {
            InitializeComponent();
            DataContext = _vm;

            _vm.Blots.CollectionChanged += (s, e) => UpdatePreview();
            _vm.ConditionRows.CollectionChanged += (s, e) => UpdatePreview();
            _vm.Markers.CollectionChanged += (s, e) => { UpdatePreview(); RefreshOverlay(); };
            _vm.TopLabels.CollectionChanged += (s, e) => UpdatePreview();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            UpdatePreview();
        }

        // ============================================================
        //  BLOT LIST
        // ============================================================

        private void BtnAddBlot_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select blot images",
                Filter = "Image files (*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp)|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|All files (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog() != true) return;
            foreach (var path in dlg.FileNames) TryAddBlot(path);
            ReIndex(); UpdatePreview();
        }

        private void BtnRemoveBlot_Click(object sender, RoutedEventArgs e)
        {
            if (BlotListBox.SelectedItem is BlotEntry entry)
            { _vm.Blots.Remove(entry); ReIndex(); UpdatePreview(); }
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            int i = BlotListBox.SelectedIndex;
            if (i > 0) { _vm.Blots.Move(i, i - 1); BlotListBox.SelectedIndex = i - 1; ReIndex(); UpdatePreview(); }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            int i = BlotListBox.SelectedIndex;
            if (i >= 0 && i < _vm.Blots.Count - 1) { _vm.Blots.Move(i, i + 1); BlotListBox.SelectedIndex = i + 1; ReIndex(); UpdatePreview(); }
        }

        private void BlotListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _suppressUpdate = true;
            if (BlotListBox.SelectedItem is BlotEntry bSel)
            {
                _suppressUpdate = true;
                TxtBlotName.Text = bSel.Name;
                if (CboBlotPrefix != null)
                {
                    string p = bSel.Prefix ?? "α";
                    string match = p == "" ? "(none)" : p;
                    foreach (ComboBoxItem item in CboBlotPrefix.Items)
                        if ((string)item.Content == match) { CboBlotPrefix.SelectedItem = item; break; }
                }
                _suppressUpdate = false;
            }
            else { TxtBlotName.Text = string.Empty; }
            _suppressUpdate = false;
        }


        private void TxtBlotName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressUpdate) return;
            if (BlotListBox.SelectedItem is BlotEntry b) { b.Name = TxtBlotName.Text; UpdatePreview(); }
        }

        private void CboBlotPrefix_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressUpdate) return;
            if (CboBlotPrefix?.SelectedItem is ComboBoxItem item &&
                BlotListBox.SelectedItem is BlotEntry b)
            {
                b.Prefix = (string)item.Content == "(none)" ? "" : (string)item.Content;
                UpdatePreview();
            }
        }

        private void BlotListBox_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            foreach (var f in (string[])e.Data.GetData(DataFormats.FileDrop))
                if (IsImg(f)) TryAddBlot(f);
            ReIndex(); UpdatePreview();
        }

        private void TryAddBlot(string path)
        {
            try
            {
                _vm.Blots.Add(new BlotEntry
                {
                    FilePath = path,
                    Name = IO.Path.GetFileNameWithoutExtension(path),
                    Image = LoadBmp(path),
                    Index = _vm.Blots.Count
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Cannot load: " + IO.Path.GetFileName(path) + "\n" + ex.Message,
                                "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ReIndex() { for (int i = 0; i < _vm.Blots.Count; i++) _vm.Blots[i].Index = i; }
        private static bool IsImg(string p) { var x = IO.Path.GetExtension(p).ToLower(); return x == ".png" || x == ".jpg" || x == ".jpeg" || x == ".tif" || x == ".tiff" || x == ".bmp"; }

        // ============================================================
        //  TOP LABELS
        // ============================================================

        private void BtnAddTopLabel_Click(object sender, RoutedEventArgs e)
        {
            var tl = new TopLabelEntry { StartLane = 1, EndLane = Cols };
            tl.PropertyChanged += (s, ev) => UpdatePreview();
            _vm.TopLabels.Add(tl);
            UpdatePreview();
        }

        private void DeleteTopLabel_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is TopLabelEntry tl)
            { _vm.TopLabels.Remove(tl); UpdatePreview(); }
        }

        // ============================================================
        //  CONDITION ROWS
        // ============================================================

        private void TxtColumnCount_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressUpdate) return;
            int c = Math.Max(1, Math.Min(24, ParseI(TxtColumnCount?.Text, 4)));
            foreach (var row in _vm.ConditionRows) SyncCells(row, c);
            UpdatePreview();
        }

        private void BtnAddConditionRow_Click(object sender, RoutedEventArgs e)
        {
            var row = new ConditionRow();
            SyncCells(row, Cols);
            WireRow(row);
            _vm.ConditionRows.Add(row);
            UpdatePreview();
        }

        private void DeleteConditionRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ConditionRow row)
            { _vm.ConditionRows.Remove(row); UpdatePreview(); }
        }

        private void PatternToggle_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ConditionRow row)
            { row.CyclePattern(Cols); UpdatePreview(); }
        }

        private void SyncCells(ConditionRow row, int target)
        {
            while (row.Cells.Count < target)
            {
                var cell = new ConditionCell { Value = "-" };
                cell.PropertyChanged += (s, e2) => UpdatePreview();
                row.Cells.Add(cell);
            }
            while (row.Cells.Count > target) row.Cells.RemoveAt(row.Cells.Count - 1);
        }

        private void WireRow(ConditionRow row)
        {
            row.PropertyChanged += (s, e) => UpdatePreview();
            row.Cells.CollectionChanged += (s, e) => UpdatePreview();
            foreach (var c in row.Cells) c.PropertyChanged += (s, e) => UpdatePreview();
        }

        // ============================================================
        //  MARKERS / LABELS
        // ============================================================

        private void MarkerLabel_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_suppressUpdate) UpdatePreview();
        }

        private void DeleteMarker_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is MarkerEntry m)
            { _vm.Markers.Remove(m); UpdatePreview(); RefreshOverlay(); }
        }

        // ============================================================
        //  LANE MODE
        // ============================================================

        private void BtnLaneSelect_Checked(object sender, RoutedEventArgs e)
        {
            // Clear everything before starting fresh
            _vm.LaneXFractions.Clear();
            _vm.LeftBoundFrac = null;
            _vm.RightBoundFrac = null;
            _laneMode = true;
            _markerMode = false;
            _boundaryMode = false;
            if (BtnAddMarkerMode != null) BtnAddMarkerMode.IsChecked = false;
            if (BtnBoundaryMode != null) BtnBoundaryMode.IsChecked = false;
            PreviewGrid.IsHitTestVisible = true;
            PreviewGrid.Cursor = Cursors.Cross;
            UpdatePreview(); RefreshOverlay();
        }

        private void BtnLaneSelect_Unchecked(object sender, RoutedEventArgs e)
        {
            _laneMode = false;
            if (!_markerMode && !_boundaryMode) { PreviewGrid.IsHitTestVisible = false; PreviewGrid.Cursor = null; }
            RemoveTempLine();
        }

        private void BtnClearLanes_Click(object sender, RoutedEventArgs e)
        {
            _vm.LaneXFractions.Clear();
            UpdatePreview(); RefreshOverlay();
        }

        // ============================================================
        //  BOUNDARY MODE
        // ============================================================

        private void BtnBoundaryMode_Checked(object sender, RoutedEventArgs e)
        {
            // Clear everything before starting fresh
            _vm.LaneXFractions.Clear();
            _vm.LeftBoundFrac = null;
            _vm.RightBoundFrac = null;
            _boundaryMode = true;
            _boundNextLeft = true;
            _laneMode = false;
            _markerMode = false;
            if (BtnLaneSelect != null) BtnLaneSelect.IsChecked = false;
            if (BtnAddMarkerMode != null) BtnAddMarkerMode.IsChecked = false;
            PreviewGrid.IsHitTestVisible = true;
            PreviewGrid.Cursor = Cursors.Cross;
            UpdateBoundStatusLabel();
            UpdatePreview(); RefreshOverlay();
        }

        private void BtnBoundaryMode_Unchecked(object sender, RoutedEventArgs e)
        {
            _boundaryMode = false;
            if (!_laneMode && !_markerMode)
            { PreviewGrid.IsHitTestVisible = false; PreviewGrid.Cursor = null; }
            RemoveTempLine();
            UpdateBoundStatusLabel();
        }

        private void UpdateBoundStatusLabel()
        {
            if (TxtBoundStatus == null) return;
            if (!_boundaryMode)
            { TxtBoundStatus.Visibility = Visibility.Collapsed; return; }
            TxtBoundStatus.Text = _boundNextLeft
                ? "Next click: Left bound"
                : "Next click: Right bound";
            TxtBoundStatus.Visibility = Visibility.Visible;
        }

        private void BtnClearBounds_Click(object sender, RoutedEventArgs e)
        {
            _vm.LeftBoundFrac = null;
            _vm.RightBoundFrac = null;
            UpdatePreview(); RefreshOverlay();
        }

        // ============================================================
        //  MARKER MODE
        // ============================================================

        private void BtnAddMarkerMode_Checked(object sender, RoutedEventArgs e)
        {
            _markerMode = true;
            _laneMode = false;
            _boundaryMode = false;
            if (BtnLaneSelect != null) BtnLaneSelect.IsChecked = false;
            if (BtnBoundaryMode != null) BtnBoundaryMode.IsChecked = false;
            PreviewGrid.IsHitTestVisible = true;
            PreviewGrid.Cursor = Cursors.Cross;
        }

        private void BtnAddMarkerMode_Unchecked(object sender, RoutedEventArgs e)
        {
            _markerMode = false;
            if (!_laneMode && !_boundaryMode) { PreviewGrid.IsHitTestVisible = false; PreviewGrid.Cursor = null; }
            RemoveTempLine();
        }

        // ============================================================
        //  OVERLAY MOUSE EVENTS
        // ============================================================

        private void Overlay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_laneMode && !_markerMode && !_boundaryMode) return;
            // Capture on PreviewGrid (White background = full figure surface, always hit-testable)
            PreviewGrid.CaptureMouse();
            e.Handled = true;
        }

        private void Overlay_RightMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_laneMode && !_markerMode && !_boundaryMode) return;
            PreviewGrid.CaptureMouse();
            e.Handled = true;
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_laneMode && !_markerMode && !_boundaryMode) return;
            var pos = e.GetPosition(PreviewGrid);   // PreviewGrid == OverlayCanvas coords
            MoveTempLine(pos);
        }

        private void Overlay_LeftClick(object sender, MouseButtonEventArgs e)
        {
            if (!_laneMode && !_markerMode && !_boundaryMode) return;
            PreviewGrid.ReleaseMouseCapture();
            var pos = e.GetPosition(PreviewGrid);

            if (_laneMode)
            {
                // Click position IS the lane center — symbol will appear here
                double frac = CanvasXToFrac(pos.X);
                frac = Math.Max(0.0, Math.Min(1.0, frac));
                _vm.LaneXFractions.Add(frac);
                _vm.LaneXFractions.Sort();
                UpdatePreview(); RefreshOverlay();
                // Auto-exit after reaching the required number of lanes
                if (_vm.LaneXFractions.Count >= Cols)
                {
                    if (BtnLaneSelect != null) BtnLaneSelect.IsChecked = false;
                }
            }
            else if (_boundaryMode)
            {
                double frac = CanvasXToFrac(pos.X);
                frac = Math.Max(0.0, Math.Min(1.0, frac));
                if (_boundNextLeft) _vm.LeftBoundFrac = frac;
                else _vm.RightBoundFrac = frac;
                _boundNextLeft = !_boundNextLeft;
                UpdatePreview(); RefreshOverlay(); UpdateBoundStatusLabel();
                // Auto-exit after both bounds are set
                if (_vm.LeftBoundFrac.HasValue && _vm.RightBoundFrac.HasValue)
                {
                    if (BtnBoundaryMode != null) BtnBoundaryMode.IsChecked = false;
                }
            }
            else if (_markerMode)
            {
                double frac = CanvasYToMarkerFrac(pos.Y);
                frac = Math.Max(0.0, Math.Min(1.0, frac));
                string label = PromptLabel("Enter label (e.g. 50 kDa):", "50 kDa");
                if (label == null) return;
                var m = new MarkerEntry { Label = label, YFraction = frac };
                m.PropertyChanged += (s, ev) => { UpdatePreview(); RefreshOverlay(); };
                _vm.Markers.Add(m);
                UpdatePreview(); RefreshOverlay();
            }
        }

        private void Overlay_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (!_laneMode && !_markerMode && !_boundaryMode) return;
            PreviewGrid.ReleaseMouseCapture();
            var pos = e.GetPosition(PreviewGrid);

            if (_laneMode && _vm.LaneXFractions.Count > 0)
            {
                double clickFrac = CanvasXToFrac(pos.X);
                int idx = NearestIndex(_vm.LaneXFractions, clickFrac);
                _vm.LaneXFractions.RemoveAt(idx);
                UpdatePreview(); RefreshOverlay();
            }
            else if (_boundaryMode)
            {
                // Right-click: remove nearest bound
                double frac = CanvasXToFrac(pos.X);
                double distL = _vm.LeftBoundFrac.HasValue ? Math.Abs(_vm.LeftBoundFrac.Value - frac) : double.MaxValue;
                double distR = _vm.RightBoundFrac.HasValue ? Math.Abs(_vm.RightBoundFrac.Value - frac) : double.MaxValue;
                if (distL <= distR) _vm.LeftBoundFrac = null;
                else _vm.RightBoundFrac = null;
                UpdatePreview(); RefreshOverlay(); UpdateBoundStatusLabel();
            }
            else if (_markerMode && _vm.Markers.Count > 0)
            {
                double clickFrac = CanvasYToMarkerFrac(pos.Y);
                var nearest = _vm.Markers.OrderBy(m => Math.Abs(m.YFraction - clickFrac)).FirstOrDefault();
                if (nearest != null) { _vm.Markers.Remove(nearest); UpdatePreview(); RefreshOverlay(); }
            }
        }

        // ── Coordinate mapping ────────────────────────────────

        private double CanvasXToFrac(double canvasX)
        {
            // canvas pixel → figure 96dpi space → fraction within blotW
            double figX = canvasX / _zoom;
            return (figX - _rBlotAreaX) / _rBlotW;
        }

        private double CanvasYToMarkerFrac(double canvasY)
        {
            double figY = canvasY / _zoom;
            if (_rBlotAreaH <= 0) return 0.5;
            return (figY - _rBlotAreaY) / _rBlotAreaH;
        }

        // ── Temp line on canvas ───────────────────────────────

        private void MoveTempLine(Point pos)
        {
            RemoveTempLine();
            _tempLine = new Line
            {
                StrokeThickness = 1.4,
                StrokeDashArray = new DoubleCollection(new double[] { 5, 3 })
            };

            if (_laneMode)
            {
                // Blue vertical — marks the exact lane center
                _tempLine.Stroke = new SolidColorBrush(Color.FromArgb(200, 30, 144, 255));
                _tempLine.X1 = pos.X; _tempLine.Y1 = 0;
                _tempLine.X2 = pos.X; _tempLine.Y2 = OverlayCanvas.ActualHeight;
            }
            else if (_boundaryMode)
            {
                // Green vertical — boundary preview
                _tempLine.Stroke = new SolidColorBrush(Color.FromArgb(200, 40, 180, 40));
                _tempLine.StrokeDashArray = null; // solid
                _tempLine.StrokeThickness = 2.0;
                _tempLine.X1 = pos.X; _tempLine.Y1 = 0;
                _tempLine.X2 = pos.X; _tempLine.Y2 = OverlayCanvas.ActualHeight;
            }
            else
            {
                // Orange horizontal — marker
                _tempLine.Stroke = new SolidColorBrush(Color.FromArgb(160, 230, 126, 34));
                _tempLine.X1 = 0; _tempLine.Y1 = pos.Y;
                _tempLine.X2 = OverlayCanvas.ActualWidth; _tempLine.Y2 = pos.Y;
            }

            OverlayCanvas.Children.Add(_tempLine);
        }

        private void RemoveTempLine()
        {
            if (_tempLine != null) { OverlayCanvas.Children.Remove(_tempLine); _tempLine = null; }
        }

        // ── Permanent overlay refresh ─────────────────────────

        private void RefreshOverlay()
        {
            if (OverlayCanvas == null) return;
            OverlayCanvas.Children.Clear();
            _tempLine = null;

            double canvasH = OverlayCanvas.ActualHeight;
            double canvasW = OverlayCanvas.ActualWidth;

            // ── Lane center lines: only shown while Set Lanes is active ────
            if (_laneMode)
            {
                foreach (double center96 in _rLaneCenters96)
                {
                    double cx = (_rBlotAreaX + center96) * _zoom;
                    OverlayCanvas.Children.Add(MakeOverlayLine(
                        cx, 0, cx, canvasH,
                        Color.FromArgb(200, 30, 144, 255), 1.4));
                }
            }

            // ── Left boundary (solid green) ───────────────────────────────
            if (_vm.LeftBoundFrac.HasValue)
            {
                double cx = (_rBlotAreaX + _vm.LeftBoundFrac.Value * _rBlotW) * _zoom;
                OverlayCanvas.Children.Add(MakeSolidLine(cx, 0, cx, canvasH,
                    Color.FromRgb(0, 160, 60), 2.0));
            }

            // ── Right boundary (solid red) ────────────────────────────────
            if (_vm.RightBoundFrac.HasValue)
            {
                double cx = (_rBlotAreaX + _vm.RightBoundFrac.Value * _rBlotW) * _zoom;
                OverlayCanvas.Children.Add(MakeSolidLine(cx, 0, cx, canvasH,
                    Color.FromRgb(210, 40, 40), 2.0));
            }

            // ── Marker lines (orange dashed horizontal) ───────────────────
            foreach (var marker in _vm.Markers)
            {
                double cy = (_rBlotAreaY + marker.YFraction * _rBlotAreaH) * _zoom;
                OverlayCanvas.Children.Add(MakeOverlayLine(
                    0, cy, canvasW, cy,
                    Color.FromArgb(200, 230, 126, 34), 1.5));
            }
        }

        private static Line MakeSolidLine(double x1, double y1, double x2, double y2, Color color, double thickness)
        {
            return new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                IsHitTestVisible = false
            };
        }

        private static Line MakeOverlayLine(double x1, double y1, double x2, double y2, Color color, double thickness)
        {
            return new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                StrokeDashArray = new DoubleCollection(new double[] { 5, 3 }),
                IsHitTestVisible = false
            };
        }

        // ============================================================
        //  ZOOM
        // ============================================================

        private void PreviewScroll_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == 0) return;
            e.Handled = true;  // suppress scroll
            double delta = e.Delta > 0 ? 0.1 : -0.1;
            double next = Math.Max(0.2, Math.Min(2.5, _zoom + delta));
            PreviewZoom.Value = next;   // triggers PreviewZoom_ValueChanged
        }

        private void PreviewZoom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _zoom = e.NewValue;
            if (TxtZoomLabel != null)
                TxtZoomLabel.Text = ((int)(_zoom * 100)) + "%";
            ApplyZoom();
            RefreshOverlay();
        }

        private void BtnFitZoom_Click(object sender, RoutedEventArgs e)
        {
            if (PreviewImage == null || !(PreviewImage.Source is BitmapSource bmp)) return;
            double aw = PreviewScroll.ActualWidth - 48;
            double ah = PreviewScroll.ActualHeight - 48;
            if (aw <= 0 || ah <= 0) return;
            double s = Math.Min(aw / bmp.PixelWidth, ah / bmp.PixelHeight);
            PreviewZoom.Value = Math.Max(0.2, Math.Min(2.5, s));
        }

        private void ApplyZoom()
        {
            if (PreviewScale == null) return;
            // Use LayoutTransform scale — no Width/Height manipulation needed.
            // This guarantees pixel-perfect 1:1 mapping with no aspect ratio distortion.
            PreviewScale.ScaleX = _zoom;
            PreviewScale.ScaleY = _zoom;
            // Keep OverlayCanvas sized to the unscaled bitmap (transform handles display size)
            if (PreviewImage?.Source is BitmapSource bmp)
            {
                OverlayCanvas.Width = bmp.PixelWidth;
                OverlayCanvas.Height = bmp.PixelHeight;
            }
        }

        // ── Gene ID lookup ──────────────────────────────────────────
        private System.Windows.Threading.DispatcherTimer _geneLookupTimer;
        private string _foundOfficialSymbol = string.Empty;

        /// <summary>Triggered by TxtGeneSearch typing — debounced 400 ms.</summary>
        private void TxtGeneSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string name = TxtGeneSearch?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(name))
            {
                TxtGeneOfficialName.Text = string.Empty;
                _foundOfficialSymbol = string.Empty;
                _lastLookedUpName = string.Empty;
                if (BtnUseGeneName != null) BtnUseGeneName.IsEnabled = false;
                return;
            }
            if (_geneLookupTimer == null)
            {
                _geneLookupTimer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(400) };
                _geneLookupTimer.Tick += async (s2, e2) =>
                {
                    _geneLookupTimer.Stop();
                    await DoGeneLookupAsync(TxtGeneSearch.Text.Trim());
                };
            }
            _geneLookupTimer.Stop();
            _geneLookupTimer.Start();
        }

        /// <summary>Copy the found official symbol into the antibody name box.</summary>
        private void BtnUseGeneName_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_foundOfficialSymbol)) return;
            _suppressUpdate = true;
            TxtBlotName.Text = _foundOfficialSymbol;
            _suppressUpdate = false;
            if (BlotListBox.SelectedItem is BlotEntry b) { b.Name = _foundOfficialSymbol; UpdatePreview(); }
        }

        private async System.Threading.Tasks.Task DoGeneLookupAsync(string name)
        {
            if (string.IsNullOrEmpty(name) || name == _lastLookedUpName) return;
            _lastLookedUpName = name;
            if (TxtGeneOfficialName == null) return;

            string species = (CboGeneSpecies?.SelectedIndex == 1) ? "mouse" : "human";
            TxtGeneOfficialName.Text = "searching\u2026";
            TxtGeneOfficialName.Foreground = System.Windows.Media.Brushes.Gray;

            try
            {
                if (!_geneIdService.IsDatabaseLoaded)
                {
                    _geneDbLoading = true;
                    await _geneIdService.LoadDatabaseAsync(species);
                    _geneDbLoading = false;
                }

                var results = _geneIdService.Convert(name, "symbol");
                if (results == null || results.Count == 0)
                    results = _geneIdService.Convert(name, "auto");

                if (results != null && results.Count > 0)
                {
                    var g = results[0];
                    string fullName = string.IsNullOrEmpty(g.FullName) ? string.Empty : $"  \u2014  {g.FullName}";
                    TxtGeneOfficialName.Text = g.Symbol + fullName;
                    TxtGeneOfficialName.Foreground = g.Symbol.Equals(name, StringComparison.OrdinalIgnoreCase)
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 174, 96))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(41, 128, 185));
                    _foundOfficialSymbol = g.Symbol;
                    if (BtnUseGeneName != null) BtnUseGeneName.IsEnabled = true;
                }
                else
                {
                    TxtGeneOfficialName.Text = "not found";
                    TxtGeneOfficialName.Foreground = System.Windows.Media.Brushes.Gray;
                    _foundOfficialSymbol = string.Empty;
                    if (BtnUseGeneName != null) BtnUseGeneName.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                TxtGeneOfficialName.Text = string.Empty;
                System.Diagnostics.Debug.WriteLine("[WBOrganizer] GeneLookup: " + ex.Message);
            }
        }

        private async void CboGeneSpecies_Changed(object sender, SelectionChangedEventArgs e)
        {
            _lastLookedUpName = string.Empty;
            _foundOfficialSymbol = string.Empty;
            if (BtnUseGeneName != null) BtnUseGeneName.IsEnabled = false;
            string species = (CboGeneSpecies?.SelectedIndex == 1) ? "mouse" : "human";
            _geneDbLoading = true;
            await _geneIdService.LoadDatabaseAsync(species);
            _geneDbLoading = false;
            string query = TxtGeneSearch?.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(query)) await DoGeneLookupAsync(query);
        }

        private void LayoutSetting_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_suppressUpdate) UpdatePreview();
        }

        // ============================================================
        //  PREVIEW
        // ============================================================

        private void BtnUpdatePreview_Click(object sender, RoutedEventArgs e) { UpdatePreview(); }

        private void UpdatePreview()
        {
            // 加強防護，避免介面初始化階段出現 NullReference
            if (_suppressUpdate || TxtBlotWidth == null || TxtPreviewInfo == null || OverlayCanvas == null) return;
            try
            {
                var bmp = RenderFigure(96);
                if (bmp == null)
                {
                    TxtPreviewInfo.Text = "  Add blot images to begin.";
                    PreviewImage.Source = null;
                    return;
                }
                PreviewImage.Source = bmp;
                ApplyZoom();
                RefreshOverlay();
                TxtPreviewInfo.Text = string.Format("  {0} \u00D7 {1} px  |  {2} blots  |  {3} cond rows  |  {4} labels",
                    bmp.PixelWidth, bmp.PixelHeight,
                    _vm.Blots.Count, _vm.ConditionRows.Count, _vm.Markers.Count);
            }
            catch (Exception ex)
            {
                TxtPreviewInfo.Text = "  Render error: " + ex.Message;
            }
        }

        // ============================================================
        //  RENDER ENGINE
        // ============================================================

        private BitmapSource RenderFigure(double dpi)
        {
            if (_vm.Blots.Count == 0) return null;

            // ── ALL coordinates are in DIP (96dpi logical pixels) ─────────
            // RenderTargetBitmap handles DPI upscaling automatically.
            // Do NOT multiply by scale here — that causes double-scaling.
            double scale = dpi / 96.0;
            double blotW = BlotW;
            double labelW = LabelW;
            double gap = Gap;
            double fSize = FSize;        // DIP font size
            double border = BorderPx;
            double margin = Margin96;
            int cols = Cols;

            // ── Lane centers (DIP offsets from blot left edge) ────────────
            List<double> laneOffsets = GetLaneCenters(blotW, cols);

            // ── Cache 96dpi lane centers for overlay ──────────────────────
            _rLaneCenters96 = new List<double>(laneOffsets); // already in DIP/96dpi

            // ── Top label height ───────────────────────────────────────────
            double topLabelFS = 15.0;                                              // top label font size (fixed)
            double topLabelH = _vm.TopLabels.Count > 0 ? topLabelFS * 1.5 + 10.0 : 0;

            // ── Condition header height ─────────────────────────────────────
            // Measure actual layout-box height for this font/size/dpi.
            // All FormattedText objects with the same font/size/bold have identical
            // .Height, so we measure once and use it for every glyph in every row.
            var refTF = MakeTF("Ag+|-", fSize, dpi, Brushes.Black, bold: true);
            double refH = refTF.Height;           // true layout-box height
            double condRowH = refH + 14.0;            // equal padding top & bottom
            double totalCondH = _vm.ConditionRows.Count * condRowH;

            // ── Blot heights (proportional to blotW in DIP) ───────────────
            var blotHeights = new List<double>();
            foreach (var b in _vm.Blots)
                blotHeights.Add(b.Image == null ? blotW * 0.28
                    : blotW * (double)b.Image.PixelHeight / b.Image.PixelWidth);

            double totalBlotH = 0;
            for (int i = 0; i < blotHeights.Count; i++)
                totalBlotH += blotHeights[i] + 2 * border + (i < blotHeights.Count - 1 ? gap : 0);

            // ── Cache geometry for overlay mapping (96dpi = DIP) ──────────
            _rBlotAreaX = margin + labelW + border;
            _rBlotAreaY = margin + topLabelH
                        + (_vm.TopLabels.Count > 0 ? 4.0 : 0.0)
                        + totalCondH
                        + (_vm.ConditionRows.Count > 0 ? 9.0 : 0.0);  // 6 gap + 3 line
            _rBlotAreaH = totalBlotH;
            _rBlotW = blotW;

            // ── Canvas size in DIP ─────────────────────────────────────────
            double totalW = margin + labelW + blotW + 2 * border + margin;
            double totalH = margin + topLabelH
                          + (_vm.TopLabels.Count > 0 ? 4.0 : 0.0)
                          + totalCondH
                          + (_vm.ConditionRows.Count > 0 ? 9.0 : 0.0)  // 6 gap + 3 line
                          + totalBlotH + margin;

            if (totalW < 4 || totalH < 4) return null;

            // ── DrawingVisual (DIP coordinates) ───────────────────────────
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, totalW, totalH));

                double curY = margin;
                double blotX = margin + labelW;   // left edge of border rect (DIP)

                // ── Top Group Labels ───────────────────────────────────────
                if (_vm.TopLabels.Count > 0)
                {
                    var linePen = new Pen(Brushes.Black, 1.2);
                    foreach (var tl in _vm.TopLabels)
                    {
                        int s = Math.Max(1, tl.StartLane) - 1;
                        int e = Math.Min(cols, tl.EndLane) - 1;
                        if (s > e || s >= laneOffsets.Count) continue;

                        double xStart = blotX + border + (s > 0 ? laneOffsets[s - 1] : 0);
                        double xEnd = blotX + border + laneOffsets[Math.Min(e, laneOffsets.Count - 1)];
                        double pad = 4.0;
                        if (xEnd - xStart > pad * 2) { xStart += pad; xEnd -= pad; }

                        var tf = MakeTF(tl.Text, topLabelFS, dpi, Brushes.Black, bold: true);
                        double tx = xStart + (xEnd - xStart - tf.Width) / 2;
                        dc.DrawText(tf, new Point(tx, curY));

                        double lineY = curY + tf.Height + 2.0;
                        dc.DrawLine(linePen, new Point(xStart, lineY), new Point(xEnd, lineY));
                    }
                    curY += topLabelH + 4.0;
                }

                // ── Condition rows ──────────────────────────────────────────
                if (_vm.ConditionRows.Count > 0)
                {
                    double condX0 = blotX + border;
                    // All glyphs drawn at the same textY0: layout-box top is
                    // (condRowH - refH)/2 from the row top → identical for every glyph.
                    // Lane pitch for auto-rotate threshold
                    double lanePitch = laneOffsets.Count >= 2
                        ? laneOffsets[1] - laneOffsets[0]
                        : blotW / Math.Max(1, cols);

                    foreach (var condRow in _vm.ConditionRows)
                    {
                        // Row label — right-aligned, vertically centred using tf.Height
                        if (condRow.RowLabel.Length > 0)
                        {
                            var tf = MakeTF(condRow.RowLabel, fSize, dpi, Brushes.Black, bold: true);
                            double lx = margin + labelW - tf.Width - 6.0;
                            double ty = curY + (condRowH - tf.Height) / 2.0;
                            dc.DrawText(tf, new Point(Math.Max(margin, lx), ty));
                        }

                        // Symbols — horizontally centred on lane, vertically centred using tf.Height
                        for (int ci = 0; ci < condRow.Cells.Count && ci < laneOffsets.Count; ci++)
                        {
                            double cx = condX0 + laneOffsets[ci];
                            string val = condRow.Cells[ci].Value ?? "-";
                            var tf = MakeTF(val, 15.0, dpi, Brushes.Black, bold: true);
                            double tx = cx - tf.Width / 2.0;
                            double ty = curY + (condRowH - tf.Height) / 2.0;

                            if (tf.Width > lanePitch * 0.85 && tf.Width > 0)
                            {
                                double cosA = Math.Min(1.0, lanePitch * 0.80 / tf.Width);
                                double deg = -Math.Acos(cosA) * 180.0 / Math.PI;
                                dc.PushTransform(new RotateTransform(deg, cx, curY + condRowH / 2.0));
                                dc.DrawText(tf, new Point(tx, ty));
                                dc.Pop();
                            }
                            else
                            {
                                dc.DrawText(tf, new Point(tx, ty));
                            }
                        }

                        curY += condRowH;
                    }
                    curY += 6.0;
                }

                // ── Blot rows ─────────────────────────────────────────────
                var borderPen = new Pen(Brushes.Black, border) { LineJoin = PenLineJoin.Miter };

                for (int bi = 0; bi < _vm.Blots.Count; bi++)
                {
                    double bh = blotHeights[bi];
                    double rowH = bh + 2 * border;
                    var blot = _vm.Blots[bi];

                    // α-label — right-aligned
                    string lbl = blot.Prefix + blot.Name;
                    var lblTF = MakeTF(lbl, fSize, dpi, Brushes.Black, bold: true);
                    double lx = margin + labelW - lblTF.Width - 6.0;
                    double ly = curY + (rowH - lblTF.Height) / 2;
                    dc.DrawText(lblTF, new Point(Math.Max(margin, lx), ly));

                    // Border rectangle
                    dc.DrawRectangle(null, borderPen, new Rect(
                        blotX + border / 2, curY + border / 2,
                        blotW + border, bh + border));

                    // Blot image
                    var imgRect = new Rect(blotX + border, curY + border, blotW, bh);
                    if (blot.Image != null)
                        dc.DrawRectangle(new ImageBrush(blot.Image) { Stretch = Stretch.Fill }, null, imgRect);
                    else
                    {
                        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(235, 235, 235)), null, imgRect);
                        var ph = MakeTF("[ " + blot.Name + " — no image ]", fSize * 0.82, dpi,
                                        new SolidColorBrush(Color.FromRgb(170, 170, 170)));
                        dc.DrawText(ph, new Point(
                            imgRect.Left + (blotW - ph.Width) / 2,
                            imgRect.Top + (bh - ph.Height) / 2));
                    }

                    curY += rowH + gap;
                }

                // ── MW Markers ────────────────────────────────────────────
                if (_vm.Markers.Count > 0)
                {
                    double blotTop = _rBlotAreaY;
                    double actualH = totalBlotH;
                    var markerPen = new Pen(new SolidColorBrush(Color.FromRgb(210, 120, 30)), 0.8);
                    markerPen.DashStyle = new DashStyle(new double[] { 4, 3 }, 0);

                    foreach (var mk in _vm.Markers)
                    {
                        double my = blotTop + mk.YFraction * actualH;

                        dc.DrawLine(markerPen,
                            new Point(blotX + border, my),
                            new Point(blotX + border + blotW, my));

                        var arrow = MakeTF("▶", fSize * 0.95, dpi,
                                          new SolidColorBrush(Color.FromRgb(210, 120, 30)));
                        double ax = blotX + border - arrow.Width - 2.0;
                        dc.DrawText(arrow, new Point(ax, my - arrow.Height / 2));

                        var lblTF = MakeTF(mk.Label, fSize * 0.88, dpi,
                                          new SolidColorBrush(Color.FromRgb(80, 80, 80)));
                        double lblX = ax - lblTF.Width - 3.0;
                        dc.DrawText(lblTF, new Point(Math.Max(margin, lblX), my - lblTF.Height / 2));
                    }
                }
            }

            // ── Rasterize: RTB size in physical pixels, DIP coords auto-scaled ──
            int pw = (int)Math.Ceiling(totalW * scale);
            int pixH = (int)Math.Ceiling(totalH * scale);
            if (pw < 1 || pixH < 1) return null;

            var rtb = new RenderTargetBitmap(pw, pixH, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        // ============================================================
        //  HELPERS — lanes
        // ============================================================

        /// <summary>
        /// Returns a list of X offsets (from blot left edge) for each lane boundary.
        /// If manual positions are set, use those; otherwise equal spacing.
        /// Count = cols (last entry = blotW).
        /// </summary>
        /// <summary>
        /// Returns lane CENTER X positions (offset from blot left edge, in render pixels).
        /// Priority: manual centers → auto-spacing within bounds → auto-spacing full width.
        /// </summary>
        private List<double> GetLaneCenters(double blotW, int cols)
        {
            var result = new List<double>();
            if (_vm.LaneXFractions.Count > 0)
            {
                // Manual mode: click position IS the symbol center
                foreach (var f in _vm.LaneXFractions)
                    result.Add(f * blotW);
            }
            else
            {
                // Auto mode: distribute within boundaries
                double lo = (_vm.LeftBoundFrac ?? 0.0) * blotW;
                double hi = (_vm.RightBoundFrac ?? 1.0) * blotW;
                double span = hi - lo;
                for (int c = 0; c < cols; c++)
                    result.Add(lo + span * (c + 0.5) / cols);
            }
            return result;
        }

        // ── Index helpers ─────────────────────────────────────

        private static int NearestIndex(List<double> list, double val)
        {
            int best = 0;
            double bestDist = Math.Abs(list[0] - val);
            for (int i = 1; i < list.Count; i++)
            {
                double d = Math.Abs(list[i] - val);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // ============================================================
        //  EXPORT
        // ============================================================

        private void BtnExportPng_Click(object sender, RoutedEventArgs e) { Export("PNG"); }
        private void BtnExportTiff_Click(object sender, RoutedEventArgs e) { Export("TIFF"); }

        private void Export(string fmt)
        {
            if (_vm.Blots.Count == 0)
            { MessageBox.Show("Please add at least one blot image.", "Info", MessageBoxButton.OK, MessageBoxImage.Information); return; }

            double dpi = CboExportDpi.SelectedIndex == 0 ? 150 : CboExportDpi.SelectedIndex == 2 ? 600 : 300;
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save Western Blot Figure",
                DefaultExt = fmt == "PNG" ? ".png" : ".tif",
                Filter = fmt == "PNG"
                    ? "PNG image (*.png)|*.png|All files (*.*)|*.*"
                    : "TIFF image (*.tif)|*.tif;*.tiff|All files (*.*)|*.*",
                FileName = "WesternBlot_" + DateTime.Now.ToString("yyyyMMdd_HHmm")
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var bmp = RenderFigure(dpi);
                if (bmp == null) { MessageBox.Show("Render failed.", "Error"); return; }

                BitmapEncoder enc = fmt == "PNG"
                    ? (BitmapEncoder)new PngBitmapEncoder()
                    : new TiffBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = IO.File.OpenWrite(dlg.FileName)) enc.Save(fs);

                MessageBox.Show(
                    string.Format("Saved!\n{0}\n{1} \u00D7 {2} px  |  {3} DPI",
                        dlg.FileName, bmp.PixelWidth, bmp.PixelHeight, (int)dpi),
                    "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ============================================================
        //  STATIC HELPERS
        // ============================================================

        // ============================================================
        //  IMAGE EDITOR — HANDLERS
        // ============================================================

        private void BtnEditorLoad_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|All files|*.*",
                Title = "Load image for editing"
            };
            if (dlg.ShowDialog() != true) return;
            _editorSourcePath = dlg.FileName;
            _editorSourceImage = LoadBmp(_editorSourcePath);
            TxtEditorFile.Text = IO.Path.GetFileName(_editorSourcePath);
            TxtEditorName.Text = IO.Path.GetFileNameWithoutExtension(_editorSourcePath);
            ResetEditorState(resetRotation: true, resetLevels: true, resetCrop: true);
            BtnEditorAddToBlots.IsEnabled = true;
            UpdateRotatedCache();
            RefreshEditorPreview();
        }

        // ── Rotation ────────────────────────────────────────────────
        private void EdSliderRotate_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressUpdate || _editorSourceImage == null) return;
            _edRotation = e.NewValue;
            UpdateRotatedCache();
            RefreshEditorPreview();  // overlay redrawn inside
        }

        private void BtnEditorResetRotation_Click(object sender, RoutedEventArgs e)
        {
            _suppressUpdate = true; EdSliderRotate.Value = 0; _suppressUpdate = false;
            _edRotation = 0;
            UpdateRotatedCache();
            RefreshEditorPreview();
        }

        // ── 90° rotate + flip (applied to current working image, baked immediately) ──

        private void BtnRotateCW_Click(object sender, RoutedEventArgs e) => ApplyHardTransform(TransformType.RotateCW);
        private void BtnRotateCCW_Click(object sender, RoutedEventArgs e) => ApplyHardTransform(TransformType.RotateCCW);
        private void BtnFlipH_Click(object sender, RoutedEventArgs e) => ApplyHardTransform(TransformType.FlipH);
        private void BtnFlipV_Click(object sender, RoutedEventArgs e) => ApplyHardTransform(TransformType.FlipV);

        private enum TransformType { RotateCW, RotateCCW, FlipH, FlipV }

        /// <summary>
        /// Bake the hard transform (90° rotate or flip) into _editorSourceImage,
        /// then reset the fine-angle slider to 0 and reset crop to full frame.
        /// This way the crop overlay always operates on the "current" upright frame.
        /// </summary>
        private void ApplyHardTransform(TransformType t)
        {
            if (_editorSourceImage == null) return;

            // Work on the current rotated+level-applied image as the new source
            BitmapSource current = _edRotatedImage ?? (BitmapSource)_editorSourceImage;

            BitmapSource result = HardTransform(current, t);
            if (result == null) return;

            // Convert to BitmapImage so it can be stored as the new source
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(result));
            var ms = new System.IO.MemoryStream();
            enc.Save(ms); ms.Position = 0;
            var bmpImg = new BitmapImage();
            bmpImg.BeginInit();
            bmpImg.CacheOption = BitmapCacheOption.OnLoad;
            bmpImg.StreamSource = ms;
            bmpImg.EndInit(); bmpImg.Freeze();

            _editorSourceImage = bmpImg;
            // Reset fine rotation and crop so overlay is correct
            _suppressUpdate = true;
            EdSliderRotate.Value = 0; _suppressUpdate = false;
            _edRotation = 0;
            _edCropL = 0; _edCropR = 1; _edCropT = 0; _edCropB = 1;
            UpdateRotatedCache();
            RefreshEditorPreview();
        }

        private static BitmapSource HardTransform(BitmapSource src, TransformType t)
        {
            double angle = 0;
            bool flipH = false, flipV = false;

            switch (t)
            {
                case TransformType.RotateCW: angle = 90; break;
                case TransformType.RotateCCW: angle = -90; break;
                case TransformType.FlipH: flipH = true; break;
                case TransformType.FlipV: flipV = true; break;
            }

            double ow = src.PixelWidth * 96.0 / src.DpiX;
            double oh = src.PixelHeight * 96.0 / src.DpiY;

            // 90° rotation swaps width and height
            double nw = (angle != 0) ? oh : ow;
            double nh = (angle != 0) ? ow : oh;
            int pw2 = (int)Math.Round(nw), ph2 = (int)Math.Round(nh);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, pw2, ph2));
                dc.PushTransform(new TranslateTransform(pw2 / 2.0, ph2 / 2.0));
                if (angle != 0) dc.PushTransform(new RotateTransform(angle));
                if (flipH) dc.PushTransform(new ScaleTransform(-1, 1));
                if (flipV) dc.PushTransform(new ScaleTransform(1, -1));
                dc.PushTransform(new TranslateTransform(-ow / 2.0, -oh / 2.0));
                dc.DrawImage(src, new Rect(0, 0, ow, oh));
                dc.Pop();                          // TranslateTransform(-ow/2, -oh/2)
                if (flipH || flipV) dc.Pop();      // ScaleTransform
                if (angle != 0) dc.Pop();      // RotateTransform
                dc.Pop();                          // TranslateTransform(pw2/2, ph2/2)
            }

            var rtb = new RenderTargetBitmap(pw2, ph2, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze();
            return rtb;
        }

        /// <summary>Recompute _edRotatedImage from _editorSourceImage + _edRotation.</summary>
        private void UpdateRotatedCache()
        {
            if (_editorSourceImage == null) { _edRotatedImage = null; return; }
            if (_edRotation == 0) { _edRotatedImage = null; return; }
            _edRotatedImage = RotateBitmap(_editorSourceImage, _edRotation);
        }

        // ── Levels ──────────────────────────────────────────────────
        private void EdSliderLvMin_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressUpdate || _editorSourceImage == null) return;
            _edLvMin = (byte)e.NewValue;
            if (_edLvMin >= _edLvMax) { _edLvMin = (byte)(_edLvMax > 0 ? _edLvMax - 1 : 0); EdSliderLvMin.Value = _edLvMin; }
            RefreshEditorPreview();
        }
        private void EdSliderLvMax_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressUpdate || _editorSourceImage == null) return;
            _edLvMax = (byte)e.NewValue;
            if (_edLvMax <= _edLvMin) { _edLvMax = (byte)(_edLvMin < 255 ? _edLvMin + 1 : 255); EdSliderLvMax.Value = _edLvMax; }
            RefreshEditorPreview();
        }
        private void BtnEditorResetLevels_Click(object sender, RoutedEventArgs e)
        {
            _suppressUpdate = true; EdSliderLvMin.Value = 0; EdSliderLvMax.Value = 255; _suppressUpdate = false;
            _edLvMin = 0; _edLvMax = 255;
            _edGrayscale = false;
            if (ChkGrayscale != null) ChkGrayscale.IsChecked = false;
            RefreshEditorPreview();
        }

        private void ChkGrayscale_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressUpdate || _editorSourceImage == null) return;
            _edGrayscale = ChkGrayscale.IsChecked == true;
            RefreshEditorPreview();
        }

        private static BitmapSource ToGrayscale(BitmapSource src)
        {
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int pw = conv.PixelWidth, ph = conv.PixelHeight;
            int stride = pw * 4;
            byte[] pix = new byte[ph * stride];
            conv.CopyPixels(pix, stride, 0);
            for (int i = 0; i < pix.Length; i += 4)
            {
                // Luminance formula: 0.299R + 0.587G + 0.114B
                byte g = (byte)(pix[i + 2] * 0.299 + pix[i + 1] * 0.587 + pix[i] * 0.114);
                pix[i] = pix[i + 1] = pix[i + 2] = g;
            }
            var wb = new WriteableBitmap(pw, ph, src.DpiX, src.DpiY, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, pw, ph), pix, stride, 0);
            wb.Freeze();
            return wb;
        }

        private void BtnEditorResetAll_Click(object sender, RoutedEventArgs e)
        {
            ResetEditorState(resetRotation: true, resetLevels: true, resetCrop: true);
            UpdateRotatedCache();
            RefreshEditorPreview();
        }

        /// <summary>Bake the current crop selection into _editorSourceImage, then
        /// reset crop to full frame so the cropped result fills the preview.</summary>
        private void BtnApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            if (_editorSourceImage == null) return;
            // Skip if crop is essentially full frame
            if (_edCropL <= 0.001 && _edCropR >= 0.999 && _edCropT <= 0.001 && _edCropB >= 0.999) return;

            BitmapSource current = _edRotatedImage ?? (BitmapSource)_editorSourceImage;

            int pw = current.PixelWidth, ph = current.PixelHeight;
            int x = (int)(_edCropL * pw), y = (int)(_edCropT * ph);
            int w = Math.Max(1, (int)(_edCropR * pw) - x);
            int h = Math.Max(1, (int)(_edCropB * ph) - y);
            w = Math.Min(w, pw - x); h = Math.Min(h, ph - y);
            BitmapSource cropped = new CroppedBitmap(current, new Int32Rect(x, y, w, h));

            // Convert to BitmapImage (96 DPI so ActualWidth == PixelWidth)
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(cropped));
            var ms = new System.IO.MemoryStream();
            enc.Save(ms); ms.Position = 0;
            var bmpImg = new BitmapImage();
            bmpImg.BeginInit();
            bmpImg.CacheOption = BitmapCacheOption.OnLoad;
            bmpImg.StreamSource = ms;
            bmpImg.EndInit(); bmpImg.Freeze();

            _editorSourceImage = bmpImg;
            // Fine rotation was already baked into _edRotatedImage; reset it
            _suppressUpdate = true; EdSliderRotate.Value = 0; _suppressUpdate = false;
            _edRotation = 0;
            _edCropL = 0; _edCropR = 1; _edCropT = 0; _edCropB = 1;
            _edRotatedImage = null;
            UpdateRotatedCache();
            RefreshEditorPreview();
        }

        /// <summary>Reset crop handles to full frame without changing the source image.</summary>
        private void BtnResetCropOnly_Click(object sender, RoutedEventArgs e)
        {
            _edCropL = 0; _edCropR = 1; _edCropT = 0; _edCropB = 1;
            DrawCropOverlay();
            BitmapSource baseImg = _edRotatedImage ?? (BitmapSource)_editorSourceImage;
            if (baseImg != null)
                TxtEditorDims.Text = string.Format("{0} × {1} px  (full frame)",
                                                   baseImg.PixelWidth, baseImg.PixelHeight);
        }

        private void ResetEditorState(bool resetRotation, bool resetLevels, bool resetCrop)
        {
            _suppressUpdate = true;
            if (resetRotation) { EdSliderRotate.Value = 0; _edRotation = 0; }
            if (resetLevels)
            {
                EdSliderLvMin.Value = 0; EdSliderLvMax.Value = 255;
                _edLvMin = 0; _edLvMax = 255;
                _edGrayscale = false;
                if (ChkGrayscale != null) ChkGrayscale.IsChecked = false;
            }
            if (resetCrop) { _edCropL = 0; _edCropR = 1; _edCropT = 0; _edCropB = 1; }
            _suppressUpdate = false;
        }

        // ── Preview refresh ──────────────────────────────────────────
        private void RefreshEditorPreview()
        {
            if (_editorSourceImage == null) return;
            BitmapSource baseImg = _edRotatedImage ?? (BitmapSource)_editorSourceImage;

            // Apply grayscale → levels for display
            BitmapSource display = baseImg;
            if (_edGrayscale) display = ToGrayscale(display);
            if (_edLvMin > 0 || _edLvMax < 255) display = ApplyLevels(display, _edLvMin, _edLvMax);

            EditorPreviewImage.Source = display;
            if (BtnApplyCrop != null)
                BtnApplyCrop.IsEnabled = _edCropL > 0.001 || _edCropR < 0.999 || _edCropT > 0.001 || _edCropB < 0.999;
            int outW = Math.Max(1, (int)((_edCropR - _edCropL) * baseImg.PixelWidth));
            int outH = Math.Max(1, (int)((_edCropB - _edCropT) * baseImg.PixelHeight));
            TxtEditorDims.Text = string.Format("{0} × {1} px  (crop output)", outW, outH);
            DrawCropOverlay();
        }

        // ── Crop canvas overlay ──────────────────────────────────────

        /// <summary>Returns the Rect (in canvas coordinates) where the image is actually displayed.
        /// The Canvas fills the full Grid cell; EditorPreviewImage (Stretch=Uniform, Center) is
        /// smaller and centered inside it. We use ActualWidth/Height directly — WPF has already
        /// done all DPI and aspect-ratio math for us.</summary>
        private Rect GetImageRectInCanvas()
        {
            if (_editorSourceImage == null) return Rect.Empty;
            double canvasW = EditorCropCanvas.ActualWidth;
            double canvasH = EditorCropCanvas.ActualHeight;
            double imgW = EditorPreviewImage.ActualWidth;
            double imgH = EditorPreviewImage.ActualHeight;
            if (canvasW <= 0 || imgW <= 0) return Rect.Empty;
            double offsetX = (canvasW - imgW) / 2.0;
            double offsetY = (canvasH - imgH) / 2.0;
            return new Rect(offsetX, offsetY, imgW, imgH);
        }

        private void DrawCropOverlay()
        {
            if (EditorCropCanvas == null) return;
            EditorCropCanvas.Children.Clear();
            if (_editorSourceImage == null) return;

            Rect img = GetImageRectInCanvas();
            if (img.IsEmpty || img.Width <= 0) return;

            double cL = img.X + _edCropL * img.Width;
            double cR = img.X + _edCropR * img.Width;
            double cT = img.Y + _edCropT * img.Height;
            double cB = img.Y + _edCropB * img.Height;
            double cW = EditorCropCanvas.ActualWidth;
            double cH = EditorCropCanvas.ActualHeight;

            // ── Dark vignette outside crop rect ──────────────────────
            var dark = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            AddCanvasRect(0, 0, cW, cT, dark);                          // top
            AddCanvasRect(0, cB, cW, cH - cB, dark);                   // bottom
            AddCanvasRect(0, cT, cL, cB - cT, dark);                   // left
            AddCanvasRect(cR, cT, cW - cR, cB - cT, dark);             // right

            // ── Crop border ───────────────────────────────────────────
            var border = new Rectangle
            {
                Width = cR - cL,
                Height = cB - cT,
                Stroke = Brushes.White,
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(border, cL); Canvas.SetTop(border, cT);
            EditorCropCanvas.Children.Add(border);

            // ── Rule-of-thirds guide lines ────────────────────────────
            var thirdspen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 0.6);
            for (int i = 1; i < 3; i++)
            {
                double x = cL + (cR - cL) * i / 3.0;
                double y = cT + (cB - cT) * i / 3.0;
                AddCanvasLine(x, cT, x, cB, thirdspen);
                AddCanvasLine(cL, y, cR, y, thirdspen);
            }

            // ── 8 handles ─────────────────────────────────────────────
            double mx = (cL + cR) / 2, my = (cT + cB) / 2;
            double[][] pts = {
                new[] { cL, cT }, new[] { mx, cT }, new[] { cR, cT },
                new[] { cL, my },                   new[] { cR, my },
                new[] { cL, cB }, new[] { mx, cB }, new[] { cR, cB }
            };
            foreach (var p in pts) AddHandle(p[0], p[1]);

            // Enable Crop button only when selection is not full-frame
            if (BtnApplyCrop != null)
                BtnApplyCrop.IsEnabled = _edCropL > 0.001 || _edCropR < 0.999 || _edCropT > 0.001 || _edCropB < 0.999;
        }

        private void AddCanvasRect(double x, double y, double w, double h, Brush fill)
        {
            if (w <= 0 || h <= 0) return;
            var r = new Rectangle { Width = w, Height = h, Fill = fill, IsHitTestVisible = false };
            Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
            EditorCropCanvas.Children.Add(r);
        }

        private void AddCanvasLine(double x1, double y1, double x2, double y2, Pen pen)
        {
            var l = new Line
            {
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Stroke = pen.Brush,
                StrokeThickness = pen.Thickness,
                IsHitTestVisible = false
            };
            EditorCropCanvas.Children.Add(l);
        }

        private void AddHandle(double cx, double cy)
        {
            double h = HANDLE_PX;
            var r = new Rectangle
            {
                Width = h,
                Height = h,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                StrokeThickness = 1.0,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(r, cx - h / 2);
            Canvas.SetTop(r, cy - h / 2);
            EditorCropCanvas.Children.Add(r);
        }

        // ── Crop drag events ─────────────────────────────────────────

        private void EditorCropCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawCropOverlay();
        }

        /// <summary>Triggered when the Image control's rendered size changes (e.g. window resize).
        /// Redraws the crop overlay so handles stay aligned.</summary>
        private void EditorImage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawCropOverlay();
        }

        private void CropCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_editorSourceImage == null) return;
            var pos = e.GetPosition(EditorCropCanvas);
            _cropDrag = HitTestCropHandle(pos);
            if (_cropDrag == CropHandle.None) return;
            _cropDragStart = pos;
            _cropDragL = _edCropL; _cropDragR = _edCropR;
            _cropDragT = _edCropT; _cropDragB = _edCropB;
            EditorCropCanvas.CaptureMouse();
            e.Handled = true;
        }

        private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_editorSourceImage == null) return;
            var pos = e.GetPosition(EditorCropCanvas);

            if (_cropDrag == CropHandle.None)
            {
                // Update cursor hint
                EditorCropCanvas.Cursor = CursorForHandle(HitTestCropHandle(pos));
                return;
            }

            Rect img = GetImageRectInCanvas();
            if (img.IsEmpty) return;
            double dx = (pos.X - _cropDragStart.X) / img.Width;
            double dy = (pos.Y - _cropDragStart.Y) / img.Height;

            double nL = _cropDragL, nR = _cropDragR, nT = _cropDragT, nB = _cropDragB;
            const double MIN = 0.02;

            switch (_cropDrag)
            {
                case CropHandle.TL: nL = _cropDragL + dx; nT = _cropDragT + dy; break;
                case CropHandle.TM: nT = _cropDragT + dy; break;
                case CropHandle.TR: nR = _cropDragR + dx; nT = _cropDragT + dy; break;
                case CropHandle.ML: nL = _cropDragL + dx; break;
                case CropHandle.MR: nR = _cropDragR + dx; break;
                case CropHandle.BL: nL = _cropDragL + dx; nB = _cropDragB + dy; break;
                case CropHandle.BM: nB = _cropDragB + dy; break;
                case CropHandle.BR: nR = _cropDragR + dx; nB = _cropDragB + dy; break;
                case CropHandle.Move:
                    double w = _cropDragR - _cropDragL, h = _cropDragB - _cropDragT;
                    nL = Math.Max(0, Math.Min(1 - w, _cropDragL + dx));
                    nR = nL + w;
                    nT = Math.Max(0, Math.Min(1 - h, _cropDragT + dy));
                    nB = nT + h;
                    break;
            }

            // Clamp and enforce minimum size
            nL = Math.Max(0, Math.Min(nL, 1)); nR = Math.Max(0, Math.Min(nR, 1));
            nT = Math.Max(0, Math.Min(nT, 1)); nB = Math.Max(0, Math.Min(nB, 1));
            if (nR - nL < MIN) { if (_cropDrag == CropHandle.TL || _cropDrag == CropHandle.ML || _cropDrag == CropHandle.BL) nL = nR - MIN; else nR = nL + MIN; }
            if (nB - nT < MIN) { if (_cropDrag == CropHandle.TL || _cropDrag == CropHandle.TM || _cropDrag == CropHandle.TR) nT = nB - MIN; else nB = nT + MIN; }
            nL = Math.Max(0, nL); nR = Math.Min(1, nR); nT = Math.Max(0, nT); nB = Math.Min(1, nB);

            _edCropL = nL; _edCropR = nR; _edCropT = nT; _edCropB = nB;

            // Redraw overlay only (fast, no image recompute)
            DrawCropOverlay();
            {
                var _bd = (BitmapSource)(_edRotatedImage ?? (BitmapSource)_editorSourceImage);
                int outW = Math.Max(1, (int)((_edCropR - _edCropL) * _bd.PixelWidth));
                int outH = Math.Max(1, (int)((_edCropB - _edCropT) * _bd.PixelHeight));
                TxtEditorDims.Text = string.Format("{0} × {1} px  (crop output)", outW, outH);
            }
        }

        private void CropCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_cropDrag != CropHandle.None)
            {
                EditorCropCanvas.ReleaseMouseCapture();
                _cropDrag = CropHandle.None;
            }
        }

        private CropHandle HitTestCropHandle(Point pos)
        {
            Rect img = GetImageRectInCanvas();
            if (img.IsEmpty) return CropHandle.None;

            double cL = img.X + _edCropL * img.Width;
            double cR = img.X + _edCropR * img.Width;
            double cT = img.Y + _edCropT * img.Height;
            double cB = img.Y + _edCropB * img.Height;
            double mx = (cL + cR) / 2, my = (cT + cB) / 2;
            double h = HANDLE_HIT;

            bool nL = Math.Abs(pos.X - cL) < h, nR = Math.Abs(pos.X - cR) < h;
            bool nT = Math.Abs(pos.Y - cT) < h, nB = Math.Abs(pos.Y - cB) < h;
            bool nMX = Math.Abs(pos.X - mx) < h, nMY = Math.Abs(pos.Y - my) < h;

            if (nL && nT) return CropHandle.TL;
            if (nMX && nT) return CropHandle.TM;
            if (nR && nT) return CropHandle.TR;
            if (nL && nMY) return CropHandle.ML;
            if (nR && nMY) return CropHandle.MR;
            if (nL && nB) return CropHandle.BL;
            if (nMX && nB) return CropHandle.BM;
            if (nR && nB) return CropHandle.BR;

            // Inside crop rect = move
            if (pos.X > cL && pos.X < cR && pos.Y > cT && pos.Y < cB)
                return CropHandle.Move;

            return CropHandle.None;
        }

        private static Cursor CursorForHandle(CropHandle h)
        {
            switch (h)
            {
                case CropHandle.TL: case CropHandle.BR: return Cursors.SizeNWSE;
                case CropHandle.TR: case CropHandle.BL: return Cursors.SizeNESW;
                case CropHandle.TM: case CropHandle.BM: return Cursors.SizeNS;
                case CropHandle.ML: case CropHandle.MR: return Cursors.SizeWE;
                case CropHandle.Move: return Cursors.SizeAll;
                default: return Cursors.Cross;
            }
        }

        // ── Add baked image to Blot Images ───────────────────────────
        private void BtnEditorAddToBlots_Click(object sender, RoutedEventArgs e)
        {
            if (_editorSourceImage == null) return;
            BitmapSource baked = BakeEditorAdjustments();
            if (baked == null) return;

            // Convert BitmapSource → BitmapImage via in-memory PNG
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(baked));
            var ms = new System.IO.MemoryStream();
            encoder.Save(ms);
            ms.Position = 0;

            var bmpImg = new BitmapImage();
            bmpImg.BeginInit();
            bmpImg.CacheOption = BitmapCacheOption.OnLoad;
            bmpImg.StreamSource = ms;
            bmpImg.EndInit();
            bmpImg.Freeze();

            string name = string.IsNullOrWhiteSpace(TxtEditorName.Text) ? "Protein" : TxtEditorName.Text.Trim();
            _vm.Blots.Add(new BlotEntry
            {
                FilePath = _editorSourcePath,
                Name = name,
                Image = bmpImg,
                Index = _vm.Blots.Count
            });

            UpdatePreview();

            var origContent = BtnEditorAddToBlots.Content;
            BtnEditorAddToBlots.Content = "\u2713  Added!";
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.4) };
            t.Tick += (s2, e2) => { BtnEditorAddToBlots.Content = origContent; t.Stop(); };
            t.Start();
        }

        /// <summary>Apply rotation → crop → grayscale → levels. Returns baked BitmapSource.</summary>
        private BitmapSource BakeEditorAdjustments()
        {
            if (_editorSourceImage == null) return null;
            BitmapSource result = _edRotatedImage ?? (BitmapSource)_editorSourceImage;

            // 1. Crop (on rotated frame, using pixel coordinates)
            if (_edCropL > 0 || _edCropR < 1 || _edCropT > 0 || _edCropB < 1)
            {
                int pw = result.PixelWidth, ph = result.PixelHeight;
                int x = (int)(_edCropL * pw), y = (int)(_edCropT * ph);
                int w = Math.Max(1, (int)(_edCropR * pw) - x);
                int h = Math.Max(1, (int)(_edCropB * ph) - y);
                w = Math.Min(w, pw - x); h = Math.Min(h, ph - y);
                if (w > 0 && h > 0) result = new CroppedBitmap(result, new Int32Rect(x, y, w, h));
            }

            // 2. Grayscale
            if (_edGrayscale) result = ToGrayscale(result);

            // 3. Levels
            if (_edLvMin > 0 || _edLvMax < 255)
                result = ApplyLevels(result, _edLvMin, _edLvMax);

            return result;
        }

        /// <summary>Rotate BitmapSource by degrees. Works in DIP space at 96 DPI so the
        /// result pixel dimensions equal DIP dimensions, matching WPF layout expectations.</summary>
        private static BitmapSource RotateBitmap(BitmapSource src, double degrees)
        {
            if (degrees == 0) return src;
            double rad = degrees * Math.PI / 180.0;
            // Use DIP size so DrawingVisual coordinates match WPF layout
            double ow = src.PixelWidth * 96.0 / src.DpiX;
            double oh = src.PixelHeight * 96.0 / src.DpiY;
            double nw = Math.Abs(ow * Math.Cos(rad)) + Math.Abs(oh * Math.Sin(rad));
            double nh = Math.Abs(ow * Math.Sin(rad)) + Math.Abs(oh * Math.Cos(rad));
            int pw2 = (int)Math.Ceiling(nw), ph2 = (int)Math.Ceiling(nh);

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, pw2, ph2));
                dc.PushTransform(new TranslateTransform(pw2 / 2.0, ph2 / 2.0));
                dc.PushTransform(new RotateTransform(degrees));
                dc.PushTransform(new TranslateTransform(-ow / 2.0, -oh / 2.0));
                dc.DrawImage(src, new Rect(0, 0, ow, oh));
                dc.Pop(); dc.Pop(); dc.Pop();
            }

            // Output at 96 DPI so PixelWidth == DIP width (required for GetImageRectInCanvas)
            var rtb = new RenderTargetBitmap(pw2, ph2, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv); rtb.Freeze();
            return rtb;
        }

        // ============================================================
        //  LEVELS HELPER (standalone, used by Image Editor bake)
        // ============================================================



        private static BitmapSource ApplyLevels(BitmapSource src, byte lo, byte hi)
        {
            if (lo >= hi) hi = (byte)(lo + 1);
            double range = hi - lo;
            var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int pw = conv.PixelWidth;
            int pixH2 = conv.PixelHeight;
            int stride = pw * 4;
            byte[] pix = new byte[pixH2 * stride];
            conv.CopyPixels(pix, stride, 0);

            var lut = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                double m = (i - lo) / range * 255.0;
                lut[i] = (byte)(m < 0 ? 0 : m > 255 ? 255 : m);
            }

            for (int i = 0; i < pix.Length; i += 4)
            {
                pix[i] = lut[pix[i]];
                pix[i + 1] = lut[pix[i + 1]];
                pix[i + 2] = lut[pix[i + 2]];
            }

            var wb = new WriteableBitmap(pw, pixH2, src.DpiX, src.DpiY, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, pw, pixH2), pix, stride, 0);
            wb.Freeze();
            return wb;
        }

        /// <summary>
        /// Draws text so that the ink bounding box (BuildGeometry) is exactly centred on
        /// (centerX, centerY). Uses MakeTF so rendering is identical to all other text.
        /// </summary>
        private static void DrawExactCenter(DrawingContext dc, string text,
                                            double centerX, double centerY,
                                            double fontSize, double dpi, bool bold = true)
        {
            if (string.IsNullOrEmpty(text)) return;

            // Use MakeTF — SAME object for both geometry measurement AND drawing
            var ft = MakeTF(text, fontSize, dpi, Brushes.Black, bold: bold);

            Geometry geom = ft.BuildGeometry(new Point(0, 0));
            Rect bounds = geom.Bounds;
            if (bounds.IsEmpty) return;

            double x = centerX - (bounds.Left + bounds.Width / 2.0);
            double y = centerY - (bounds.Top + bounds.Height / 2.0);
            dc.DrawText(ft, new Point(x, y));
        }

        private static FormattedText MakeTF(string text, double em, double dpi, Brush brush,
                                            bool bold = false, bool italic = false)
        {
            var tf = new Typeface(
                new FontFamily("Arial"),
                italic ? FontStyles.Italic : FontStyles.Normal,
                bold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal);

            return new FormattedText(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                tf,
                Math.Max(1.0, em),
                brush,
                dpi / 96.0);
        }

        private static BitmapImage LoadBmp(string path)
        {
            var b = new BitmapImage();
            b.BeginInit();
            b.UriSource = new Uri(path, UriKind.Absolute);
            b.CacheOption = BitmapCacheOption.OnLoad;
            b.EndInit();
            b.Freeze();
            return b;
        }

        private static string PromptLabel(string message, string defaultValue)
        {
            // Simple inline dialog window
            var win = new Window
            {
                Title = "Add Label",
                Width = 320,
                Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Owner = Application.Current.MainWindow
            };
            var sp = new StackPanel { Margin = new Thickness(16) };
            var lbl = new TextBlock { Text = message, FontFamily = new FontFamily("Arial"), FontSize = 12, Margin = new Thickness(0, 0, 0, 8) };
            var tb = new TextBox { Text = defaultValue, FontFamily = new FontFamily("Arial"), FontSize = 13, Padding = new Thickness(4), Margin = new Thickness(0, 0, 0, 10) };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "OK", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
            var can = new Button { Content = "Cancel", Width = 70 };
            string result = null;
            ok.Click += (s, e) => { result = tb.Text; win.DialogResult = true; };
            can.Click += (s, e) => { win.DialogResult = false; };
            tb.KeyDown += (s, e) => { if (e.Key == Key.Return) { result = tb.Text; win.DialogResult = true; } };
            row.Children.Add(ok); row.Children.Add(can);
            sp.Children.Add(lbl); sp.Children.Add(tb); sp.Children.Add(row);
            win.Content = sp;
            tb.SelectAll();
            tb.Focus();
            return (win.ShowDialog() == true) ? result : null;
        }

        private static double ParseD(string s, double fb)
        {
            double v;
            return (double.TryParse(s, out v) && v > 0) ? v : fb;
        }

        private static int ParseI(string s, int fb)
        {
            int v;
            return (int.TryParse(s, out v) && v > 0) ? v : fb;
        }
    }
}