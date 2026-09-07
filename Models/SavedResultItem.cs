using System;

namespace AutoUpgrade.Models;

public class SavedResultItem
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string PurchaseId { get; set; } = string.Empty;
    public string PriceId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public TaskStatusType StatusType { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.Brush StatusBrush => Services.ThemeManager.GetStatusBrush(StatusType);

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.Brush StatusForegroundBrush => Services.ThemeManager.GetStatusForegroundBrush(StatusType);

    public string Message { get; set; } = string.Empty;
    public string ProxyUsed { get; set; } = string.Empty;
    public string CompletedAt { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}

