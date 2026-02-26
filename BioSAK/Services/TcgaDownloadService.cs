using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BioSAK.Services
{
    /// <summary>
    /// TCGA 資料自動下載服務
    /// 從 Zenodo 下載 TCGA 預處理資料到本地 Data/TCGA 資料夾
    /// </summary>
    public class TcgaDownloadService
    {
        // ══════════════════════════════════════════════════════════
        //  ★ 填入你的 Zenodo Record ID（URL 中的數字）
        //    例如 https://zenodo.org/records/12345678 → "12345678"
        // ══════════════════════════════════════════════════════════
        public const string ZenodoRecordId = "18780401";

        private static readonly string BaseUrl =
            $"https://zenodo.org/records/{ZenodoRecordId}/files";

        private readonly string _tcgaDataPath;
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10
            };
            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
            client.DefaultRequestHeaders.Add(
                "User-Agent",
                "BioXAK/1.0 (https://github.com/bowyung/BioXAK)");
            client.DefaultRequestHeaders.Add(
                "Accept", "*/*");
            return client;
        }

        public TcgaDownloadService()
        {
            _tcgaDataPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Data", "TCGA");
        }

        // ── 公開屬性 ─────────────────────────────────────────────

        /// <summary>資料是否已存在（有 projects_index.json 即視為完整）</summary>
        public bool IsDataAvailable()
        {
            return Directory.Exists(_tcgaDataPath) &&
                   File.Exists(Path.Combine(_tcgaDataPath, "projects_index.json"));
        }

        /// <summary>取得 Zenodo 上的專案清單（用於顯示將下載哪些檔案）</summary>
        public async Task<List<string>> FetchProjectListAsync()
        {
            var url = $"{BaseUrl}/projects_index.json?download=1";
            var json = await _httpClient.GetStringAsync(url);
            var list = JsonSerializer.Deserialize<List<JsonElement>>(json);
            var projects = new List<string>();
            foreach (var item in list)
            {
                if (item.TryGetProperty("project_id", out var pid))
                    projects.Add(pid.GetString());
            }
            return projects;
        }

        /// <summary>
        /// 完整下載流程：index → 各 meta.json → 各 matrix.bin
        /// </summary>
        /// <param name="progress">回報 (已完成檔案數, 總檔案數, 目前檔名, 百分比0~100)</param>
        /// <param name="ct">取消 token</param>
        public async Task DownloadAllAsync(
            IProgress<DownloadProgress> progress,
            CancellationToken ct = default)
        {
            // 建立資料夾
            Directory.CreateDirectory(_tcgaDataPath);

            // Step 1：下載 projects_index.json
            progress?.Report(new DownloadProgress
            {
                CurrentFile = "projects_index.json",
                Percent = 0,
                StatusText = "Fetching project index..."
            });

            var indexLocal = Path.Combine(_tcgaDataPath, "projects_index.json");
            await DownloadFileAsync(
                $"{BaseUrl}/projects_index.json?download=1",
                indexLocal, ct);

            // Step 2：解析 index，取得所有 project_id
            var indexJson = await File.ReadAllTextAsync(indexLocal, ct);
            var projectList = JsonSerializer.Deserialize<List<JsonElement>>(indexJson);
            var projectIds = new List<string>();
            foreach (var item in projectList)
            {
                if (item.TryGetProperty("project_id", out var pid))
                    projectIds.Add(pid.GetString());
            }

            // 每個 project 需下載 2 個檔案（meta.json + matrix.bin）
            int totalFiles = projectIds.Count * 2;
            int done = 0;

            // Step 3：依序下載各 project 的檔案
            foreach (var projectId in projectIds)
            {
                ct.ThrowIfCancellationRequested();

                // meta.json
                var metaName = $"{projectId}_meta.json";
                var metaLocal = Path.Combine(_tcgaDataPath, metaName);
                if (!File.Exists(metaLocal))
                {
                    progress?.Report(new DownloadProgress
                    {
                        CurrentFile = metaName,
                        Percent = (int)((double)done / totalFiles * 100),
                        StatusText = $"Loading {projectId} metadata...",
                        DoneFiles = done,
                        TotalFiles = totalFiles
                    });
                    await DownloadFileAsync(
                        $"{BaseUrl}/{metaName}?download=1",
                        metaLocal, ct);
                }
                done++;

                // matrix.bin
                var binName = $"{projectId}_matrix.bin";
                var binLocal = Path.Combine(_tcgaDataPath, binName);
                if (!File.Exists(binLocal))
                {
                    progress?.Report(new DownloadProgress
                    {
                        CurrentFile = binName,
                        Percent = (int)((double)done / totalFiles * 100),
                        StatusText = $"Downloading {projectId} expression matrix...",
                        DoneFiles = done,
                        TotalFiles = totalFiles
                    });
                    await DownloadFileLargeAsync(
                        $"{BaseUrl}/{binName}?download=1",
                        binLocal, progress, done, totalFiles, ct);
                }
                done++;
            }

            progress?.Report(new DownloadProgress
            {
                Percent = 100,
                StatusText = "Download complete!",
                DoneFiles = totalFiles,
                TotalFiles = totalFiles
            });
        }

        // ── 私有：小檔案下載 ──────────────────────────────────────

        private async Task DownloadFileAsync(
            string url, string localPath, CancellationToken ct)
        {
            var bytes = await _httpClient.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(localPath, bytes, ct);
        }

        // ── 私有：大檔案串流下載（顯示個別檔案進度）─────────────

        private async Task DownloadFileLargeAsync(
            string url, string localPath,
            IProgress<DownloadProgress> progress,
            int doneBase, int totalFiles,
            CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            long? contentLength = response.Content.Headers.ContentLength;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var file = new FileStream(
                localPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 81920, useAsync: true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;

            while ((read = await stream.ReadAsync(buffer, ct)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;

                if (contentLength.HasValue && progress != null)
                {
                    double filePercent = (double)totalRead / contentLength.Value;
                    int overallPercent = (int)(((double)doneBase + filePercent) / totalFiles * 100);
                    progress.Report(new DownloadProgress
                    {
                        CurrentFile = Path.GetFileName(localPath),
                        Percent = overallPercent,
                        StatusText = $"Downloading {Path.GetFileName(localPath)} " +
                                     $"({totalRead / 1024 / 1024:F0} / {contentLength.Value / 1024 / 1024:F0} MB)",
                        DoneFiles = doneBase,
                        TotalFiles = totalFiles
                    });
                }
            }
        }
    }

    // ── 進度資料結構 ──────────────────────────────────────────────

    public class DownloadProgress
    {
        public string CurrentFile { get; set; } = "";
        public string StatusText { get; set; } = "";
        public int Percent { get; set; }        // 0~100
        public int DoneFiles { get; set; }
        public int TotalFiles { get; set; }
    }
}