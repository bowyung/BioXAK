using System;
using System.Collections.Generic;

namespace BioSAK
{
    /// <summary>
    /// Root container for .xak v2.0 file format.
    /// Supports GraphGen, TCGA Analysis, and Western Blot Organizer.
    /// </summary>
    public class XakDocument
    {
        public string Version { get; set; } = "2.0";
        public string Module { get; set; } = "";  // "GraphGen" | "TcgaAnalysis" | "WesternBlot"
        public string SavedAt { get; set; } = "";
        public string Description { get; set; } = "";

        // Module-specific data (only one will be non-null)
        public GraphGenData? GraphGen { get; set; }
        public TcgaData? TcgaAnalysis { get; set; }
        public WesternBlotData? WesternBlot { get; set; }
        public FlowCytometryData? FlowCytometry { get; set; }
        // Shared chart settings (applicable to GraphGen and TCGA)
        public List<ChartSnapshot> Charts { get; set; } = new();
    }

    // ════════════════════════════════════════════════════════════
    //  GRAPH GEN MODULE
    // ════════════════════════════════════════════════════════════

    public class GraphGenData
    {
        public string ChartType { get; set; } = "XY";
        public int YColumns { get; set; } = 3;
        public int YDataMode { get; set; } = 0;
        public int YReplicates { get; set; } = 3;
        public bool XHasRepeat { get; set; } = false;
        public int XReplicates { get; set; } = 1;
        public Dictionary<string, string> ColumnTitles { get; set; } = new();
        public List<Dictionary<string, string>> Data { get; set; } = new();
    }

    // ════════════════════════════════════════════════════════════
    //  TCGA ANALYSIS MODULE
    //  Field names must match actual TcgaAnalysisPage.xaml.cs types:
    //  - BoxPlotStatsRow: CancerCode, GeneName, TumorMean, NormalMean, PValue, TumorN, NormalN
    //  - ScatterDataPoint: X, Y
    //  - KMComparisonResult: LogRankPValue, HazardRatio, HighExpression.Curve, LowExpression.Curve
    //  - VolcanoPointViewModel: GeneId, GeneName, Log2FoldChange, PValue, FDR, IsHighlighted
    //  - CoExprDisplayRow: GeneId, GeneName, PearsonR, PValue, FDR
    // ════════════════════════════════════════════════════════════

    public class TcgaData
    {
        // Query parameters
        public string GeneSymbol { get; set; } = "";
        public List<string> SelectedCancerTypes { get; set; } = new();
        public int ActiveTabIndex { get; set; } = 0;
        public bool IsMultiSelect { get; set; } = false;
        public string Condition { get; set; } = "Both";
        public List<string> HeatmapGenes { get; set; } = new();
        public List<int> AnalyzedTabs { get; set; } = new();

        // Per-tab settings & cache
        public TcgaBoxPlotCache? BoxPlotCache { get; set; }
        public TcgaScatterSettings Scatter { get; set; } = new();
        public TcgaScatterCache? ScatterCache { get; set; }
        public TcgaKMSettings KaplanMeier { get; set; } = new();
        public TcgaKMCache? KMCache { get; set; }
        public TcgaVolcanoSettings Volcano { get; set; } = new();
        public TcgaVolcanoCache? VolcanoCache { get; set; }
        public TcgaCoExprSettings CoExpression { get; set; } = new();
        public TcgaCoExprCache? CoExprCache { get; set; }
    }

    public class TcgaBoxPlotCache
    {
        public List<TcgaBoxPlotRow> Rows { get; set; } = new();
    }

    public class TcgaBoxPlotRow
    {
        public string CancerCode { get; set; } = "";
        public string GeneName { get; set; } = "";
        public double TumorMean { get; set; }
        public double NormalMean { get; set; }
        public double PValue { get; set; }
        public int TumorN { get; set; }
        public int NormalN { get; set; }
    }

    public class TcgaScatterSettings
    {
        public string GeneX { get; set; } = "";
        public string GeneY { get; set; } = "";
        public string CancerType { get; set; } = "";
    }

    public class TcgaScatterCache
    {
        public List<double> XValues { get; set; } = new();
        public List<double> YValues { get; set; } = new();
        public double R { get; set; }
        public double R2 { get; set; }
        public double PValue { get; set; }
        public double Slope { get; set; }
        public double Intercept { get; set; }
        public int N { get; set; }
    }

    public class TcgaKMSettings
    {
        public string GeneId { get; set; } = "";
        public string CancerType { get; set; } = "";
        public string CutoffType { get; set; } = "Median";
        public double Percentile { get; set; } = 50;
    }
    public class TcgaKMCache
    {
        public double LogRankP { get; set; }
        public double LogRankChiSq { get; set; }
        public List<TcgaKMPoint> HighCurve { get; set; } = new();
        public List<TcgaKMPoint> LowCurve { get; set; } = new();
    }

