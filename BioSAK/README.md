# BioSAK - Biological Swiss Army Knife

## 檔案結構

```
BioSAK/
├── Models/
│   └── RestrictionEnzyme.cs       # 資料模型 (RestrictionEnzyme, CutSite, DnaFragment)
├── Services/
│   ├── RebaseParser.cs            # REBASE 資料解析器
│   └── RestrictionEnzymeCutter.cs # 切割邏輯服務
├── Pages/
│   ├── RestrictionEnzymePatternPage.xaml/.cs    # 電泳圖頁面
│   └── RestrictionEnzymePredictorPage.xaml/.cs  # 分析預測頁面
├── Controls/
│   └── ElectrophoresisGelControl.xaml/.cs       # 電泳圖控件
├── Data/
│   └── common_enzymes.txt         # 酶資料庫
├── MainWindow.xaml/.cs            # 主視窗（按鈕分類介面）
└── README.md                      # 本說明檔
```

## 整合步驟

### 1. 複製檔案到你的專案

將以下資料夾複製到你的 BioSAK 專案：
- `Models/`
- `Services/`
- `Controls/`
- `Data/`
- `Pages/`

### 2. 更新 MainWindow.xaml

在 DNA 選單中，將原本的 `Restriction Enzyme Patten Pridictor` 改為：
```xml
<MenuItem Header="DNA">
    <!-- 現有項目... -->
    <Separator/>
    <MenuItem Header="Restriction Enzyme Pattern Predictor" Click="RestrictionEnzymePattern_Click"/>
    <MenuItem Header="Restriction Enzyme Predictor" Click="RestrictionEnzymePredictor_Click"/>
</MenuItem>
```

### 3. 更新 MainWindow.xaml.cs

加入 using 和事件處理：
```csharp
using BioSAK.Pages;

// 加入這兩個方法
private void RestrictionEnzymePattern_Click(object sender, RoutedEventArgs e)
{
    MainFrame.Navigate(new RestrictionEnzymePatternPage());
}

private void RestrictionEnzymePredictor_Click(object sender, RoutedEventArgs e)
{
    MainFrame.Navigate(new RestrictionEnzymePredictorPage());
}
```

### 4. 設定 Data 檔案

在專案中將 `Data/common_enzymes.txt` 設為：
- **Build Action**: Content
- **Copy to Output Directory**: Copy if newer

或在 `.csproj` 加入：
```xml
<ItemGroup>
  <Content Include="Data\common_enzymes.txt">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

### 5. 確認 namespace

確保所有檔案的 namespace 與你的專案一致（預設為 `BioSAK`）。

## 功能說明

### Restriction Enzyme Pattern Predictor
- 輸入 DNA 序列（支援 circular/linear）
- 選擇單一限制酶
- 顯示切割位點
- 電泳圖視覺化（模擬跑膠效果）
- 片段大小排序表格
- Marker 支援兩種類型：
  - Standard: 10K, 9K, 8K, 7K, 6K, 5K, 4K, 3K, 2K, 1K, 500, 250
  - High Resolution: 10K, 5K, 3K, 1K, 900, 800, 700, 600, 500, 400, 300, 200, 100

### Restriction Enzyme Predictor
- 輸入 DNA 序列（支援 circular/linear）
- 分析所有酶（可篩選）
- 顯示每個酶的：
  - 切割次數
  - 切割位置
  - 末端類型（5' overhang, 3' overhang, Blunt）
  - 是否為回文序列
- 篩選功能：
  - 依識別序列長度（4-cutter, 6-cutter, 8-cutter）
  - 依末端類型
  - 依切割次數

## 支援的 IUPAC 簡併碼

| Code | Nucleotides | Description |
|------|-------------|-------------|
| R | A/G | Purine |
| Y | C/T | Pyrimidine |
| M | A/C | Amino |
| K | G/T | Keto |
| S | G/C | Strong |
| W | A/T | Weak |
| H | A/C/T | Not G |
| B | C/G/T | Not A |
| V | A/C/G | Not T |
| D | A/G/T | Not C |
| N | A/C/G/T | Any |

## 技術細節

### 切割邏輯
- 使用正則表達式搜尋識別序列
- 支援回文與非回文序列
- 環狀 DNA 會正確處理跨越原點的片段
- 電泳圖使用對數刻度模擬 DNA 遷移率

### 資料來源
內建酶資料基於 REBASE 資料庫，包含約 100+ 常用限制酶。
可自行擴充 `common_enzymes.txt` 加入更多酶。

## 擴充

### 加入新酶
在 `Data/common_enzymes.txt` 加入新行：
```
EnzymeName;RecognitionSequence;CutPosition5;CutPosition3
```

範例：
```
EcoRI;GAATTC;1;5     # G^AATTC (5' overhang)
SmaI;CCCGGG;3;3      # CCC^GGG (blunt)
KpnI;GGTACC;5;1      # GGTAC^C (3' overhang)
```

### 下載完整 REBASE
可從 http://rebase.neb.com/rebase/rebase.files.html 下載完整資料，
使用 `bairoch.txt` 格式需修改 `RebaseParser.cs` 的解析邏輯。

## 依賴項
- .NET Framework 4.7.2+ 或 .NET 6+
- WPF (Windows Presentation Foundation)
- 無需額外 NuGet 套件

## 授權
MIT License
