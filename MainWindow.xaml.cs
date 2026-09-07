using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AutoUpgrade.Models;
using AutoUpgrade.Services;
using Microsoft.Win32;

namespace AutoUpgrade;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<AccountTask> _tasks = new();
    private readonly ObservableCollection<SavedResultItem> _filteredResults = new();
    private readonly object _taskLock = new();
    private readonly object _resultLock = new();

    private readonly AppSettings _settings;
    private readonly ResultsStore _resultsStore;
    private readonly ProxyManager _proxyManager = new();
    private readonly WorkerEngine _workerEngine;

    public MainWindow()
    {
        _settings = AppSettings.Load();
        ThemeManager.SetTheme(this.Resources, _settings.IsDarkMode);
        ThemeManager.ThemeChanged += OnThemeChanged;

        InitializeComponent();

        _resultsStore = new ResultsStore(_settings);

        BindingOperations.EnableCollectionSynchronization(_tasks, _taskLock);
        BindingOperations.EnableCollectionSynchronization(_filteredResults, _resultLock);

        DgTasks.ItemsSource = _tasks;
        DgResults.ItemsSource = _filteredResults;

        _workerEngine = new WorkerEngine(_proxyManager);
        _workerEngine.LogMessage += OnLogMessage;
        _workerEngine.TaskUpdated += OnTaskUpdated;
        _workerEngine.EngineCompleted += OnEngineCompleted;

        // Initialize UI from Settings
        SliderThreads.Value = _settings.DefaultThreads;
        TxtThreadCount.Text = $"{_settings.DefaultThreads} Threads";

        if (!string.IsNullOrWhiteSpace(_settings.DefaultProxies))
        {
            TxtProxies.Text = _settings.DefaultProxies;
            AppendLog($"[PROXY] Restored {_proxyManager.Count} saved default proxy item(s).");
        }

        UpdateThemeButtonUi();
        UpdateStats();
        UpdateResultsFilter();
        UpdateHeaderSavedCount();
        LoadSettingsToUi();
    }

    #region Tab Navigation

    private void TabNav_Checked(object sender, RoutedEventArgs e)
    {
        if (MainTabControl == null) return;

        if (TabNavRunner?.IsChecked == true)
        {
            MainTabControl.SelectedItem = TabRunner;
        }
        else if (TabNavResults?.IsChecked == true)
        {
            MainTabControl.SelectedItem = TabResults;
            UpdateResultsFilter();
        }
        else if (TabNavSettings?.IsChecked == true)
        {
            MainTabControl.SelectedItem = TabSettings;
            LoadSettingsToUi();
        }
    }

    private void UpdateHeaderSavedCount()
    {
        if (TxtHeaderSavedCount != null)
        {
            TxtHeaderSavedCount.Text = _resultsStore.AllResults.Count.ToString();
        }
    }

    #endregion

    #region Theme Handling

    private void BtnToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        _settings.IsDarkMode = !_settings.IsDarkMode;
        ThemeManager.SetTheme(this.Resources, _settings.IsDarkMode);
        _settings.Save();
    }

    private void RadioTheme_Checked(object sender, RoutedEventArgs e)
    {
        bool wantDark = RadioThemeDark?.IsChecked == true;
        if (wantDark != ThemeManager.IsDarkMode)
        {
            _settings.IsDarkMode = wantDark;
            ThemeManager.SetTheme(this.Resources, wantDark);
            _settings.Save();
        }
    }

    private void OnThemeChanged(bool isDark)
    {
        UpdateThemeButtonUi();

        // Refresh DataGrids so status badge converters update with new theme colors
        DgTasks?.Items.Refresh();
        DgResults?.Items.Refresh();
    }

    private void UpdateThemeButtonUi()
    {
        if (TxtThemeMode == null || PathThemeIcon == null) return;

        if (ThemeManager.IsDarkMode)
        {
            TxtThemeMode.Text = "Light";
            PathThemeIcon.Data = (Geometry)FindResource("IconSun");
            if (RadioThemeDark != null && RadioThemeDark.IsChecked != true) RadioThemeDark.IsChecked = true;
        }
        else
        {
            TxtThemeMode.Text = "Dark";
            PathThemeIcon.Data = (Geometry)FindResource("IconMoon");
            if (RadioThemeLight != null && RadioThemeLight.IsChecked != true) RadioThemeLight.IsChecked = true;
        }
    }

    #endregion

    #region Drag & Drop & File Loading

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            e.Handled = true;
            ProcessDroppedFiles(files);
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropZoneBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeAccent");
            e.Handled = true;
        }
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        DropZoneBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorder");
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZoneBorder.SetResourceReference(Border.BorderBrushProperty, "ThemeBorder");
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            e.Handled = true;
            ProcessDroppedFiles(files);
        }
    }

    private void DropZone_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenBrowseDialog();
    }

    private void BtnBrowseFiles_Click(object sender, RoutedEventArgs e)
    {
        OpenBrowseDialog();
    }

    private void OpenBrowseDialog()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Token Text Files",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            ProcessDroppedFiles(dialog.FileNames);
        }
    }

    private void BtnPasteClipboard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Clipboard is empty or does not contain text.", "Clipboard Empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var extractedTasks = TokenExtractor.ExtractFromString(text, "clipboard");
            if (extractedTasks.Count > 0)
            {
                lock (_taskLock)
                {
                    foreach (var task in extractedTasks)
                    {
                        task.Id = _tasks.Count + 1;
                        _tasks.Add(task);
                    }
                }
                AppendLog($"[CLIPBOARD] Loaded {extractedTasks.Count} account task(s) from clipboard.");
                UpdateStats();
            }
            else
            {
                MessageBox.Show("No valid GR_TOKEN / GR_REFRESH found in clipboard text.\nMake sure the copied text contains either token cookie strings or JSON arrays.", "No Tokens Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                AppendLog("[WARN] No GR_TOKEN / GR_REFRESH found in clipboard.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error reading clipboard: {ex.Message}", "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ProcessDroppedFiles(string[] filePaths)
    {
        try
        {
            int addedCount = 0;

            foreach (var path in filePaths)
            {
                if (File.Exists(path))
                {
                    var extractedTasks = TokenExtractor.ExtractFromFile(path);
                    if (extractedTasks.Count > 0)
                    {
                        lock (_taskLock)
                        {
                            foreach (var task in extractedTasks)
                            {
                                task.Id = _tasks.Count + 1;
                                _tasks.Add(task);
                                addedCount++;
                            }
                        }
                    }
                    else
                    {
                        AppendLog($"[WARN] No GR_TOKEN / GR_REFRESH found in file: {Path.GetFileName(path)}");
                    }
                }
                else if (Directory.Exists(path))
                {
                    var txtFiles = Directory.GetFiles(path, "*.txt", SearchOption.AllDirectories);
                    ProcessDroppedFiles(txtFiles);
                }
            }

            if (addedCount > 0)
            {
                AppendLog($"[LOAD] Loaded {addedCount} token task(s) into the queue.");
                UpdateStats();
            }
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] Failed to load files: {ex.Message}");
            MessageBox.Show($"Error processing files: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    #endregion

    #region Proxy Handling

    private void TxtProxies_TextChanged(object sender, TextChangedEventArgs e)
    {
        _proxyManager.LoadFromText(TxtProxies.Text);
        TxtProxyCount.Text = $"{_proxyManager.Count} Loaded";
    }

    private void BtnLoadProxies_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Proxy List",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            string content = File.ReadAllText(dialog.FileName);
            TxtProxies.Text = content;
            AppendLog($"[PROXY] Loaded proxies from {Path.GetFileName(dialog.FileName)} ({_proxyManager.Count} parsed).");
        }
    }

    private void BtnClearProxies_Click(object sender, RoutedEventArgs e)
    {
        TxtProxies.Text = string.Empty;
        _proxyManager.Clear();
        TxtProxyCount.Text = "0 Loaded";
        AppendLog("[PROXY] Proxies cleared.");
    }

    private void BtnSaveProxies_Click(object sender, RoutedEventArgs e)
    {
        string proxies = TxtProxies.Text.Trim();
        _settings.DefaultProxies = proxies;
        _settings.Save();

        if (TxtSettingDefaultProxies != null)
        {
            TxtSettingDefaultProxies.Text = proxies;
        }
        UpdateSavedProxyCountDisplay();

        int count = _proxyManager.Count;
        AppendLog($"[PROXY] Saved {count} default proxy item(s) to settings for future sessions.");
        MessageBox.Show($"Successfully saved {count} proxy item(s) as default.\nThey will be restored automatically on future launches.", "Default Proxies Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BtnExportProxies_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtProxies.Text))
        {
            MessageBox.Show("No proxies to export.", "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Export Proxy List",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = $"proxies_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (saveDialog.ShowDialog() == true)
        {
            File.WriteAllText(saveDialog.FileName, TxtProxies.Text);
            AppendLog($"[PROXY] Exported proxy list to {Path.GetFileName(saveDialog.FileName)}");
            MessageBox.Show($"Exported proxies to:\n{saveDialog.FileName}", "Export Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    #endregion

    #region Concurrency & Control Buttons

    private void SliderThreads_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtThreadCount != null)
        {
            TxtThreadCount.Text = $"{(int)SliderThreads.Value} Threads";
        }
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_tasks.Count == 0)
        {
            MessageBox.Show("Please drop or load at least one .txt file containing GR_TOKEN and GR_REFRESH.", "No Tasks", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_proxyManager.Count == 0)
        {
            MessageBox.Show("Proxy is mandatory! Please paste or load at least one proxy before starting.", "Proxy Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        BtnStart.IsEnabled = false;
        BtnStop.IsEnabled = true;

        int threads = (int)SliderThreads.Value;
        await _workerEngine.StartAsync(_tasks, threads);
    }

    private void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        _workerEngine.Stop();
        BtnStop.IsEnabled = false;
    }

    private void OnEngineCompleted()
    {
        Dispatcher.InvokeAsync(() =>
        {
            BtnStart.IsEnabled = true;
            BtnStop.IsEnabled = false;
            UpdateStats();
            UpdateResultsFilter();
            UpdateHeaderSavedCount();
        });
    }

    private void OnTaskUpdated(AccountTask task)
    {
        Dispatcher.InvokeAsync(() =>
        {
            UpdateStats();

            // Automatically record completed tasks into the persistent Results store
            if (task.StatusType == TaskStatusType.Approved ||
                task.StatusType == TaskStatusType.Declined ||
                task.StatusType == TaskStatusType.Error)
            {
                _resultsStore.AddResult(task);
                UpdateResultsFilter();
                UpdateHeaderSavedCount();
            }
        });
    }

    private void BtnClearList_Click(object sender, RoutedEventArgs e)
    {
        if (_workerEngine.IsRunning)
        {
            MessageBox.Show("Please stop the active run before clearing tasks.", "Engine Running", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        lock (_taskLock)
        {
            _tasks.Clear();
        }
        UpdateStats();
        AppendLog("[QUEUE] Task queue cleared.");
    }

    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_tasks.Count == 0)
        {
            MessageBox.Show("No tasks available to export.", "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Export Current Run Results",
            Filter = "Text File (*.txt)|*.txt|CSV File (*.csv)|*.csv",
            FileName = $"current_run_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (saveDialog.ShowDialog() == true)
        {
            bool isCsv = saveDialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
            var sb = new StringBuilder();

            if (isCsv)
            {
                sb.AppendLine("ID,Status,Email,UserId,PurchaseId,PriceId,Message,Proxy,Timestamp");
                foreach (var t in _tasks)
                {
                    sb.AppendLine($"\"{t.Id}\",\"{t.Status}\",\"{t.Email}\",\"{t.UserId}\",\"{t.PurchaseId}\",\"{t.PriceId}\",\"{t.Message.Replace("\"", "\"\"")}\",\"{t.ProxyUsed}\",\"{t.Timestamp}\"");
                }
            }
            else
            {
                sb.AppendLine("==========================================================================");
                sb.AppendLine($"MAGNIFIC CURRENT RUN - {DateTime.Now}");
                sb.AppendLine("==========================================================================\n");

                foreach (var t in _tasks)
                {
                    sb.AppendLine($"[{t.Status.ToUpper()}] #{t.Id} | Email: {t.Email} | CustomerID: {t.UserId}");
                    sb.AppendLine($"PurchaseID: {t.PurchaseId} | PriceID: {t.PriceId}");
                    sb.AppendLine($"Details: {t.Message} | Time: {t.Timestamp}");
                    sb.AppendLine(new string('-', 70));
                }
            }

            File.WriteAllText(saveDialog.FileName, sb.ToString());
            AppendLog($"[EXPORT] Results saved to {Path.GetFileName(saveDialog.FileName)}");
            MessageBox.Show($"Results exported successfully to:\n{saveDialog.FileName}", "Export Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    #endregion

    #region Logging & Stats

    private void OnLogMessage(string message)
    {
        AppendLog(message);
    }

    private void AppendLog(string text)
    {
        Dispatcher.InvokeAsync(() =>
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {text}\n";
            TxtConsole.AppendText(line);
            if (_settings.AutoScrollLog)
            {
                TxtConsole.ScrollToEnd();
            }
        });
    }

    private void BtnClearLog_Click(object sender, RoutedEventArgs e)
    {
        TxtConsole.Clear();
    }

    private void UpdateStats()
    {
        int total = _tasks.Count;
        int processing = _tasks.Count(t => t.StatusType == TaskStatusType.Running);
        int approved = _tasks.Count(t => t.StatusType == TaskStatusType.Approved);
        int declined = _tasks.Count(t => t.StatusType == TaskStatusType.Declined);
        int errors = _tasks.Count(t => t.StatusType == TaskStatusType.Error);

        TxtStatTotal.Text = total.ToString();
        TxtStatProcessing.Text = processing.ToString();
        TxtStatApproved.Text = approved.ToString();
        TxtStatDeclined.Text = declined.ToString();
        TxtStatErrors.Text = errors.ToString();
    }

    #endregion

    #region Results Tab Operations

    private void FilterResults_Changed(object sender, RoutedEventArgs e)
    {
        UpdateResultsFilter();
    }

    private void TxtSearchResults_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateResultsFilter();
    }

    private void UpdateResultsFilter()
    {
        if (_resultsStore == null || DgResults == null) return;

        lock (_resultLock)
        {
            var query = _resultsStore.AllResults.AsEnumerable();

            // Status filter
            if (FilterApproved?.IsChecked == true)
            {
                query = query.Where(r => r.StatusType == TaskStatusType.Approved);
            }
            else if (FilterDeclined?.IsChecked == true)
            {
                query = query.Where(r => r.StatusType == TaskStatusType.Declined);
            }
            else if (FilterErrors?.IsChecked == true)
            {
                query = query.Where(r => r.StatusType == TaskStatusType.Error);
            }

            // Text search
            string search = TxtSearchResults?.Text?.Trim().ToLowerInvariant() ?? "";
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(r =>
                    (r.Email != null && r.Email.ToLowerInvariant().Contains(search)) ||
                    (r.UserId != null && r.UserId.ToLowerInvariant().Contains(search)) ||
                    (r.PurchaseId != null && r.PurchaseId.ToLowerInvariant().Contains(search)) ||
                    (r.PriceId != null && r.PriceId.ToLowerInvariant().Contains(search)) ||
                    (r.Message != null && r.Message.ToLowerInvariant().Contains(search)) ||
                    (r.FileName != null && r.FileName.ToLowerInvariant().Contains(search)));
            }

            _filteredResults.Clear();
            foreach (var item in query)
            {
                _filteredResults.Add(item);
            }
        }

        // Update Results Metric Summary
        int totalSaved = _resultsStore.AllResults.Count;
        int approvedSaved = _resultsStore.AllResults.Count(r => r.StatusType == TaskStatusType.Approved);
        int declinedSaved = _resultsStore.AllResults.Count(r => r.StatusType == TaskStatusType.Declined);
        int errorsSaved = _resultsStore.AllResults.Count(r => r.StatusType == TaskStatusType.Error);

        if (TxtResTotal != null) TxtResTotal.Text = totalSaved.ToString();
        if (TxtResApproved != null) TxtResApproved.Text = approvedSaved.ToString();
        if (TxtResDeclined != null) TxtResDeclined.Text = declinedSaved.ToString();
        if (TxtResErrors != null) TxtResErrors.Text = errorsSaved.ToString();
        if (TxtHeaderSavedCount != null) TxtHeaderSavedCount.Text = totalSaved.ToString();
    }

    private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_filteredResults.Count == 0)
        {
            MessageBox.Show("No results available to export.", "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Export Saved Results to CSV",
            Filter = "CSV File (*.csv)|*.csv",
            FileName = $"saved_results_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };

        if (saveDialog.ShowDialog() == true)
        {
            string csv = _resultsStore.ExportCsv(_filteredResults);
            File.WriteAllText(saveDialog.FileName, csv);
            MessageBox.Show($"Exported {_filteredResults.Count} item(s) to:\n{saveDialog.FileName}", "Export Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnExportTxt_Click(object sender, RoutedEventArgs e)
    {
        if (_filteredResults.Count == 0)
        {
            MessageBox.Show("No results available to export.", "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var saveDialog = new SaveFileDialog
        {
            Title = "Export Saved Results to Text",
            Filter = "Text File (*.txt)|*.txt",
            FileName = $"saved_results_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
        };

        if (saveDialog.ShowDialog() == true)
        {
            string txt = _resultsStore.ExportTxt(_filteredResults);
            File.WriteAllText(saveDialog.FileName, txt);
            MessageBox.Show($"Exported {_filteredResults.Count} item(s) to:\n{saveDialog.FileName}", "Export Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = _settings.SaveDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("Are you sure you want to clear all saved results history?", "Confirm Clear", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            _resultsStore.Clear();
            UpdateResultsFilter();
            UpdateHeaderSavedCount();
        }
    }

    #endregion

    #region Settings Tab Operations

    private void LoadSettingsToUi()
    {
        if (SettingSliderThreads != null)
        {
            SettingSliderThreads.Value = _settings.DefaultThreads;
            TxtSettingThreads.Text = _settings.DefaultThreads.ToString();
        }
        if (TxtSettingTimeout != null) TxtSettingTimeout.Text = _settings.TimeoutSeconds.ToString();
        if (TxtSettingUserAgent != null) TxtSettingUserAgent.Text = _settings.CustomUserAgent;
        if (ChkAutoSave != null) ChkAutoSave.IsChecked = _settings.AutoSaveResults;
        if (TxtSettingSaveDir != null) TxtSettingSaveDir.Text = _settings.SaveDirectory;
        if (ChkAutoScrollLog != null) ChkAutoScrollLog.IsChecked = _settings.AutoScrollLog;

        if (RadioThemeDark != null && RadioThemeLight != null)
        {
            if (_settings.IsDarkMode) RadioThemeDark.IsChecked = true;
            else RadioThemeLight.IsChecked = true;
        }

        if (TxtSettingDefaultProxies != null)
        {
            TxtSettingDefaultProxies.Text = _settings.DefaultProxies;
        }
        UpdateSavedProxyCountDisplay();
    }

    private void UpdateSavedProxyCountDisplay()
    {
        if (TxtSettingSavedProxyCount == null) return;
        if (string.IsNullOrWhiteSpace(_settings.DefaultProxies))
        {
            TxtSettingSavedProxyCount.Text = "0 proxy items saved (None)";
        }
        else
        {
            var temp = new ProxyManager();
            temp.LoadFromText(_settings.DefaultProxies);
            TxtSettingSavedProxyCount.Text = $"{temp.Count} valid proxy item(s) saved";
        }
    }

    private void BtnSaveCurrentAsDefaultProxy_Click(object sender, RoutedEventArgs e)
    {
        BtnSaveProxies_Click(sender, e);
    }

    private void BtnClearSavedDefaultProxy_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("Are you sure you want to remove the saved default proxies from settings?", "Clear Default Proxies", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            _settings.DefaultProxies = "";
            _settings.Save();
            if (TxtSettingDefaultProxies != null)
            {
                TxtSettingDefaultProxies.Text = string.Empty;
            }
            UpdateSavedProxyCountDisplay();
            AppendLog("[PROXY] Saved default proxies removed from settings.");
        }
    }

    private void SettingSliderThreads_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TxtSettingThreads != null)
        {
            TxtSettingThreads.Text = ((int)SettingSliderThreads.Value).ToString();
        }
    }

    private void BtnBrowseSaveDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Results Output Folder",
            InitialDirectory = Directory.Exists(_settings.SaveDirectory) ? _settings.SaveDirectory : AppDomain.CurrentDomain.BaseDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            TxtSettingSaveDir.Text = dialog.FolderName;
        }
    }

    private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings.DefaultThreads = (int)SettingSliderThreads.Value;
            if (int.TryParse(TxtSettingTimeout.Text, out int timeout) && timeout > 0)
            {
                _settings.TimeoutSeconds = timeout;
            }
            _settings.CustomUserAgent = TxtSettingUserAgent.Text?.Trim() ?? _settings.CustomUserAgent;
            _settings.AutoSaveResults = ChkAutoSave.IsChecked == true;
            _settings.SaveDirectory = string.IsNullOrWhiteSpace(TxtSettingSaveDir.Text) ? _settings.SaveDirectory : TxtSettingSaveDir.Text.Trim();
            _settings.AutoScrollLog = ChkAutoScrollLog.IsChecked == true;

            if (TxtSettingDefaultProxies != null)
            {
                string newProxies = TxtSettingDefaultProxies.Text.Trim();
                _settings.DefaultProxies = newProxies;
                if (string.IsNullOrWhiteSpace(TxtProxies.Text) && !string.IsNullOrWhiteSpace(newProxies))
                {
                    TxtProxies.Text = newProxies;
                }
            }
            UpdateSavedProxyCountDisplay();

            bool isDark = RadioThemeDark?.IsChecked == true;
            if (_settings.IsDarkMode != isDark)
            {
                _settings.IsDarkMode = isDark;
                ThemeManager.SetTheme(this.Resources, isDark);
            }

            _settings.Save();

            // Synchronize runner slider with default threads
            SliderThreads.Value = _settings.DefaultThreads;

            TxtSettingsStatus.Text = "Settings saved successfully!";
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            timer.Tick += (s, args) => { TxtSettingsStatus.Text = ""; timer.Stop(); };
            timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show("Reset settings to factory defaults?", "Reset", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res == MessageBoxResult.Yes)
        {
            var def = new AppSettings();
            _settings.DefaultThreads = def.DefaultThreads;
            _settings.TimeoutSeconds = def.TimeoutSeconds;
            _settings.CustomUserAgent = def.CustomUserAgent;
            _settings.AutoSaveResults = def.AutoSaveResults;
            _settings.SaveDirectory = def.SaveDirectory;
            _settings.AutoScrollLog = def.AutoScrollLog;
            _settings.IsDarkMode = def.IsDarkMode;
            _settings.DefaultProxies = def.DefaultProxies;
            _settings.Save();

            ThemeManager.SetTheme(this.Resources, _settings.IsDarkMode);
            LoadSettingsToUi();
            SliderThreads.Value = def.DefaultThreads;
            TxtSettingsStatus.Text = "Settings restored to defaults.";
        }
    }

    #endregion
}