using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BioSAK.Services
{
    /// <summary>
    /// TCGA Gene Helper - 為 TCGA Analysis 提供基因 ID 轉換功能
    /// 支援輸入：Symbol, Ensembl ID, Entrez ID, HGNC ID
    /// 自動轉換為 Gene Symbol 以匹配 TCGA 資料
    /// </summary>
    public class TcgaGeneHelper
    {
        private readonly GeneIdService _geneService;
        private bool _isInitialized = false;

        public TcgaGeneHelper()
        {
            _geneService = new GeneIdService();
        }

        /// <summary>
        /// 初始化基因資料庫 (Human)
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            if (_isInitialized && _geneService.IsDatabaseLoaded)
                return true;

            _isInitialized = await _geneService.LoadDatabaseAsync("human");
            return _isInitialized;
        }

        /// <summary>
        /// 檢查資料庫是否已載入
        /// </summary>
        public bool IsReady => _geneService.IsDatabaseLoaded;

        /// <summary>
        /// 將任意 ID 轉換為 Gene Symbol
        /// 自動偵測輸入類型
        /// </summary>
        /// <param name="geneId">輸入的基因 ID (Symbol, Ensembl, Entrez, 或 HGNC)</param>
        /// <returns>Gene Symbol，找不到則返回原始輸入</returns>
        public string ToSymbol(string geneId)
        {
            if (string.IsNullOrWhiteSpace(geneId))
                return geneId;

            var symbol = _geneService.ConvertSingleToSymbol(geneId.Trim());
            return symbol ?? geneId.Trim();
        }

        /// <summary>
        /// 批次轉換多個 ID 為 Gene Symbol
        /// </summary>
        /// <param name="geneIds">輸入的 ID 列表</param>
        /// <returns>轉換結果字典 (原始ID -> Symbol)</returns>
        public Dictionary<string, string> ToSymbols(IEnumerable<string> geneIds)
        {
            return _geneService.ConvertToSymbols(geneIds);
        }

        /// <summary>
        /// 解析使用者輸入的基因清單
        /// 支援多種分隔符號：換行、逗號、Tab、分號
        /// 自動轉換為 Symbol
        /// </summary>
        /// <param name="input">使用者輸入的文字</param>
        /// <returns>Gene Symbol 列表</returns>
        public List<string> ParseAndConvert(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            var separators = new[] { '\n', '\r', ',', '\t', ';', ' ' };
            var ids = input.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                          .Select(g => g.Trim())
                          .Where(g => !string.IsNullOrEmpty(g))
                          .Distinct()
                          .ToList();

            var symbols = new List<string>();
            foreach (var id in ids)
            {
                var symbol = ToSymbol(id);
                if (!string.IsNullOrEmpty(symbol))
                    symbols.Add(symbol);
            }

            return symbols.Distinct().ToList();
        }

        /// <summary>
        /// 解析並轉換，同時返回轉換報告
        /// </summary>
        public (List<string> symbols, ConversionReport report) ParseAndConvertWithReport(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return (new List<string>(), new ConversionReport());

            var separators = new[] { '\n', '\r', ',', '\t', ';' };
            var ids = input.Split(separators, StringSplitOptions.RemoveEmptyEntries)
                          .Select(g => g.Trim())
                          .Where(g => !string.IsNullOrEmpty(g))
                          .Distinct()
                          .ToList();

            var report = new ConversionReport
            {
                TotalInput = ids.Count
            };

            var symbols = new List<string>();
            foreach (var id in ids)
            {
                var detectedType = _geneService.DetectIdType(id);
                var symbol = _geneService.ConvertSingleToSymbol(id);

                if (symbol != null)
                {
                    symbols.Add(symbol);
                    report.Converted++;

                    // 統計輸入類型
                    if (!report.InputTypes.ContainsKey(detectedType))
                        report.InputTypes[detectedType] = 0;
                    report.InputTypes[detectedType]++;
                }
                else
                {
                    // 找不到的保留原始值
                    symbols.Add(id);
                    report.NotFound.Add(id);
                }
            }

            return (symbols.Distinct().ToList(), report);
        }

        /// <summary>
        /// 驗證基因是否存在於資料庫中
        /// </summary>
        public bool ValidateGene(string geneId)
        {
            if (string.IsNullOrWhiteSpace(geneId))
                return false;

            var symbol = _geneService.ConvertSingleToSymbol(geneId.Trim());
            return symbol != null;
        }

        /// <summary>
        /// 將任意 ID 轉換為 Ensembl ID（去除版本號）
        /// </summary>
        public string ToEnsemblId(string geneId)
        {
            if (string.IsNullOrWhiteSpace(geneId)) return null;
            var entry = GetGeneInfo(geneId.Trim());
            if (string.IsNullOrEmpty(entry?.EnsemblId)) return null;
            return entry.EnsemblId.Split('.')[0];
        }


        public string ResolveForTcga(string geneId)
        {
            if (string.IsNullOrWhiteSpace(geneId)) return geneId;
            var trimmed = geneId.Trim();
            // 已是 ENSG，直接去除版本號
            if (trimmed.StartsWith("ENSG", StringComparison.OrdinalIgnoreCase))
                return trimmed.Split('.')[0];
            if (!_isInitialized || !_geneService.IsDatabaseLoaded)
                return trimmed;
            var ensemblId = ToEnsemblId(trimmed);
            return ensemblId ?? trimmed;
        }

        /// <summary>
        /// 批次解析 TCGA 查詢用 ID 列表（轉為 Ensembl ID）
        /// </summary>
        public List<string> ResolveListForTcga(IEnumerable<string> geneIds)
        {
            return geneIds
                .Select(g => ResolveForTcga(g))
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// 取得基因的完整資訊
        /// </summary>
        public GeneEntry GetGeneInfo(string geneId)
        {
            if (string.IsNullOrWhiteSpace(geneId))
                return null;

            var inputType = _geneService.DetectIdType(geneId.Trim());
            var matches = _geneService.Convert(geneId.Trim(), inputType);
            return matches.FirstOrDefault();
        }
    }

    /// <summary>
    /// 轉換報告
    /// </summary>
    public class ConversionReport
    {
        public int TotalInput { get; set; }
        public int Converted { get; set; }
        public List<string> NotFound { get; set; } = new List<string>();
        public Dictionary<string, int> InputTypes { get; set; } = new Dictionary<string, int>();

        public int NotFoundCount => NotFound.Count;
        public double SuccessRate => TotalInput > 0 ? (double)Converted / TotalInput * 100 : 0;

        public string GetSummary()
        {
            var typeInfo = string.Join(", ", InputTypes.Select(kv => $"{kv.Key}: {kv.Value}"));
            return $"Converted: {Converted}/{TotalInput} ({SuccessRate:F1}%) | Types: {typeInfo}";
        }
    }
}