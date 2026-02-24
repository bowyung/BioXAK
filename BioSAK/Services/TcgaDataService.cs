using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BioSAK.Services
{
    /// <summary>
    /// TCGA 資料服務 - 二進位格式版本 (含生存資訊)
    /// </summary>
    public class TcgaDataService
    {
        private readonly string _dataPath;
        private List<TcgaProjectIndex> _projectIndex;
        private readonly Dictionary<string, TcgaProjectData> _dataCache = new();

        public TcgaDataService()
        {
            _dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "TCGA");
        }

        public TcgaDataService(string dataPath)
        {
            _dataPath = dataPath;
        }

        public bool IsDataAvailable()
        {
            return Directory.Exists(_dataPath) &&
                   File.Exists(Path.Combine(_dataPath, "projects_index.json"));
        }

        #region 專案索引

        public async Task<List<TcgaProjectIndex>> GetProjectIndexAsync()
        {
            if (_projectIndex != null) return _projectIndex;

            var indexPath = Path.Combine(_dataPath, "projects_index.json");
            if (!File.Exists(indexPath))
                throw new FileNotFoundException("projects_index.json not found");

            var json = await File.ReadAllTextAsync(indexPath);
            _projectIndex = JsonSerializer.Deserialize<List<TcgaProjectIndex>>(json);
            return _projectIndex;
        }

        #endregion

        #region 載入專案資料

        private async Task<TcgaProjectData> LoadProjectDataAsync(string projectId)
        {
            if (_dataCache.TryGetValue(projectId, out var cached))
                return cached;

            var metaPath = Path.Combine(_dataPath, $"{projectId}_meta.json");
            var matrixPath = Path.Combine(_dataPath, $"{projectId}_matrix.bin");

            if (!File.Exists(metaPath) || !File.Exists(matrixPath))
                return null;

            var metaJson = await File.ReadAllTextAsync(metaPath);
            var meta = JsonSerializer.Deserialize<TcgaMetadata>(metaJson);

            var matrixBytes = await File.ReadAllBytesAsync(matrixPath);
            int totalValues = meta.n_genes * meta.n_samples;
            var matrix = new float[totalValues];
            Buffer.BlockCopy(matrixBytes, 0, matrix, 0, matrixBytes.Length);

            var geneIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < meta.gene_ids.Length; i++)
            {
                var geneIdClean = meta.gene_ids[i].Split('.')[0];
                if (!geneIndex.ContainsKey(geneIdClean))
                    geneIndex[geneIdClean] = i;

                if (meta.gene_names != null && i < meta.gene_names.Length &&
                    !string.IsNullOrEmpty(meta.gene_names[i]))
                {
                    var geneName = meta.gene_names[i];
                    if (!geneIndex.ContainsKey(geneName))
                        geneIndex[geneName] = i;
                }
            }

            var tumorIndices = new List<int>();
            var normalIndices = new List<int>();
            for (int i = 0; i < meta.sample_conditions.Length; i++)
            {
                if (meta.sample_conditions[i] == "Tumor")
                    tumorIndices.Add(i);
                else if (meta.sample_conditions[i] == "Normal")
                    normalIndices.Add(i);
            }

            var projectData = new TcgaProjectData
            {
                Meta = meta,
                Matrix = matrix,
                GeneIndex = geneIndex,
                TumorIndices = tumorIndices.ToArray(),
                NormalIndices = normalIndices.ToArray()
            };

            _dataCache[projectId] = projectData;
            return projectData;
        }

        #endregion

        #region 基因表達查詢

        public async Task<GeneExpressionResult> GetGeneExpressionAsync(string projectId, string geneQuery)
        {
            var data = await LoadProjectDataAsync(projectId);
            if (data == null) return null;

            var queryClean = geneQuery.ToUpper().Split('.')[0];
            if (!data.GeneIndex.TryGetValue(queryClean, out int geneIdx))
            {
                if (!data.GeneIndex.TryGetValue(geneQuery, out geneIdx))
                    return null;
            }

            var meta = data.Meta;
            int nSamples = meta.n_samples;
            int rowStart = geneIdx * nSamples;

            var result = new GeneExpressionResult
            {
                GeneId = meta.gene_ids[geneIdx],
                GeneName = meta.gene_names?[geneIdx],
                TumorValues = new List<double>(data.TumorIndices.Length),
                NormalValues = new List<double>(data.NormalIndices.Length),
                TumorSampleIds = new List<string>(data.TumorIndices.Length),
                NormalSampleIds = new List<string>(data.NormalIndices.Length)
            };

            foreach (int sampleIdx in data.TumorIndices)
            {
                result.TumorValues.Add(data.Matrix[rowStart + sampleIdx]);
                result.TumorSampleIds.Add(meta.sample_ids[sampleIdx]);
            }

            foreach (int sampleIdx in data.NormalIndices)
            {
                result.NormalValues.Add(data.Matrix[rowStart + sampleIdx]);
                result.NormalSampleIds.Add(meta.sample_ids[sampleIdx]);
            }

            return result;
        }

        public async Task<TwoGeneExpressionResult> GetTwoGeneExpressionAsync(
            string projectId, string gene1, string gene2)
        {
            var data = await LoadProjectDataAsync(projectId);
            if (data == null) return null;

            if (!data.GeneIndex.TryGetValue(gene1.ToUpper().Split('.')[0], out int idx1) &&
                !data.GeneIndex.TryGetValue(gene1, out idx1))
                return null;

            if (!data.GeneIndex.TryGetValue(gene2.ToUpper().Split('.')[0], out int idx2) &&
                !data.GeneIndex.TryGetValue(gene2, out idx2))
                return null;

            var meta = data.Meta;
            int nSamples = meta.n_samples;
            int row1Start = idx1 * nSamples;
            int row2Start = idx2 * nSamples;

            var result = new TwoGeneExpressionResult
            {
                Gene1Id = meta.gene_ids[idx1],
                Gene1Name = meta.gene_names?[idx1],
                Gene2Id = meta.gene_ids[idx2],
                Gene2Name = meta.gene_names?[idx2],
                TumorPairs = new List<(double X, double Y, string SampleId)>(data.TumorIndices.Length),
                NormalPairs = new List<(double X, double Y, string SampleId)>(data.NormalIndices.Length)
            };

            foreach (int sampleIdx in data.TumorIndices)
            {
                result.TumorPairs.Add((
                    data.Matrix[row1Start + sampleIdx],
                    data.Matrix[row2Start + sampleIdx],
                    meta.sample_ids[sampleIdx]
                ));
            }

            foreach (int sampleIdx in data.NormalIndices)
            {
                result.NormalPairs.Add((
                    data.Matrix[row1Start + sampleIdx],
                    data.Matrix[row2Start + sampleIdx],
                    meta.sample_ids[sampleIdx]
                ));
            }

            return result;
        }

        public async Task<MultiGeneExpressionResult> GetMultiGeneExpressionAsync(
            string projectId, List<string> geneQueries, string condition = "Both")
        {
            var data = await LoadProjectDataAsync(projectId);
            if (data == null) return null;

            var meta = data.Meta;
            int nSamples = meta.n_samples;

            int[] sampleIndices = condition switch
            {
                "Tumor" => data.TumorIndices,
                "Normal" => data.NormalIndices,
                _ => data.TumorIndices.Concat(data.NormalIndices).ToArray()
            };

            var result = new MultiGeneExpressionResult
            {
                GeneIds = new List<string>(),
                GeneNames = new List<string>(),
                Expressions = new List<List<double>>()
            };

            foreach (var query in geneQueries)
            {
                var q = query.ToUpper().Split('.')[0];
                if (!data.GeneIndex.TryGetValue(q, out int geneIdx) &&
                    !data.GeneIndex.TryGetValue(query, out geneIdx))
                    continue;

                result.GeneIds.Add(meta.gene_ids[geneIdx]);
                result.GeneNames.Add(meta.gene_names?[geneIdx] ?? meta.gene_ids[geneIdx]);

                int rowStart = geneIdx * nSamples;
                var values = new List<double>(sampleIndices.Length);
                foreach (int sampleIdx in sampleIndices)
                {
                    values.Add(Math.Log2(data.Matrix[rowStart + sampleIdx] + 1));
                }
                result.Expressions.Add(values);
            }

            return result;
        }

        #endregion

        #region 生存分析相關方法

        public async Task<bool> HasSurvivalDataAsync(string projectId)
        {
            var data = await LoadProjectDataAsync(projectId);
            if (data?.Meta?.vital_status == null) return false;

            int validCount = data.Meta.vital_status.Count(s => !string.IsNullOrEmpty(s));
            return validCount > 10;
        }

        public async Task<GeneSurvivalResult> GetGeneSurvivalDataAsync(string projectId, string geneQuery)
        {
            var data = await LoadProjectDataAsync(projectId);
            if (data == null) return null;

            var meta = data.Meta;
            if (meta.vital_status == null || meta.days_to_death == null || meta.days_to_last_follow_up == null)
                return null;

            var queryClean = geneQuery.ToUpper().Split('.')[0];
            if (!data.GeneIndex.TryGetValue(queryClean, out int geneIdx))
            {
                if (!data.GeneIndex.TryGetValue(geneQuery, out geneIdx))
                    return null;
            }

            int nSamples = meta.n_samples;
            int rowStart = geneIdx * nSamples;

            var result = new GeneSurvivalResult
            {
                GeneId = meta.gene_ids[geneIdx],
                GeneName = meta.gene_names?[geneIdx] ?? meta.gene_ids[geneIdx],
                Samples = new List<SurvivalSample>()
            };

            foreach (int sampleIdx in data.TumorIndices)
            {
                var vitalStatus = meta.vital_status[sampleIdx];
                if (string.IsNullOrEmpty(vitalStatus)) continue;

                bool isDead = vitalStatus.Equals("Dead", StringComparison.OrdinalIgnoreCase);
                int? survivalDays = null;

                if (isDead && meta.days_to_death[sampleIdx].HasValue)
                    survivalDays = meta.days_to_death[sampleIdx].Value;
                else if (!isDead && meta.days_to_last_follow_up[sampleIdx].HasValue)
                    survivalDays = meta.days_to_last_follow_up[sampleIdx].Value;

                if (!survivalDays.HasValue || survivalDays.Value <= 0) continue;

                result.Samples.Add(new SurvivalSample
                {
                    SampleId = meta.sample_ids[sampleIdx],
                    Expression = data.Matrix[rowStart + sampleIdx],
                    SurvivalDays = survivalDays.Value,
                    IsEvent = isDead,
                    VitalStatus = vitalStatus
                });
            }

            return result;
        }

        public async Task<VolcanoPlotResult> GetVolcanoDataAsync(string projectId, IProgress<int> progress = null)
        {
            var data = await LoadProjectDataAsync(projectId);
            if (data == null) return null;

            var meta = data.Meta;
            int nSamples = meta.n_samples;
            int nGenes = meta.n_genes;

            if (data.TumorIndices.Length < 3 || data.NormalIndices.Length < 3)
                return null;

            var result = new VolcanoPlotResult
            {
                ProjectId = projectId,
                Points = new List<VolcanoPoint>(nGenes)
            };

            for (int geneIdx = 0; geneIdx < nGenes; geneIdx++)
            {
                if (progress != null && geneIdx % 1000 == 0)
                    progress.Report((geneIdx * 100) / nGenes);

                int rowStart = geneIdx * nSamples;

                var tumorVals = new List<double>(data.TumorIndices.Length);
                var normalVals = new List<double>(data.NormalIndices.Length);

                foreach (int i in data.TumorIndices)
                    tumorVals.Add(Math.Log2(data.Matrix[rowStart + i] + 1));
                foreach (int i in data.NormalIndices)
                    normalVals.Add(Math.Log2(data.Matrix[rowStart + i] + 1));

                double tumorMean = tumorVals.Average();
                double normalMean = normalVals.Average();
                double log2FC = tumorMean - normalMean;

                double pValue = StatisticsService.WelchTTest(tumorVals, normalVals);

                if (double.IsNaN(pValue) || double.IsInfinity(pValue) || pValue <= 0)
                    continue;

                result.Points.Add(new VolcanoPoint
                {
                    GeneId = meta.gene_ids[geneIdx],
                    GeneName = meta.gene_names?[geneIdx] ?? meta.gene_ids[geneIdx],
                    Log2FoldChange = log2FC,
                    PValue = pValue,
                    NegLog10PValue = -Math.Log10(pValue),
                    TumorMean = tumorMean,
                    NormalMean = normalMean
                });
            }

            if (result.Points.Count > 0)
            {
                var pValues = result.Points.Select(p => p.PValue).ToList();
                var fdrs = StatisticsService.BenjaminiHochbergFDR(pValues);
                for (int i = 0; i < result.Points.Count; i++)
                {
                    result.Points[i].FDR = fdrs[i];
                }
            }

            progress?.Report(100);
            return result;
        }

        #endregion

        #region Co-Expression 相關性分析

        /// <summary>
        /// 計算目標基因與該專案中所有其他基因的 Pearson 相關性 (Tumor samples only)
        /// </summary>
        public async Task<GeneCorrelationResult> GetGeneCorrelationAsync(
            string projectId, string targetGene, string condition = "Tumor",
            IProgress<int> progress = null)
        {
            var data = await LoadProjectDataAsync(projectId);
            if (data == null) return null;

            var meta = data.Meta;
            int nSamples = meta.n_samples;

            // 找到目標基因 index
            var queryClean = targetGene.ToUpper().Split('.')[0];
            if (!data.GeneIndex.TryGetValue(queryClean, out int targetIdx))
            {
                if (!data.GeneIndex.TryGetValue(targetGene, out targetIdx))
                    return null;
            }

            // 決定使用哪些樣本
            int[] sampleIndices = condition switch
            {
                "Tumor" => data.TumorIndices,
                "Normal" => data.NormalIndices,
                _ => data.TumorIndices.Concat(data.NormalIndices).ToArray()
            };

            if (sampleIndices.Length < 5)
                return null;

            // 取得目標基因的表達量（log2 轉換）
            int targetRowStart = targetIdx * nSamples;
            var targetValues = new double[sampleIndices.Length];
            for (int i = 0; i < sampleIndices.Length; i++)
                targetValues[i] = Math.Log2(data.Matrix[targetRowStart + sampleIndices[i]] + 1);

            // 預計算 target 的統計量 (避免重複計算)
            double targetMean = 0;
            for (int i = 0; i < targetValues.Length; i++)
                targetMean += targetValues[i];
            targetMean /= targetValues.Length;

            double targetSS = 0;
            var targetCentered = new double[targetValues.Length];
            for (int i = 0; i < targetValues.Length; i++)
            {
                targetCentered[i] = targetValues[i] - targetMean;
                targetSS += targetCentered[i] * targetCentered[i];
            }

            double targetStd = Math.Sqrt(targetSS);
            if (targetStd < 1e-12) return null; // 零變異量

            int nGenes = meta.n_genes;
            var points = new List<CorrelatedGene>(nGenes);
            int nSamp = sampleIndices.Length;
            int df = nSamp - 2;

            for (int geneIdx = 0; geneIdx < nGenes; geneIdx++)
            {
                if (geneIdx == targetIdx) continue;

                if (progress != null && geneIdx % 2000 == 0)
                    progress.Report((geneIdx * 100) / nGenes);

                int rowStart = geneIdx * nSamples;

                // 計算 Pearson r (手動向量化，避免 List 分配)
                double sum = 0, geneMean = 0, geneSS = 0;
                for (int i = 0; i < nSamp; i++)
                    geneMean += Math.Log2(data.Matrix[rowStart + sampleIndices[i]] + 1);
                geneMean /= nSamp;

                for (int i = 0; i < nSamp; i++)
                {
                    double diff = Math.Log2(data.Matrix[rowStart + sampleIndices[i]] + 1) - geneMean;
                    geneSS += diff * diff;
                    sum += targetCentered[i] * diff;
                }

                double geneStd = Math.Sqrt(geneSS);
                if (geneStd < 1e-12) continue;

                double r = sum / (targetStd * geneStd);

                // 限制範圍
                if (r > 1.0) r = 1.0;
                if (r < -1.0) r = -1.0;

                // T-test for correlation significance (two-tailed)
                double t = r * Math.Sqrt(df / (1.0 - r * r + 1e-300));
                // 使用正態近似計算 p-value (df 通常夠大)
                double pValue = CorrelationPValue(Math.Abs(t), df);

                if (double.IsNaN(pValue) || double.IsInfinity(pValue) || pValue <= 0)
                    continue;

                points.Add(new CorrelatedGene
                {
                    GeneId = meta.gene_ids[geneIdx],
                    GeneName = meta.gene_names?[geneIdx] ?? meta.gene_ids[geneIdx],
                    PearsonR = r,
                    PValue = pValue,
                    N = nSamp
                });
            }

            // BH FDR 校正
            if (points.Count > 0)
            {
                var pValues = points.Select(p => p.PValue).ToList();
                var fdrs = StatisticsService.BenjaminiHochbergFDR(pValues);
                for (int i = 0; i < points.Count; i++)
                    points[i].FDR = fdrs[i];
            }

            progress?.Report(100);

            return new GeneCorrelationResult
            {
                ProjectId = projectId,
                TargetGeneId = meta.gene_ids[targetIdx],
                TargetGeneName = meta.gene_names?[targetIdx] ?? meta.gene_ids[targetIdx],
                Condition = condition,
                SampleCount = nSamp,
                CorrelatedGenes = points
            };
        }

        /// <summary>
        /// 使用 Beta 分佈近似計算 t 分佈的雙尾 p-value
        /// </summary>
        private static double CorrelationPValue(double absT, int df)
        {
            if (absT <= 0) return 1.0;
            if (df <= 0) return 1.0;

            // 使用 Beta function 的正則化不完全形式
            double x = df / (df + absT * absT);
            double a = df / 2.0;
            double b = 0.5;

            // Regularized incomplete beta via continued fraction
            double bt = Math.Exp(LogGammaLocal(a + b) - LogGammaLocal(a) - LogGammaLocal(b)
                                 + a * Math.Log(x) + b * Math.Log(1 - x));

            double ibeta;
            if (x < (a + 1) / (a + b + 2))
                ibeta = bt * BetaCFLocal(x, a, b) / a;
            else
                ibeta = 1.0 - bt * BetaCFLocal(1 - x, b, a) / b;

            return ibeta; // 已經是雙尾 (因為 I(x, df/2, 1/2) 直接給出雙尾)
        }

        private static double LogGammaLocal(double x)
        {
            double[] c = { 76.18009172947146, -86.50532032941677,
                           24.01409824083091, -1.231739572450155,
                           0.001208650973866179, -0.000005395239384953 };
            double y = x, tmp = x + 5.5;
            tmp -= (x + 0.5) * Math.Log(tmp);
            double ser = 1.000000000190015;
            for (int j = 0; j < 6; j++) ser += c[j] / ++y;
            return -tmp + Math.Log(2.5066282746310005 * ser / x);
        }

        private static double BetaCFLocal(double x, double a, double b)
        {
            int maxIter = 200;
            double eps = 3e-7;
            double qab = a + b, qap = a + 1, qam = a - 1;
            double c = 1, d = 1 - qab * x / qap;
            if (Math.Abs(d) < 1e-30) d = 1e-30;
            d = 1.0 / d;
            double h = d;

            for (int m = 1; m <= maxIter; m++)
            {
                int m2 = 2 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1 + aa * d; if (Math.Abs(d) < 1e-30) d = 1e-30;
                c = 1 + aa / c; if (Math.Abs(c) < 1e-30) c = 1e-30;
                d = 1.0 / d; h *= d * c;

                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1 + aa * d; if (Math.Abs(d) < 1e-30) d = 1e-30;
                c = 1 + aa / c; if (Math.Abs(c) < 1e-30) c = 1e-30;
                d = 1.0 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1) < eps) break;
            }
            return h;
        }

        #endregion

        public void ClearCache()
        {
            _dataCache.Clear();
            _projectIndex = null;
            GC.Collect();
        }

        public int CachedProjectCount => _dataCache.Count;
    }

    #region Models

    public class TcgaProjectIndex
    {
        public string project_id { get; set; }
        public int n_genes { get; set; }
        public int n_samples { get; set; }
        public int n_tumor { get; set; }
        public int n_normal { get; set; }
        public int n_survival_available { get; set; }
        public int n_alive { get; set; }
        public int n_dead { get; set; }

        public string CancerCode => project_id?.Replace("TCGA-", "") ?? "";
        public string DisplayName => $"{CancerCode} (T:{n_tumor}, N:{n_normal})";
        public string CancerFullName => CancerCode;
        public bool HasSurvivalData => n_survival_available > 0;
    }

    public class TcgaMetadata
    {
        public string project_id { get; set; }
        public int n_genes { get; set; }
        public int n_samples { get; set; }
        public int n_tumor { get; set; }
        public int n_normal { get; set; }
        public string[] gene_ids { get; set; }
        public string[] gene_names { get; set; }
        public string[] sample_ids { get; set; }
        public string[] sample_conditions { get; set; }
        public string matrix_format { get; set; }
        public string matrix_layout { get; set; }
        public int[] matrix_shape { get; set; }
        // 生存資訊
        public string[] patient_ids { get; set; }
        public string[] vital_status { get; set; }
        public int?[] days_to_death { get; set; }
        public int?[] days_to_last_follow_up { get; set; }
        public int?[] age_at_diagnosis { get; set; }
        public string[] gender { get; set; }
    }

    internal class TcgaProjectData
    {
        public TcgaMetadata Meta { get; set; }
        public float[] Matrix { get; set; }
        public Dictionary<string, int> GeneIndex { get; set; }
        public int[] TumorIndices { get; set; }
        public int[] NormalIndices { get; set; }
    }

    public class GeneExpressionResult
    {
        public string GeneId { get; set; }
        public string GeneName { get; set; }
        public List<double> TumorValues { get; set; }
        public List<double> NormalValues { get; set; }
        public List<string> TumorSampleIds { get; set; }
        public List<string> NormalSampleIds { get; set; }
    }

    public class TwoGeneExpressionResult
    {
        public string Gene1Id { get; set; }
        public string Gene1Name { get; set; }
        public string Gene2Id { get; set; }
        public string Gene2Name { get; set; }
        public List<(double X, double Y, string SampleId)> TumorPairs { get; set; }
        public List<(double X, double Y, string SampleId)> NormalPairs { get; set; }
    }

    public class MultiGeneExpressionResult
    {
        public List<string> GeneIds { get; set; }
        public List<string> GeneNames { get; set; }
        public List<List<double>> Expressions { get; set; }
    }

    #endregion

    #region Survival Models

    public class SurvivalSample
    {
        public string SampleId { get; set; }
        public double Expression { get; set; }
        public int SurvivalDays { get; set; }
        public bool IsEvent { get; set; }
        public string VitalStatus { get; set; }
        public double SurvivalMonths => SurvivalDays / 30.44;
    }

    public class GeneSurvivalResult
    {
        public string GeneId { get; set; }
        public string GeneName { get; set; }
        public List<SurvivalSample> Samples { get; set; }
        public int TotalSamples => Samples?.Count ?? 0;
        public int EventCount => Samples?.Count(s => s.IsEvent) ?? 0;
    }

    #endregion

    #region Volcano Models

    public class VolcanoPoint
    {
        public string GeneId { get; set; }
        public string GeneName { get; set; }
        public double Log2FoldChange { get; set; }
        public double PValue { get; set; }
        public double NegLog10PValue { get; set; }
        public double FDR { get; set; }
        public double TumorMean { get; set; }
        public double NormalMean { get; set; }

        public string PValueDisplay => PValue < 0.001 ? $"{PValue:E2}" : $"{PValue:F4}";
        public string FDRDisplay => FDR < 0.001 ? $"{FDR:E2}" : $"{FDR:F4}";

        public bool IsSignificant(double fdrThreshold = 0.05, double fcThreshold = 1.0)
            => FDR < fdrThreshold && Math.Abs(Log2FoldChange) > fcThreshold;

        public string Regulation
        {
            get
            {
                if (!IsSignificant()) return "NS";
                return Log2FoldChange > 0 ? "Up" : "Down";
            }
        }
    }

    public class VolcanoPlotResult
    {
        public string ProjectId { get; set; }
        public List<VolcanoPoint> Points { get; set; }

        public int UpRegulated(double fdr = 0.05, double fc = 1.0) =>
            Points?.Count(p => p.FDR < fdr && p.Log2FoldChange > fc) ?? 0;

        public int DownRegulated(double fdr = 0.05, double fc = 1.0) =>
            Points?.Count(p => p.FDR < fdr && p.Log2FoldChange < -fc) ?? 0;
    }

    #endregion

    #region Co-Expression Models

    public class CorrelatedGene
    {
        public string GeneId { get; set; }
        public string GeneName { get; set; }
        public double PearsonR { get; set; }
        public double AbsR => Math.Abs(PearsonR);
        public double PValue { get; set; }
        public double FDR { get; set; }
        public int N { get; set; }

        public string PValueDisplay => PValue < 0.001 ? $"{PValue:E2}" : $"{PValue:F4}";
        public string FDRDisplay => FDR < 0.001 ? $"{FDR:E2}" : $"{FDR:F4}";
        public string Direction => PearsonR > 0 ? "Positive" : "Negative";
    }

    public class GeneCorrelationResult
    {
        public string ProjectId { get; set; }
        public string TargetGeneId { get; set; }
        public string TargetGeneName { get; set; }
        public string Condition { get; set; }
        public int SampleCount { get; set; }
        public List<CorrelatedGene> CorrelatedGenes { get; set; }
    }

    #endregion
}