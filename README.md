# BioXAK — Biology X Analysis Kit

<p align="center">
  <img src="https://img.shields.io/badge/version-1.0.0-blue" />
  <img src="https://img.shields.io/badge/platform-Windows-lightgrey" />
  <img src="https://img.shields.io/badge/license-MIT-green" />
  <img src="https://img.shields.io/badge/framework-.NET%20WPF-purple" />
</p>

> 繁體中文說明請參閱 [讀我檔案.md](讀我檔案.md)

---

## Overview

**BioXAK (Biology X Analysis Kit)** is a free, open-source Windows desktop application that integrates multiple bioinformatics analysis workflows into a single, user-friendly platform. It is designed for wet-lab researchers who need powerful computational biology tools without requiring any programming expertise.

BioXAK is distributed as a self-contained executable requiring no programming knowledge or external runtime installation, and is freely available under the MIT License at https://github.com/bowyung/BioXAK/releases/latest.

---

## Features

| Module | Description |
|--------|-------------|
| 🧬 **TCGA Analysis** | Cancer genomics analysis using DESeq2-normalized expression data; supports differential expression, survival analysis, and multi-group comparisons with automatic statistical test selection |
| ✂️ **CRISPR sgRNA Design** | Automated sgRNA candidate generation with off-target scoring and GC content evaluation |
| 💧 **Flow Cytometry** | FCS file import, biexponential scaling, compensation matrix, and interactive gating |
| 🔬 **Primer Design** | Primer pair design with Tm calculation, GC content, and hairpin/dimer checking |
| 🔎 **Restriction Enzyme Analysis** | Restriction site mapping integrated with REBASE database |
| 🧮 **Molecular Calculators** | DNA/RNA/protein tools including MW calculator, codon usage, and sequence converter |

---

## Download & Installation

1. Go to [**Releases**](https://github.com/bowyung/BioXAK/releases/latest)
2. Download `BioXAK-v1.0.zip`
3. Extract the ZIP file to any folder
4. Double-click `BioXAK.exe` to launch

> **Requirements:** Windows 10 / 11 (64-bit). No additional installation required.

---

## Data

TCGA preprocessed dataset is available on Zenodo: *(DOI to be added)*

Download and place the data file in the `Data/` folder before launching the TCGA analysis module.

---

## Citation

If you use BioXAK in your research, please cite:

> *(Citation will be added upon publication)*

---

## License

This project is licensed under the [MIT License](LICENSE).
