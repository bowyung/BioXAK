using System;

namespace BioSAK.Services
{
    /// <summary>
    /// Box Plot 統計資料行
    /// </summary>
    public class BoxPlotStatsRow
    {
        public string CancerCode { get; set; }
        public string GeneId { get; set; }
        public string GeneName { get; set; }

        // Tumor statistics
        public int TumorN { get; set; }
        public double TumorMean { get; set; }
        public double TumorSd { get; set; }
        public double TumorMedian { get; set; }
        public double TumorQ1 { get; set; }
        public double TumorQ3 { get; set; }

        // Normal statistics
        public int NormalN { get; set; }
        public double NormalMean { get; set; }
        public double NormalSd { get; set; }
        public double NormalMedian { get; set; }
        public double NormalQ1 { get; set; }
        public double NormalQ3 { get; set; }

        // Statistical test results
        public double PValue { get; set; }
        public double FDR { get; set; }

        // Display formatters
        public string PValueDisplay => PValue < 0.001 ? $"{PValue:E2}" : $"{PValue:F4}";
        public string FdrDisplay => FDR < 0.001 ? $"{FDR:E2}" : $"{FDR:F4}";

        // Fold change
        public double Log2FoldChange => TumorMean - NormalMean;
        public string Regulation => Log2FoldChange > 0 ? "Up" : "Down";
    }

    /// <summary>
    /// Scatter Plot 資料點
    /// (已在 TcgaAnalysisPage.xaml.cs 中定義，此處為備份)
    /// </summary>
    public class ScatterPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
        public string Condition { get; set; }
        public string SampleId { get; set; }
    }

    /// <summary>
    /// 基因搜尋結果
    /// </summary>
    public class GeneSearchResult
    {
        public string GeneId { get; set; }
        public string GeneName { get; set; }
        public string Description { get; set; }
        public bool Found { get; set; }
        public string OriginalQuery { get; set; }
    }

    /// <summary>
    /// 分析進度報告
    /// </summary>
    public class AnalysisProgress
    {
        public int Percentage { get; set; }
        public string Message { get; set; }
        public string Stage { get; set; }
        public bool IsComplete { get; set; }
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 匯出設定
    /// </summary>
    public class ExportSettings
    {
        public string FilePath { get; set; }
        public string FileFormat { get; set; }  // "PNG", "CSV", "TSV", "XLSX"
        public int ImageWidth { get; set; } = 800;
        public int ImageHeight { get; set; } = 600;
        public int DPI { get; set; } = 96;
        public bool IncludeHeader { get; set; } = true;
        public string Delimiter { get; set; } = ",";
    }
}
