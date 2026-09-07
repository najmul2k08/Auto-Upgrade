using System;
using System.IO;
using System.Text.Json;

namespace AutoUpgrade.Models;

public class AppSettings
{
    public int DefaultThreads { get; set; } = 5;
    public int TimeoutSeconds { get; set; } = 35;
    public bool AutoSaveResults { get; set; } = true;
    public string SaveDirectory { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results");
    public string CustomUserAgent { get; set; } = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";
    public string DefaultProxies { get; set; } = "";
    public bool AutoScrollLog { get; set; } = true;
    public bool IsDarkMode { get; set; } = false;

    private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    if (string.IsNullOrWhiteSpace(settings.SaveDirectory))
                    {
                        settings.SaveDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results");
                    }
                    return settings;
                }
            }
        }
        catch
        {
            // Fallback to defaults on error
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Ignore write errors
        }
    }
}

