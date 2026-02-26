library(TCGAbiolinks)
library(DESeq2)
library(SummarizedExperiment)
library(jsonlite)



cache_dir  <- "TCGA_cache"      
output_dir <- "Data/TCGA"       
if (!dir.exists(cache_dir))  dir.create(cache_dir,  recursive = TRUE)
if (!dir.exists(output_dir)) dir.create(output_dir, recursive = TRUE)

TARGET_PROJECTS <- NULL
# TARGET_PROJECTS <- c("TCGA-CHOL", "TCGA-LIHC", "TCGA-BRCA")


null_na <- function(x) lapply(x, function(v) {
  if (is.null(v) || length(v) == 0) return(NULL)
  if (length(v) == 1 && is.na(v)) return(NULL)
  v
})

check_memory <- function(tag = "") {
  m <- sum(gc()[, 2])
  cat("  💾", tag, round(m, 0), "MB\n")
}

if (is.null(TARGET_PROJECTS)) {
  all_proj   <- TCGAbiolinks:::getGDCprojects()
  TARGET_PROJECTS <- grep("^TCGA-", all_proj$project_id, value = TRUE)
}

project_index <- list()

for (project in TARGET_PROJECTS) {
  cat(paste(rep("=", 55), collapse=""), "\n")
  cat("▶ ", project, "\n")

  meta_out   <- file.path(output_dir, paste0(project, "_meta.json"))
  matrix_out <- file.path(output_dir, paste0(project, "_matrix.bin"))

  if (file.exists(meta_out) && file.exists(matrix_out)) {
    cat("  ✅ 已存在，跳過（刪除檔案可重新產生）\n")
    existing <- tryCatch(fromJSON(meta_out), error = function(e) NULL)
    if (!is.null(existing)) {
      project_index[[length(project_index) + 1]] <- list(
        project_id           = existing$project_id,
        n_genes              = existing$n_genes,
        n_samples            = existing$n_samples,
        n_tumor              = existing$n_tumor,
        n_normal             = existing$n_normal,
        n_survival_available = sum(!sapply(existing$days_to_last_follow_up, is.null)),
        n_alive              = sum(sapply(existing$vital_status, function(v) !is.null(v) && tolower(v) == "alive")),
        n_dead               = sum(sapply(existing$vital_status, function(v) !is.null(v) && tolower(v) == "dead"))
      )
    }
    next
  }

  # ── Step 1: 下載 RNA-seq 資料 ──────────────────────────────
  cat("  ⬇️  查詢 GDC...\n")
  query <- tryCatch(
    GDCquery(project = project,
             data.category = "Transcriptome Profiling",
             data.type = "Gene Expression Quantification",
             workflow.type = "STAR - Counts"),
    error = function(e) { cat(" 查詢失敗:", e$message, "\n"); NULL }
  )
  if (is.null(query)) next

  data <- tryCatch({
    GDCdownload(query, directory = cache_dir)
    GDCprepare(query, directory = cache_dir)
  }, error = function(e) { cat("  ❌ 下載/準備失敗:", e$message, "\n"); NULL })
  if (is.null(data)) { gc(); next }

  check_memory("載入後")

  # ── Step 2: 取得 counts 矩陣 ──────────────────────────────
  anames <- assayNames(data)
  counts <- if ("unstranded" %in% anames) assay(data, "unstranded") else assay(data, 1)

  meta_cd <- colData(data)
  if (!"sample_type" %in% colnames(meta_cd)) {
    cat("  ⚠️  缺少 sample_type，跳過\n"); rm(data, counts, meta_cd); gc(); next
  }

  condition <- ifelse(meta_cd$sample_type == "Solid Tissue Normal", "Normal", "Tumor")
  if (length(unique(condition)) < 2) {
    cat("  ⚠️  只有單一組別，跳過\n"); rm(data, counts, meta_cd); gc(); next
  }

  # ── Step 3: 基因過濾與清理 ────────────────────────────────
  # 取得 rowData（含 gene_name，在刪欄前先存）
  rd_all <- rowData(data)

  # 過濾：移除全 0
  counts <- counts[rowSums(counts) > 0, ]

  # 去除版本號
  clean_names <- gsub("\\..*", "", rownames(counts))

  # 合併重複基因（取平均）
  if (length(unique(clean_names)) < length(clean_names)) {
    cat("  🔄 合併重複基因...\n")
    uniq_genes <- unique(clean_names)
    merged <- matrix(0L, nrow = length(uniq_genes), ncol = ncol(counts),
                     dimnames = list(uniq_genes, colnames(counts)))
    for (g in uniq_genes) {
      idx <- which(clean_names == g)
      merged[g, ] <- if (length(idx) == 1) counts[idx, ] else
        as.integer(round(colMeans(counts[idx, , drop=FALSE])))
    }
    rm(counts); counts <- merged; rm(merged)
  } else {
    rownames(counts) <- clean_names
  }

  # 低表達過濾（>= 10 total counts）
  counts <- counts[rowSums(counts) >= 10, ]
  if (!is.integer(counts)) { counts <- round(counts); storage.mode(counts) <- "integer" }
  cat("  🧬 最終基因數:", nrow(counts), "\n")
  check_memory("過濾後")

  # ── Step 4: DESeq2 標準化 ────────────────────────────────
  common_samples <- intersect(colnames(counts), rownames(meta_cd))
  counts   <- counts[, common_samples]
  meta_cd  <- meta_cd[common_samples, ]
  condition <- condition[match(common_samples, rownames(meta_cd))]

  coldata <- data.frame(
    row.names = colnames(counts),
    condition = factor(condition, levels = c("Normal", "Tumor"))
  )

  norm_counts <- tryCatch({
    dds <- DESeqDataSetFromMatrix(countData = counts, colData = coldata, design = ~condition)
    dds <- estimateSizeFactors(dds)
    counts(dds, normalized = TRUE)
  }, error = function(e) {
    cat("  ⚠️  DESeq2 失敗，使用原始 counts:", e$message, "\n")
    matrix(as.numeric(counts), nrow=nrow(counts), dimnames=dimnames(counts))
  })

  check_memory("DESeq2 後")

  # ── Step 5: 取得 gene_name 對應 ─────────────────────────
  # 從原始 rowData 對應回過濾後的基因
  rd_df     <- as.data.frame(rd_all)
  rd_ids    <- gsub("\\..*", "", rownames(rd_df))
  name_map  <- setNames(
    if ("gene_name" %in% colnames(rd_df)) as.character(rd_df$gene_name) else rep("", nrow(rd_df)),
    rd_ids
  )
  gene_ids   <- rownames(norm_counts)
  gene_names <- ifelse(is.na(name_map[gene_ids]), "", name_map[gene_ids])
  cat("  📛 gene_name 有值:", sum(nzchar(gene_names)), "/", length(gene_names), "\n")

  # ── Step 6: 抓取臨床資料 ────────────────────────────────
  cat("  📋 抓取臨床資料...\n")
  clinical <- tryCatch(
    GDCquery_clinic(project = project, type = "clinical"),
    error = function(e) { cat("  ⚠️  臨床資料失敗:", e$message, "\n"); NULL }
  )

  sample_ids  <- colnames(norm_counts)
  n_samples   <- length(sample_ids)
  patient_ids <- sapply(sample_ids, function(s) {
    p <- strsplit(s, "-")[[1]]; paste(p[1:min(3,length(p))], collapse="-")
  }, USE.NAMES = FALSE)

  # 對應臨床資料
  get_clinical_col <- function(col_name) {
    lapply(patient_ids, function(pid) {
      if (is.null(clinical)) return(NULL)
      idx <- which(clinical$submitter_id == pid)
      if (length(idx) == 0) return(NULL)
      v <- clinical[[col_name]][idx[1]]
      if (is.na(v) || is.null(v)) NULL else v
    })
  }

  vital_status           <- get_clinical_col("vital_status")
  days_to_death          <- get_clinical_col("days_to_death")
  days_to_last_follow_up <- get_clinical_col("days_to_last_follow_up")
  age_at_diagnosis       <- get_clinical_col("age_at_diagnosis")
  gender                 <- get_clinical_col("gender")

  n_survival <- sum(!sapply(days_to_last_follow_up, is.null))
  n_alive    <- sum(sapply(vital_status, function(v) !is.null(v) && tolower(v) == "alive"))
  n_dead     <- sum(sapply(vital_status, function(v) !is.null(v) && tolower(v) == "dead"))
  cat("  🏥 臨床資料：存活=", n_alive, "死亡=", n_dead, "有追蹤資料=", n_survival, "\n")

  # ── Step 7: 輸出 meta.json ──────────────────────────────
  n_genes  <- nrow(norm_counts)
  n_tumor  <- sum(condition == "Tumor")
  n_normal <- sum(condition == "Normal")

  meta <- list(
    project_id             = project,
    n_genes                = n_genes,
    n_samples              = n_samples,
    n_tumor                = n_tumor,
    n_normal               = n_normal,
    gene_ids               = as.list(gene_ids),
    gene_names             = as.list(unname(gene_names)),
    sample_ids             = as.list(sample_ids),
    sample_conditions      = as.list(condition),
    matrix_format          = "float32",
    matrix_layout          = "row_major",
    matrix_shape           = list(n_genes, n_samples),
    patient_ids            = as.list(patient_ids),
    vital_status           = null_na(vital_status),
    days_to_death          = null_na(days_to_death),
    days_to_last_follow_up = null_na(days_to_last_follow_up),
    age_at_diagnosis       = null_na(age_at_diagnosis),
    gender                 = null_na(gender)
  )
  writeLines(toJSON(meta, auto_unbox = FALSE, null = "null", na = "null"), meta_out)
  cat("  ✅ meta.json 寫出\n")

  # ── Step 8: 輸出 matrix.bin（float32 LE, row-major）────
  con <- file(matrix_out, "wb")
  writeBin(as.numeric(t(norm_counts)), con, size = 4, endian = "little")
  close(con)
  expected <- as.numeric(n_genes) * n_samples * 4
  actual   <- file.size(matrix_out)
  if (actual == expected) {
    cat("  ✅ matrix.bin 寫出 (", round(actual/1e6, 1), "MB)\n")
  } else {
    cat("  ⚠️  matrix.bin 大小不符！期望", expected, "實際", actual, "\n")
  }

  # ── 加入 index ────────────────────────────────────────────
  project_index[[length(project_index) + 1]] <- list(
    project_id           = project,
    n_genes              = n_genes,
    n_samples            = n_samples,
    n_tumor              = n_tumor,
    n_normal             = n_normal,
    n_survival_available = n_survival,
    n_alive              = n_alive,
    n_dead               = n_dead
  )

  # 清理記憶體
  rm(data, counts, norm_counts, meta_cd, coldata, clinical, rd_all, rd_df, meta)
  gc()
  check_memory("完成")
}

# ── 輸出 projects_index.json ─────────────────────────────────
index_path <- file.path(output_dir, "projects_index.json")
writeLines(toJSON(project_index, auto_unbox = TRUE, pretty = TRUE), index_path)
cat("\n✅ projects_index.json 寫出（", length(project_index), "個專案）\n")
cat("🎉 全部完成！輸出在:", output_dir, "\n")
