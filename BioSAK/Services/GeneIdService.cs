using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BioSAK.Services
{
    /// <summary>
    /// Gene ID 轉換服務
    /// 載入由 R script 產生的 JSON 資料庫
    /// </summary>
    public class GeneIdService
    {
        private readonly string _dataPath;
        private Dictionary<string, List<GeneEntry>> _symbolIndex;
        private Dictionary<string, GeneEntry> _ensemblIndex;
        private Dictionary<string, GeneEntry> _entrezIndex;
        private Dictionary<string, GeneEntry> _hgncIndex;
        private List<GeneEntry> _allGenes;
        private string _currentSpecies;
        private DatabaseInfo _dbInfo;

        public bool IsDatabaseLoaded => _allGenes != null && _allGenes.Count > 0;

        // 用於除錯的訊息
        public string LastError { get; private set; }

        public GeneIdService()
        {
            _dataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "GeneDB");
            System.Diagnostics.Debug.WriteLine($"[GeneIdService] Data path: {_dataPath}");
            System.Diagnostics.Debug.WriteLine($"[GeneIdService] Base directory: {AppDomain.CurrentDomain.BaseDirectory}");

            if (!Directory.Exists(_dataPath))
            {
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] Creating directory: {_dataPath}");
                Directory.CreateDirectory(_dataPath);
            }
        }

        /// <summary>
        /// 載入基因資料庫 (從 Data/GeneDB/{species}_genes.json)
        /// </summary>
        public async Task<bool> LoadDatabaseAsync(string species)
        {
            if (_currentSpecies == species && IsDatabaseLoaded)
                return true;

            _currentSpecies = species;
            var dbFile = Path.Combine(_dataPath, $"{species}_genes.json");

            System.Diagnostics.Debug.WriteLine($"[GeneIdService] Looking for: {dbFile}");
            System.Diagnostics.Debug.WriteLine($"[GeneIdService] File exists: {File.Exists(dbFile)}");

            if (!File.Exists(dbFile))
            {
                LastError = $"File not found: {dbFile}";
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] ERROR: {LastError}");

                // 列出目錄內容
                if (Directory.Exists(_dataPath))
                {
                    var files = Directory.GetFiles(_dataPath);
                    System.Diagnostics.Debug.WriteLine($"[GeneIdService] Files in directory ({files.Length}):");
                    foreach (var f in files)
                    {
                        System.Diagnostics.Debug.WriteLine($"  - {Path.GetFileName(f)}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[GeneIdService] Directory does not exist: {_dataPath}");
                }

                return false;
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] Loading file: {dbFile}");
                var fileInfo = new FileInfo(dbFile);
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] File size: {fileInfo.Length / 1024.0 / 1024.0:F2} MB");

                var json = await File.ReadAllTextAsync(dbFile);
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] JSON length: {json.Length} chars");
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] JSON preview: {json.Substring(0, Math.Min(500, json.Length))}...");

                var db = JsonSerializer.Deserialize<GeneDatabase>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (db == null)
                {
                    LastError = "Failed to deserialize JSON (db is null)";
                    System.Diagnostics.Debug.WriteLine($"[GeneIdService] ERROR: {LastError}");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[GeneIdService] Deserialized. Info: {db.Info?.Species}, Genes: {db.Genes?.Count ?? 0}");

                if (db.Genes == null || db.Genes.Count == 0)
                {
                    LastError = "Database is empty or invalid (no genes)";
                    System.Diagnostics.Debug.WriteLine($"[GeneIdService] ERROR: {LastError}");
                    return false;
                }

                _allGenes = db.Genes;
                _dbInfo = db.Info ?? new DatabaseInfo
                {
                    Species = species,
                    GeneCount = _allGenes.Count,
                    Version = "unknown",
                    Sources = "Local JSON"
                };

                BuildIndices();

                System.Diagnostics.Debug.WriteLine($"[GeneIdService] Successfully loaded {_allGenes.Count} genes for {species}");
                return true;
            }
            catch (JsonException jex)
            {
                LastError = $"JSON parsing error: {jex.Message}";
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] JSON ERROR: {jex.Message}");
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] Path: {jex.Path}, Line: {jex.LineNumber}");
                return false;
            }
            catch (Exception ex)
            {
                LastError = $"Error loading database: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] ERROR: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] Stack: {ex.StackTrace}");
                return false;
            }
        }

        private void BuildIndices()
        {
            _symbolIndex = new Dictionary<string, List<GeneEntry>>(StringComparer.OrdinalIgnoreCase);
            _ensemblIndex = new Dictionary<string, GeneEntry>(StringComparer.OrdinalIgnoreCase);
            _entrezIndex = new Dictionary<string, GeneEntry>();
            _hgncIndex = new Dictionary<string, GeneEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var gene in _allGenes)
            {
                // Symbol index
                if (!string.IsNullOrEmpty(gene.Symbol))
                {
                    if (!_symbolIndex.ContainsKey(gene.Symbol))
                        _symbolIndex[gene.Symbol] = new List<GeneEntry>();
                    _symbolIndex[gene.Symbol].Add(gene);
                }

                // Aliases index
                if (gene.Aliases != null)
                {
                    foreach (var alias in gene.Aliases.Where(a => !string.IsNullOrEmpty(a)))
                    {
                        if (!_symbolIndex.ContainsKey(alias))
                            _symbolIndex[alias] = new List<GeneEntry>();
                        if (!_symbolIndex[alias].Contains(gene))
                            _symbolIndex[alias].Add(gene);
                    }
                }

                // Ensembl index (支援有/無版本號)
                if (!string.IsNullOrEmpty(gene.EnsemblId))
                {
                    var clean = gene.EnsemblId.Split('.')[0];
                    _ensemblIndex[clean] = gene;
                    _ensemblIndex[gene.EnsemblId] = gene;
                }

                // Entrez index
                if (!string.IsNullOrEmpty(gene.EntrezId))
                    _entrezIndex[gene.EntrezId] = gene;

                // HGNC index (支援有/無 "HGNC:" 前綴)
                if (!string.IsNullOrEmpty(gene.HgncId))
                {
                    _hgncIndex[gene.HgncId] = gene;
                    if (gene.HgncId.StartsWith("HGNC:"))
                        _hgncIndex[gene.HgncId.Substring(5)] = gene;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[GeneIdService] Index built: Symbols={_symbolIndex.Count}, Ensembl={_ensemblIndex.Count}, Entrez={_entrezIndex.Count}");
        }

        /// <summary>
        /// 轉換基因 ID
        /// </summary>
        public List<GeneEntry> Convert(string query, string inputType)
        {
            if (string.IsNullOrEmpty(query))
                return new List<GeneEntry>();

            query = query.Trim();

            switch (inputType)
            {
                case "symbol":
                    if (_symbolIndex.TryGetValue(query, out var symbolMatches))
                        return symbolMatches.ToList();
                    break;

                case "ensembl":
                    var ensemblClean = query.Split('.')[0];
                    if (_ensemblIndex.TryGetValue(ensemblClean, out var ensemblMatch))
                        return new List<GeneEntry> { ensemblMatch };
                    break;

                case "entrez":
                    if (_entrezIndex.TryGetValue(query, out var entrezMatch))
                        return new List<GeneEntry> { entrezMatch };
                    break;

                case "hgnc":
                    // 嘗試有無 HGNC: 前綴
                    if (_hgncIndex.TryGetValue(query, out var hgncMatch))
                        return new List<GeneEntry> { hgncMatch };
                    if (_hgncIndex.TryGetValue("HGNC:" + query, out hgncMatch))
                        return new List<GeneEntry> { hgncMatch };
                    break;
            }

            return new List<GeneEntry>();
        }

        public DatabaseInfo GetDatabaseInfo()
        {
            return _dbInfo ?? new DatabaseInfo { Version = "Not loaded", GeneCount = 0 };
        }

        /// <summary>
        /// 取得可用的資料庫清單
        /// </summary>
        public List<string> GetAvailableDatabases()
        {
            var available = new List<string>();

            System.Diagnostics.Debug.WriteLine($"[GeneIdService] GetAvailableDatabases - checking: {_dataPath}");

            if (Directory.Exists(_dataPath))
            {
                foreach (var file in Directory.GetFiles(_dataPath, "*_genes.json"))
                {
                    var species = Path.GetFileNameWithoutExtension(file).Replace("_genes", "");
                    available.Add(species);
                    System.Diagnostics.Debug.WriteLine($"[GeneIdService] Found database: {species}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] Directory does not exist!");
            }

            return available;
        }

        /// <summary>
        /// 檢查資料庫是否存在
        /// </summary>
        public bool DatabaseExists(string species)
        {
            var dbFile = Path.Combine(_dataPath, $"{species}_genes.json");
            var exists = File.Exists(dbFile);
            System.Diagnostics.Debug.WriteLine($"[GeneIdService] DatabaseExists({species}): {exists} - {dbFile}");
            return exists;
        }

        /// <summary>
        /// 批次將任意 ID 轉換為 Gene Symbol
        /// 自動偵測輸入類型 (Symbol, Ensembl, Entrez, HGNC)
        /// </summary>
        /// <param name="ids">輸入的 ID 列表</param>
        /// <returns>轉換結果字典 (原始ID -> Symbol)，找不到的會保留原始值</returns>
        public Dictionary<string, string> ConvertToSymbols(IEnumerable<string> ids)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var id in ids.Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                var trimmed = id.Trim();
                var symbol = ConvertSingleToSymbol(trimmed);
                result[trimmed] = symbol ?? trimmed; // 找不到就保留原始值
            }

            return result;
        }

        /// <summary>
        /// 將單一 ID 轉換為 Gene Symbol
        /// 自動偵測輸入類型
        /// </summary>
        public string ConvertSingleToSymbol(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !IsDatabaseLoaded)
                return null;

            id = id.Trim();

            // 1. 先檢查是否已經是 Symbol
            if (_symbolIndex.TryGetValue(id, out var symbolMatches))
                return symbolMatches[0].Symbol;

            // 2. 檢查 Ensembl ID (ENSG, ENSMUSG, ENSRNOG...)
            if (id.StartsWith("ENS", StringComparison.OrdinalIgnoreCase))
            {
                var clean = id.Split('.')[0];
                if (_ensemblIndex.TryGetValue(clean, out var ensemblMatch))
                    return ensemblMatch.Symbol;
            }

            // 3. 檢查 HGNC ID
            if (id.StartsWith("HGNC:", StringComparison.OrdinalIgnoreCase) ||
                (int.TryParse(id, out _) && _hgncIndex.ContainsKey("HGNC:" + id)))
            {
                var hgncKey = id.StartsWith("HGNC:") ? id : "HGNC:" + id;
                if (_hgncIndex.TryGetValue(hgncKey, out var hgncMatch))
                    return hgncMatch.Symbol;
            }

            // 4. 檢查 Entrez ID (純數字)
            if (int.TryParse(id, out _))
            {
                if (_entrezIndex.TryGetValue(id, out var entrezMatch))
                    return entrezMatch.Symbol;
            }

            return null;
        }

        /// <summary>
        /// 偵測 ID 類型
        /// </summary>
        public string DetectIdType(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return "unknown";

            id = id.Trim();

            if (id.StartsWith("ENS", StringComparison.OrdinalIgnoreCase))
                return "ensembl";

            if (id.StartsWith("HGNC:", StringComparison.OrdinalIgnoreCase))
                return "hgnc";

            if (int.TryParse(id, out _))
            {
                // 純數字可能是 Entrez 或 HGNC
                if (_entrezIndex.ContainsKey(id))
                    return "entrez";
                if (_hgncIndex.ContainsKey("HGNC:" + id))
                    return "hgnc";
                return "entrez"; // 預設當作 Entrez
            }

            // 其他當作 Symbol
            return "symbol";
        }

        /// <summary>
        /// 批次偵測並報告 ID 類型統計
        /// </summary>
        public Dictionary<string, int> AnalyzeIdTypes(IEnumerable<string> ids)
        {
            var stats = new Dictionary<string, int>
            {
                { "symbol", 0 },
                { "ensembl", 0 },
                { "entrez", 0 },
                { "hgnc", 0 },
                { "unknown", 0 }
            };

            foreach (var id in ids.Where(i => !string.IsNullOrWhiteSpace(i)))
            {
                var type = DetectIdType(id.Trim());
                stats[type]++;
            }

            return stats;
        }

        #region Download Functions

        /// <summary>
        /// 下載並更新指定物種的基因資料庫
        /// </summary>
        public async Task<bool> DownloadDatabaseAsync(string species, Action<int, string> progress)
        {
            try
            {
                progress(5, $"Connecting to server...");

                List<GeneEntry> genes;

                switch (species)
                {
                    case "human":
                        genes = await DownloadHumanHgncAsync(progress);
                        break;
                    case "mouse":
                        genes = await DownloadNcbiAsync(species, "Mammalia/Mus_musculus.gene_info.gz", "ENSMUSG", progress);
                        break;
                    case "rat":
                        genes = await DownloadNcbiAsync(species, "Mammalia/Rattus_norvegicus.gene_info.gz", "ENSRNOG", progress);
                        break;
                    case "zebrafish":
                        genes = await DownloadNcbiAsync(species, "Non-mammalian_vertebrates/Danio_rerio.gene_info.gz", "ENSDARG", progress);
                        break;
                    case "fly":
                        genes = await DownloadNcbiAsync(species, "Invertebrates/Drosophila_melanogaster.gene_info.gz", "FBgn", progress);
                        break;
                    case "worm":
                        genes = await DownloadNcbiAsync(species, "Invertebrates/Caenorhabditis_elegans.gene_info.gz", "WBGene", progress);
                        break;
                    default:
                        progress(0, $"Unknown species: {species}");
                        return false;
                }

                if (genes == null || genes.Count == 0)
                {
                    progress(0, "No genes downloaded");
                    return false;
                }

                progress(90, $"Saving {genes.Count:N0} genes...");
                await SaveDatabaseAsync(species, genes);

                progress(100, "Complete!");

                // 重新載入
                _currentSpecies = null;
                await LoadDatabaseAsync(species);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GeneIdService] Download error: {ex.Message}");
                progress(0, $"Error: {ex.Message}");
                return false;
            }
        }

        private async Task<List<GeneEntry>> DownloadHumanHgncAsync(Action<int, string> progress)
        {
            progress(10, "Downloading from HGNC...");

            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };

            // Google Cloud Storage URL
            var url = "https://storage.googleapis.com/public-download-files/hgnc/tsv/tsv/hgnc_complete_set.txt";

            var response = await client.GetStringAsync(url);
            progress(50, "Parsing HGNC data...");

            var genes = new List<GeneEntry>();
            var lines = response.Split('\n');

            // 解析 header
            var header = lines[0].Split('\t').Select(h => h.Trim().ToLower()).ToList();
            int idxSymbol = header.IndexOf("symbol");
            int idxEnsembl = header.IndexOf("ensembl_gene_id");
            int idxEntrez = header.IndexOf("entrez_id");
            int idxHgnc = header.IndexOf("hgnc_id");
            int idxName = header.IndexOf("name");
            int idxLocus = header.IndexOf("locus_type");
            int idxLoc = header.IndexOf("location");
            int idxAlias = header.IndexOf("alias_symbol");
            int idxPrev = header.IndexOf("prev_symbol");

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var parts = lines[i].Split('\t');
                if (parts.Length <= idxSymbol) continue;

                var symbol = GetField(parts, idxSymbol);
                if (string.IsNullOrEmpty(symbol)) continue;

                var gene = new GeneEntry
                {
                    Symbol = symbol,
                    EnsemblId = GetField(parts, idxEnsembl),
                    EntrezId = GetField(parts, idxEntrez),
                    HgncId = GetField(parts, idxHgnc),
                    FullName = GetField(parts, idxName),
                    Biotype = GetField(parts, idxLocus),
                    Chromosome = GetField(parts, idxLoc)
                };

                // Aliases
                var aliases = new List<string>();
                var aliasStr = GetField(parts, idxAlias);
                var prevStr = GetField(parts, idxPrev);
                if (!string.IsNullOrEmpty(aliasStr))
                    aliases.AddRange(aliasStr.Split('|').Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)));
                if (!string.IsNullOrEmpty(prevStr))
                    aliases.AddRange(prevStr.Split('|').Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)));
                if (aliases.Count > 0)
                    gene.Aliases = aliases.Distinct().ToList();

                genes.Add(gene);

                if (i % 5000 == 0)
                    progress(50 + (i * 30 / lines.Length), $"Processing {i:N0}/{lines.Length:N0}...");
            }

            return genes;
        }

        private async Task<List<GeneEntry>> DownloadNcbiAsync(string species, string filePath, string ensemblPrefix, Action<int, string> progress)
        {
            progress(10, $"Downloading {species} from NCBI...");

            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromMinutes(10) };

            var url = $"https://ftp.ncbi.nlm.nih.gov/gene/DATA/GENE_INFO/{filePath}";

            // 下載 gzip 檔案
            var bytes = await client.GetByteArrayAsync(url);
            progress(40, "Decompressing...");

            // 解壓縮
            string content;
            using (var ms = new MemoryStream(bytes))
            using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzip))
            {
                content = await reader.ReadToEndAsync();
            }

            progress(50, "Parsing gene data...");

            var genes = new List<GeneEntry>();
            var lines = content.Split('\n');

            // 解析 header (移除 # 開頭)
            var headerLine = lines[0];
            if (headerLine.StartsWith("#")) headerLine = headerLine.Substring(1);
            var header = headerLine.Split('\t').Select(h => h.Trim()).ToList();

            int idxGeneId = header.IndexOf("GeneID");
            int idxSymbol = header.IndexOf("Symbol");
            int idxSynonyms = header.IndexOf("Synonyms");
            int idxDbXrefs = header.IndexOf("dbXrefs");
            int idxChrom = header.IndexOf("chromosome");
            int idxDesc = header.IndexOf("description");
            int idxType = header.IndexOf("type_of_gene");

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                var parts = lines[i].Split('\t');
                if (parts.Length <= idxSymbol) continue;

                var symbol = GetField(parts, idxSymbol);
                if (string.IsNullOrEmpty(symbol) || symbol == "-") continue;

                var gene = new GeneEntry
                {
                    Symbol = symbol,
                    EntrezId = GetField(parts, idxGeneId),
                    FullName = GetFieldOrNull(parts, idxDesc),
                    Biotype = GetFieldOrNull(parts, idxType),
                    Chromosome = GetFieldOrNull(parts, idxChrom)
                };

                // 從 dbXrefs 取得 Ensembl ID
                var xrefs = GetField(parts, idxDbXrefs);
                if (!string.IsNullOrEmpty(xrefs) && xrefs != "-")
                {
                    var match = System.Text.RegularExpressions.Regex.Match(xrefs, $"Ensembl:({ensemblPrefix}[0-9]+)");
                    if (match.Success)
                        gene.EnsemblId = match.Groups[1].Value;
                }

                // Aliases
                var synonyms = GetField(parts, idxSynonyms);
                if (!string.IsNullOrEmpty(synonyms) && synonyms != "-")
                {
                    gene.Aliases = synonyms.Split('|').Select(a => a.Trim()).Where(a => !string.IsNullOrEmpty(a)).ToList();
                }

                genes.Add(gene);

                if (i % 10000 == 0)
                    progress(50 + (i * 30 / lines.Length), $"Processing {i:N0}/{lines.Length:N0}...");
            }

            return genes;
        }

        private string GetField(string[] parts, int index)
        {
            if (index < 0 || index >= parts.Length) return null;
            var val = parts[index].Trim().Trim('"');
            return string.IsNullOrEmpty(val) ? null : val;
        }

        private string GetFieldOrNull(string[] parts, int index)
        {
            var val = GetField(parts, index);
            return val == "-" ? null : val;
        }

        private async Task SaveDatabaseAsync(string species, List<GeneEntry> genes)
        {
            var db = new GeneDatabase
            {
                Info = new DatabaseInfo
                {
                    Species = species,
                    Version = DateTime.Now.ToString("yyyy.MM.dd"),
                    GeneCount = genes.Count,
                    LastUpdate = DateTime.Now,
                    Sources = species == "human" ? "HGNC" : "NCBI Gene"
                },
                Genes = genes
            };

            var dbFile = Path.Combine(_dataPath, $"{species}_genes.json");
            var json = JsonSerializer.Serialize(db, new JsonSerializerOptions
            {
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });
            await File.WriteAllTextAsync(dbFile, json);

            System.Diagnostics.Debug.WriteLine($"[GeneIdService] Saved database to: {dbFile}");
        }

        #endregion
    }

    #region Models

    public class GeneDatabase
    {
        public DatabaseInfo Info { get; set; }
        public List<GeneEntry> Genes { get; set; }
    }

    public class DatabaseInfo
    {
        public string Species { get; set; }
        public string Version { get; set; }
        public int GeneCount { get; set; }
        public DateTime LastUpdate { get; set; }
        public string Sources { get; set; }
    }

    public class GeneEntry
    {
        public string Symbol { get; set; }
        public string EnsemblId { get; set; }
        public string EntrezId { get; set; }
        public string HgncId { get; set; }
        public string FullName { get; set; }
        public string Biotype { get; set; }
        public string Chromosome { get; set; }
        public List<string> Aliases { get; set; }

        // 物種特定 ID
        public string MgiId { get; set; }      // Mouse
        public string RgdId { get; set; }      // Rat
        public string ZfinId { get; set; }     // Zebrafish
    }

    #endregion
}
