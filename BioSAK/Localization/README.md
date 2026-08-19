# 介面語言切換 (i18n)

只影響**畫面上顯示的文字**。分析結果、統計數值、匯出的 CSV/PNG/TIFF、
剪貼簿內容、圖表軸標與 .xak 檔內容 **完全不經過** 這套機制，維持原樣。

## 檔案

| 檔案 | 用途 |
|---|---|
| `LocalizationManager.cs` | 單例語言管理器，切換語言 + 記住使用者選擇 |
| `LExtension.cs` | XAML 標記擴充 `{loc:L Key}`，實際上是綁到 Manager 的索引子 |
| `LocalizationStrings.cs` | 字串表（`En` / `ZhHant`），**自動產生**，可手動增修 |

語言設定存在 `%AppData%\BioXAK\language.txt`。

## 在 XAML 中使用

根項目已加入命名空間：

```xml
xmlns:loc="clr-namespace:BioSAK.Localization"
```

然後：

```xml
<TextBlock Text="{loc:L My_Key}"/>
<Button   Content="{loc:L My_Key}" ToolTip="{loc:L My_Tip}"/>
<DataGridTextColumn Header="{loc:L My_Key}" Binding="{Binding Foo}"/>
```

切換語言時 UI 會**即時更新**，不需重開程式。

## 在程式碼中使用

```csharp
StatusText.Text = BioSAK.Localization.L.T("My_Key");
```

## 新增字串

在 `LocalizationStrings.cs` 的 `En` 與 `ZhHant` 兩個字典各加一筆即可。
`ZhHant` 沒有的 key 會自動退回英文，所以可以只補一部分。

## 注意事項（避免踩雷）

1. **`ComboBoxItem` 的 `Content` 一律不翻譯。** 多處程式碼用
   `SelectedItem.Content.ToString()` 當成「值」在比對（色階名稱、單位、
   Log/Linear、PAM 等），翻譯會直接改掉運算行為與輸出。
2. **DataGrid 欄位不要用 `Column.Header` 當識別字。** 已改為用
   `SortMemberPath`（Binding 路徑）：
   - `TcgaAnalysisPage.CoExprDataGrid_Sorting`
   - `GeneIdConverterPage.ColumnKey` / `GetCellValue`
   新增欄位時請沿用這個做法。
3. **程式碼裡直接指派 `.Text = "..."` 會蓋掉綁定**，該控制項之後就不會
   隨語言切換更新。若那段文字要跟著語言走，請改用 `L.T("Key")`。

## 尚未中文化的部分

`MessageBox` 訊息、執行期狀態列文字、例外訊息等仍為英文；
XAML 介面（按鈕／標籤／標題／欄位標頭／提示）已全部完成。

## 還原

`_i18n_backup/` 內是所有 XAML 的修改前備份（`*.xaml.bak`）。
確認沒問題後可整個資料夾刪除。