    public class TcgaKMPoint
    {
        public double Time { get; set; }
        public double Survival { get; set; }
        public int AtRisk { get; set; }
    }

    public class TcgaVolcanoSettings
    {
        public string CancerType { get; set; } = "";
        public double FdrThreshold { get; set; } = 0.05;
        public double FcThreshold { get; set; } = 1.0;
        public List<string> HighlightedGenes { get; set; } = new();
    }

    public class TcgaVolcanoCache
    {
        public List<TcgaVolcanoPoint> Points { get; set; } = new();
    }

    public class TcgaVolcanoPoint
    {
        public string GeneId { get; set; } = "";
        public string GeneName { get; set; } = "";
        public double Log2FC { get; set; }
        public double PValue { get; set; }
        public double FDR { get; set; }
    }

    public class TcgaCoExprSettings
    {
        public string TargetGene { get; set; } = "";
        public string CancerType { get; set; } = "";
        public double FdrThreshold { get; set; } = 0.05;
        public double MinAbsR { get; set; } = 0.3;
    }

    public class TcgaCoExprCache
    {
        public List<TcgaCoExprRow> Results { get; set; } = new();
    }

    public class TcgaCoExprRow
    {
        public string GeneId { get; set; } = "";
        public string GeneName { get; set; } = "";
        public double PearsonR { get; set; }  // matches CoExprDisplayRow.PearsonR
        public double PValue { get; set; }
        public double FDR { get; set; }
    }

    // ════════════════════════════════════════════════════════════
    //  WESTERN BLOT MODULE
    //  Classes in WesternBlotOrganizer.xaml.cs (namespace BioSAK.Pages):
    //  - BlotEntry: Name, Prefix, Image (BitmapImage), Index, FilePath
    //  - ConditionRow: RowLabel (NOT Label), Cells (ObservableCollection<ConditionCell>), PatternCycle
    //  - ConditionCell: Value
    //  - TopLabelEntry: Text, StartLane, EndLane
    //  - MarkerEntry: Label, YFraction
    //  - WesternBlotViewModel: Blots, ConditionRows, Markers, TopLabels, LaneXFractions, LeftBoundFrac, RightBoundFrac
    //  - No SetColumns() method on ConditionRow; use page's SyncCells(row, count) instead
    // ════════════════════════════════════════════════════════════

    public class WesternBlotData
    {
        public double BlotWidth { get; set; } = 480;
        public double LabelWidth { get; set; } = 130;
        public double RowSpacing { get; set; } = 6;
        public double FontSize { get; set; } = 13;
        public int LaneCount { get; set; } = 4;

        public List<WBBlotEntry> Blots { get; set; } = new();
        public List<double> LaneXFractions { get; set; } = new();
        public double? LeftBoundFrac { get; set; }
        public double? RightBoundFrac { get; set; }

        public List<WBConditionRow> ConditionRows { get; set; } = new();
        public List<WBTopLabel> TopLabels { get; set; } = new();
        public List<WBMarker> Markers { get; set; } = new();
        public WBEditorState? EditorState { get; set; }
    }

    public class WBBlotEntry
    {
        public string Name { get; set; } = "";
        public string Prefix { get; set; } = "α";
        public int Index { get; set; }
        public string OriginalPath { get; set; } = "";
        public string ImageBase64 { get; set; } = "";
    }

    public class WBConditionRow
    {
        public string RowLabel { get; set; } = "";  // matches ConditionRow.RowLabel
        public List<string> CellValues { get; set; } = new();
        public int PatternCycle { get; set; } = 0;
    }

    public class WBTopLabel
    {
        public string Text { get; set; } = "";
        public int StartLane { get; set; } = 1;
        public int EndLane { get; set; } = 2;
    }

    public class WBMarker
    {
        public string Label { get; set; } = "";
        public double YFraction { get; set; } = 0.5;
    }

    public class WBEditorState
    {
        public double Rotation { get; set; }
        public double CropL { get; set; }
        public double CropR { get; set; } = 1;
        public double CropT { get; set; }
        public double CropB { get; set; } = 1;
        public byte LevelMin { get; set; }
        public byte LevelMax { get; set; } = 255;
        public bool Grayscale { get; set; }
    }

    // ════════════════════════════════════════════════════════════
    //  SHARED CHART SNAPSHOT
    // ════════════════════════════════════════════════════════════

    public class ChartSnapshot
    {
        public string ChartId { get; set; } = "";
        public string ChartType { get; set; } = "Scatter";
        public string Title { get; set; } = "";
        public string XAxisTitle { get; set; } = "";
        public string YAxisTitle { get; set; } = "";
        public string ErrorType { get; set; } = "None";
        public string ErrorDirection { get; set; } = "Both";
        public AxisSettings XAxis { get; set; } = new();
        public AxisSettings YAxis { get; set; } = new();
        public bool ShowRegression { get; set; } = false;
        public int RegressionDegree { get; set; } = 1;
        public bool ShowRegressionEquation { get; set; } = true;
        public bool ShowConnectLines { get; set; } = false;
        public List<SeriesStyleSnapshot> SeriesStyles { get; set; } = new();
        public List<BarStyleSnapshot> BarStyles { get; set; } = new();
    }

