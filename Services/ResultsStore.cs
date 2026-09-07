using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using AutoUpgrade.Models;

namespace AutoUpgrade.Services;

public class ResultsStore
{
    private readonly object _lock = new();
    private readonly string _historyFilePath;
    private readonly AppSettings _settings;

    public ObservableCollection<SavedResultItem> AllResults { get; } = new();

    public ResultsStore(AppSettings settings)
    {
        _settings = settings;
        string dir = _settings.SaveDirectory;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _historyFilePath = Path.Combine(dir, "history.json");
        LoadHistory();
    }

    public void AddResult(AccountTask task)
    {
        lock (_lock)
        {
            var item = new SavedResultItem
            {
                Id = AllResults.Count + 1,
                Email = task.Email,
                UserId = task.UserId,
                PurchaseId = task.PurchaseId,
                PriceId = task.PriceId,
                Status = task.Status,
                StatusType = task.StatusType,
                Message = task.Message,
                ProxyUsed = task.ProxyUsed,
                CompletedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                FileName = task.FileName
            };

            AllResults.Insert(0, item);

            // Auto-save to text log files if enabled
            if (_settings.AutoSaveResults)
            {
                AutoSaveToFile(item);
            }

            SaveHistory();
        }
    }

    private void AutoSaveToFile(SavedResultItem item)
    {
        try
        {
            string dir = _settings.SaveDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string dateStr = DateTime.Now.ToString("yyyy-MM-dd");
            string fileName = item.StatusType switch
            {
                TaskStatusType.Approved => $"approved_{dateStr}.txt",
                TaskStatusType.Declined => $"declined_{dateStr}.txt",
                _ => $"errors_{dateStr}.txt"
            };

            string path = Path.Combine(dir, fileName);
            string line = $"[{item.CompletedAt}] [{item.Status}] Email: {item.Email} | CustomerID: {item.UserId} | PurchaseID: {item.PurchaseId} | PriceID: {item.PriceId} | Details: {item.Message} | Proxy: {item.ProxyUsed}";
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch
        {
            // Ignore auto-save write errors
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            AllResults.Clear();
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    File.Delete(_historyFilePath);
                }
            }
            catch
            {
                // Ignore
            }
        }
    }

    private void SaveHistory()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            // Save top 1000 items to keep history file fast
            var list = AllResults.Take(1000).ToList();
            string json = JsonSerializer.Serialize(list, options);
            File.WriteAllText(_historyFilePath, json);
        }
        catch
        {
            // Ignore write errors
        }
    }

    private void LoadHistory()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    string json = File.ReadAllText(_historyFilePath);
                    var items = JsonSerializer.Deserialize<List<SavedResultItem>>(json);
                    if (items != null)
                    {
                        AllResults.Clear();
                        foreach (var item in items)
                        {
                            AllResults.Add(item);
                        }
                    }
                }
            }
            catch
            {
                // Ignore load errors
            }
        }
    }

    public string ExportCsv(IEnumerable<SavedResultItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ID,CompletedAt,Status,Email,CustomerId,PurchaseId,PriceId,Message,Proxy,SourceFile");
        foreach (var it in items)
        {
            sb.AppendLine($"\"{it.Id}\",\"{it.CompletedAt}\",\"{it.Status}\",\"{it.Email}\",\"{it.UserId}\",\"{it.PurchaseId}\",\"{it.PriceId}\",\"{it.Message.Replace("\"", "\"\"")}\",\"{it.ProxyUsed}\",\"{it.FileName}\"");
        }
        return sb.ToString();
    }

    public string ExportTxt(IEnumerable<SavedResultItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("==========================================================================");
        sb.AppendLine($"MAGNIFIC AUTO UPGRADE SAVED RESULTS - Generated on {DateTime.Now}");
        sb.AppendLine("==========================================================================\n");

        foreach (var it in items)
        {
            sb.AppendLine($"[{it.CompletedAt}] [{it.Status.ToUpper()}] #{it.Id} | Email: {it.Email} | CustomerID: {it.UserId}");
            sb.AppendLine($"PurchaseID: {it.PurchaseId} | PriceID: {it.PriceId}");
            sb.AppendLine($"Details: {it.Message} | Proxy: {it.ProxyUsed} | File: {it.FileName}");
            sb.AppendLine(new string('-', 70));
        }
        return sb.ToString();
    }
}

