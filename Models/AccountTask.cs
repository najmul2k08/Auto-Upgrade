using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AutoUpgrade.Models;

public enum TaskStatusType
{
    Pending,
    Running,
    Approved,
    Declined,
    Error
}

public class AccountTask : INotifyPropertyChanged
{
    private int _id;
    private string _fileName = string.Empty;
    private string _grToken = string.Empty;
    private string _grRefresh = string.Empty;
    private string _email = string.Empty;
    private string _plan = "-";
    private string _subscription = "-";
    private string _availableCredits = "-";
    private string _userId = string.Empty;
    private string _purchaseId = string.Empty;
    private string _priceId = string.Empty;
    private TaskStatusType _statusType = TaskStatusType.Pending;
    private string _status = "Pending";
    private string _message = string.Empty;
    private string _proxyUsed = "-";
    private string _timestamp = "-";

    public int Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string FileName
    {
        get => _fileName;
        set => SetField(ref _fileName, value);
    }

    public string GrToken
    {
        get => _grToken;
        set => SetField(ref _grToken, value);
    }

    public string GrRefresh
    {
        get => _grRefresh;
        set => SetField(ref _grRefresh, value);
    }

    public string Email
    {
        get => _email;
        set => SetField(ref _email, value);
    }

    public string Plan
    {
        get => _plan;
        set => SetField(ref _plan, value);
    }

    public string Subscription
    {
        get => _subscription;
        set => SetField(ref _subscription, value);
    }

    public string AvailableCredits
    {
        get => _availableCredits;
        set => SetField(ref _availableCredits, value);
    }

    public string UserId
    {
        get => _userId;
        set => SetField(ref _userId, value);
    }

    public string PurchaseId
    {
        get => _purchaseId;
        set => SetField(ref _purchaseId, value);
    }

    public string PriceId
    {
        get => _priceId;
        set => SetField(ref _priceId, value);
    }

    public TaskStatusType StatusType
    {
        get => _statusType;
        set
        {
            if (SetField(ref _statusType, value))
            {
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusForegroundBrush));
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.Brush StatusBrush => Services.ThemeManager.GetStatusBrush(StatusType);

    [System.Text.Json.Serialization.JsonIgnore]
    public System.Windows.Media.Brush StatusForegroundBrush => Services.ThemeManager.GetStatusForegroundBrush(StatusType);

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string Message
    {
        get => _message;
        set => SetField(ref _message, value);
    }

    public string ProxyUsed
    {
        get => _proxyUsed;
        set => SetField(ref _proxyUsed, value);
    }

    public string Timestamp
    {
        get => _timestamp;
        set => SetField(ref _timestamp, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