    public class AxisSettings
    {
        public bool Auto { get; set; } = true;
        public double? Min { get; set; }
        public double? Max { get; set; }
        public double? Interval { get; set; }
        public bool LogScale { get; set; } = false;
        public double LogBase { get; set; } = 10;
    }

    public class SeriesStyleSnapshot
    {
        public string Name { get; set; } = "";
        public string Color { get; set; } = "#4285F4";
        public double LineThickness { get; set; } = 2;
        public int MarkerSize { get; set; } = 8;
        public string MarkerShape { get; set; } = "Circle";
    }

    public class BarStyleSnapshot
    {
        public int SeriesIndex { get; set; }
        public int XIndex { get; set; }
        public string FillColor { get; set; } = "#4285F4";
        public string BorderColor { get; set; } = "#000000";
        public double BorderThickness { get; set; } = 1;
        public string Pattern { get; set; } = "Solid";
    }
    // ════════════════════════════════════════════════════════════
    //  FLOW CYTOMETRY MODULE
    // ════════════════════════════════════════════════════════════

    public class FlowCytometryData
    {
        // File references
        public List<FlowFcsFileEntry> Files { get; set; } = new();
        public int SelectedFileIndex { get; set; } = 0;
        public int OverlayFileIndex { get; set; } = -1;
        public bool OverlayEnabled { get; set; } = false;

        // View state
        public string CurrentView { get; set; } = "scatter";
        public string PlotType { get; set; } = "dot";

        // Scatter parameters (by name, not index)
        public string XParamName { get; set; } = "";
        public string YParamName { get; set; } = "";
        public string XScaleMode { get; set; } = "Log";
        public string YScaleMode { get; set; } = "Log";

        // Histogram parameters
        public string HistParamName { get; set; } = "";
        public bool HistLogScale { get; set; } = true;

        // Compensation
        public bool ApplyCompensation { get; set; } = false;
        public string GlobalCompSourceFilename { get; set; } = "";
        public FlowCompensationOverride? CompensationOverride { get; set; }

        // Colormaps
        public string DotColormap { get; set; } = "Turbo";
        public string ContourColormap { get; set; } = "YlOrRd";
        public string HistColor { get; set; } = "Blue";

        // Axis range
        public double? CustomXMin { get; set; }
        public double? CustomXMax { get; set; }
        public double? CustomYMin { get; set; }
        public double? CustomYMax { get; set; }

        // Parent gate selections (by name)
        public string ScatterParentGateName { get; set; } = "";
        public string HistParentGateName { get; set; } = "";

        // Gate templates
        public List<FlowGateData> Gates { get; set; } = new();

        // Statistics records
        public List<FlowStatsRecord> Stats { get; set; } = new();
    }

    /// <summary>
    /// Stores both file path (for full reload) and embedded subsampled data (fallback).
    /// </summary>
    public class FlowFcsFileEntry
    {
        public string FilePath { get; set; } = "";
        public string Filename { get; set; } = "";

        // Embedded subsampled data (for portability when original file is missing)
        public List<FlowFcsParam> Parameters { get; set; } = new();
        public int OriginalEventCount { get; set; } = 0;
        public int EmbeddedEventCount { get; set; } = 0;
        /// <summary>Base64-encoded Deflate-compressed float[] (row-major: events × params)</summary>
        public string? CompressedEvents { get; set; }
    }

    public class FlowFcsParam
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public double Range { get; set; } = 262144;
    }

    public class FlowGateData
    {
        public string Name { get; set; } = "";
        public string GateType { get; set; } = "Polygon";
        public List<double[]> Points { get; set; } = new();
        public string XParamName { get; set; } = "";
        public string YParamName { get; set; } = "";
        public string ParentGateName { get; set; } = "";
        public double RangeMin { get; set; }
        public double RangeMax { get; set; }
    }

    public class FlowStatsRecord
    {
        public string SampleName { get; set; } = "";
        public string ViewType { get; set; } = "";
        public string Parameters { get; set; } = "";
        public string GateRegion { get; set; } = "";
        public string Count { get; set; } = "";
        public string Percentage { get; set; } = "";
        public string ParentGate { get; set; } = "";
        public string Details { get; set; } = "";
    }

    public class FlowCompensationOverride
    {
        public List<string> Channels { get; set; } = new();
        /// <summary>Row-major spillover matrix values [n][n]</summary>
        public List<List<float>> Matrix { get; set; } = new();
    }
}
