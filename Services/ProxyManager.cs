using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AutoUpgrade.Models;

namespace AutoUpgrade.Services;

public class ProxyManager
{
    private readonly List<ProxyItem> _proxies = new();
    private readonly object _lock = new();
    private int _currentIndex = 0;

    public int Count
    {
        get
        {
            lock (_lock) return _proxies.Count;
        }
    }

    public void LoadFromText(string text)
    {
        lock (_lock)
        {
            _proxies.Clear();
            _currentIndex = 0;

            if (string.IsNullOrWhiteSpace(text)) return;

            using var reader = new StringReader(text);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (ProxyItem.TryParse(line, out var proxy) && proxy != null)
                {
                    _proxies.Add(proxy);
                }
            }
        }
    }

    public void LoadFromFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            string content = File.ReadAllText(filePath);
            LoadFromText(content);
        }
    }

    public ProxyItem? GetNextProxy()
    {
        lock (_lock)
        {
            if (_proxies.Count == 0) return null;
            var proxy = _proxies[_currentIndex % _proxies.Count];
            _currentIndex = (_currentIndex + 1) % _proxies.Count;
            return proxy;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _proxies.Clear();
            _currentIndex = 0;
        }
    }
}

