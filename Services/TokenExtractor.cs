using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AutoUpgrade.Models;

namespace AutoUpgrade.Services;

public static class TokenExtractor
{
    // Regex for key-value / cookie header format: GR_TOKEN=... or GR_TOKEN: ...
    private static readonly Regex KeyValueTokenRegex = new(
        @"(?<![""']\s*:\s*[""']?)(?:GR_TOKEN|gr_token)\s*[=:\t]\s*[""']?([A-Za-z0-9_\-\.]{20,})[""']?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex KeyValueRefreshRegex = new(
        @"(?<![""']\s*:\s*[""']?)(?:GR_REFRESH|gr_refresh)\s*[=:\t]\s*[""']?([A-Za-z0-9_\-\.]{20,})[""']?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // Regex for JSON cookie objects: {"name":"GR_TOKEN","value":"..."} or {"value":"...","name":"GR_TOKEN"}
    private static readonly Regex JsonTokenRegex = new(
        @"\{(?:[^{}]*?""name""\s*:\s*""GR_TOKEN""[^{}]*?""value""\s*:\s*""([^""]+)""|[^{}]*?""value""\s*:\s*""([^""]+)""[^{}]*?""name""\s*:\s*""GR_TOKEN"")[^{}]*?\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex JsonRefreshRegex = new(
        @"\{(?:[^{}]*?""name""\s*:\s*""GR_REFRESH""[^{}]*?""value""\s*:\s*""([^""]+)""|[^{}]*?""value""\s*:\s*""([^""]+)""[^{}]*?""name""\s*:\s*""GR_REFRESH"")[^{}]*?\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex EmailHeaderRegex = new(
        @"Email:\s*([^\r\n\s]+@[^\r\n\s]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static List<AccountTask> ExtractFromFile(string filePath)
    {
        var list = new List<AccountTask>();
        if (!File.Exists(filePath)) return list;

        string fileName = Path.GetFileName(filePath);
        string content = File.ReadAllText(filePath);

        var tasks = ExtractFromString(content, fileName);
        list.AddRange(tasks);
        return list;
    }

    public static List<AccountTask> ExtractFromString(string content, string fileName = "direct_input")
    {
        var results = new List<AccountTask>();
        if (string.IsNullOrWhiteSpace(content)) return results;

        // METHOD 1: Scan for JSON array blocks [...] in text (e.g. accounts with metadata and embedded JSON cookies)
        var jsonBlockTasks = ExtractFromJsonArrayBlocks(content, fileName);
        if (jsonBlockTasks.Count > 0)
        {
            return jsonBlockTasks;
        }

        // METHOD 2: Regex extraction for JSON object cookies {"name":"GR_TOKEN", "value":"..."}
        var jsonCookieTasks = ExtractFromJsonCookieRegex(content, fileName);
        if (jsonCookieTasks.Count > 0)
        {
            return jsonCookieTasks;
        }

        // METHOD 3: Key-Value / Cookie format (GR_TOKEN=xxx; GR_REFRESH=yyy)
        var kvTasks = ExtractFromKeyValue(content, fileName);
        if (kvTasks.Count > 0)
        {
            return kvTasks;
        }

        return results;
    }

    /// <summary>
    /// Finds all JSON array blocks `[...]` in the text, parses them as cookie arrays, and associates nearby account metadata.
    /// </summary>
    private static List<AccountTask> ExtractFromJsonArrayBlocks(string content, string fileName)
    {
        var tasks = new List<AccountTask>();
        int startIndex = 0;

        while (startIndex < content.Length)
        {
            int arrayStart = content.IndexOf('[', startIndex);
            if (arrayStart == -1) break;

            // Find matching closing bracket ']' accounting for nesting/strings
            int arrayEnd = FindMatchingBracket(content, arrayStart);
            if (arrayEnd == -1)
            {
                startIndex = arrayStart + 1;
                continue;
            }

            string jsonSnippet = content.Substring(arrayStart, arrayEnd - arrayStart + 1);

            try
            {
                using var doc = JsonDocument.Parse(jsonSnippet);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    string? token = null;
                    string? refresh = null;

                    foreach (var element in doc.RootElement.EnumerateArray())
                    {
                        if (element.TryGetProperty("name", out var nameProp) &&
                            element.TryGetProperty("value", out var valProp))
                        {
                            string name = nameProp.GetString() ?? "";
                            string val = valProp.GetString() ?? "";

                            if (string.Equals(name, "GR_TOKEN", StringComparison.OrdinalIgnoreCase))
                                token = val;
                            else if (string.Equals(name, "GR_REFRESH", StringComparison.OrdinalIgnoreCase))
                                refresh = val;
                        }
                    }

                    if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(refresh))
                    {
                        var task = CreateTask(token, refresh, fileName);

                        // Look backwards in content for "Email: ..." header
                        int lookbackStart = Math.Max(0, arrayStart - 500);
                        string priorText = content.Substring(lookbackStart, arrayStart - lookbackStart);
                        var emailMatch = EmailHeaderRegex.Match(priorText);
                        if (emailMatch.Success)
                        {
                            task.Email = emailMatch.Groups[1].Value.Trim();
                        }

                        tasks.Add(task);
                    }
                }
            }
            catch
            {
                // Snippet wasn't valid JSON, continue searching
            }

            startIndex = arrayEnd + 1;
        }

        return tasks;
    }

    private static int FindMatchingBracket(string s, int openPos)
    {
        int depth = 0;
        bool inString = false;
        bool escape = false;

        for (int i = openPos; i < s.Length; i++)
        {
            char c = s[i];

            if (escape)
            {
                escape = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (!inString)
            {
                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Uses regex to find individual JSON objects containing GR_TOKEN and GR_REFRESH.
    /// </summary>
    private static List<AccountTask> ExtractFromJsonCookieRegex(string content, string fileName)
    {
        var tasks = new List<AccountTask>();

        var tokenMatches = JsonTokenRegex.Matches(content);
        var refreshMatches = JsonRefreshRegex.Matches(content);

        if (tokenMatches.Count > 0 && refreshMatches.Count > 0)
        {
            int count = Math.Min(tokenMatches.Count, refreshMatches.Count);
            for (int i = 0; i < count; i++)
            {
                string token = tokenMatches[i].Groups[1].Success
                    ? tokenMatches[i].Groups[1].Value
                    : tokenMatches[i].Groups[2].Value;

                string refresh = refreshMatches[i].Groups[1].Success
                    ? refreshMatches[i].Groups[1].Value
                    : refreshMatches[i].Groups[2].Value;

                if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(refresh))
                {
                    tasks.Add(CreateTask(token, refresh, fileName));
                }
            }
        }

        return tasks;
    }

    /// <summary>
    /// Parses key-value or cookie string format: GR_TOKEN=...; GR_REFRESH=...
    /// </summary>
    private static List<AccountTask> ExtractFromKeyValue(string content, string fileName)
    {
        var tasks = new List<AccountTask>();

        var tokenMatches = KeyValueTokenRegex.Matches(content);
        var refreshMatches = KeyValueRefreshRegex.Matches(content);

        if (tokenMatches.Count > 0 && refreshMatches.Count > 0)
        {
            int count = Math.Min(tokenMatches.Count, refreshMatches.Count);
            for (int i = 0; i < count; i++)
            {
                string token = tokenMatches[i].Groups[1].Value.Trim();
                string refresh = refreshMatches[i].Groups[1].Value.Trim();

                tasks.Add(CreateTask(token, refresh, fileName));
            }
            return tasks;
        }

        // Line by line fallback
        using var reader = new StringReader(content);
        string? line;
        string? currentToken = null;
        string? currentRefresh = null;

        while ((line = reader.ReadLine()) != null)
        {
            var tMatch = KeyValueTokenRegex.Match(line);
            var rMatch = KeyValueRefreshRegex.Match(line);

            if (tMatch.Success) currentToken = tMatch.Groups[1].Value.Trim();
            if (rMatch.Success) currentRefresh = rMatch.Groups[1].Value.Trim();

            if (!string.IsNullOrEmpty(currentToken) && !string.IsNullOrEmpty(currentRefresh))
            {
                tasks.Add(CreateTask(currentToken, currentRefresh, fileName));
                currentToken = null;
                currentRefresh = null;
            }
        }

        return tasks;
    }

    private static AccountTask CreateTask(string token, string refresh, string fileName)
    {
        var task = new AccountTask
        {
            FileName = fileName,
            GrToken = token,
            GrRefresh = refresh,
            Plan = "-",
            Subscription = "-",
            AvailableCredits = "-",
            StatusType = TaskStatusType.Pending,
            Status = "Pending",
            Message = "Ready to process",
            Timestamp = DateTime.Now.ToString("HH:mm:ss")
        };

        // Extract email & user_id from JWT payload
        TryExtractJwtInfo(token, task);

        return task;
    }

    public static void TryExtractJwtInfo(string jwtToken, AccountTask task)
    {
        try
        {
            var parts = jwtToken.Split('.');
            if (parts.Length < 2) return;

            string payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            byte[] bytes = Convert.FromBase64String(payload);
            string json = Encoding.UTF8.GetString(bytes);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (string.IsNullOrEmpty(task.Email))
            {
                if (root.TryGetProperty("email", out var emailProp))
                {
                    task.Email = emailProp.GetString() ?? "";
                }
                else if (root.TryGetProperty("name", out var nameProp))
                {
                    task.Email = nameProp.GetString() ?? "";
                }
            }

            if (string.IsNullOrEmpty(task.UserId))
            {
                if (root.TryGetProperty("accounts_user_id", out var accUserProp))
                {
                    if (accUserProp.ValueKind == JsonValueKind.Number)
                    {
                        task.UserId = accUserProp.GetInt64().ToString();
                    }
                    else
                    {
                        task.UserId = accUserProp.GetString() ?? "";
                    }
                }
                else if (root.TryGetProperty("user_id", out var userProp))
                {
                    task.UserId = userProp.GetString() ?? "";
                }
                else if (root.TryGetProperty("sub", out var subProp))
                {
                    task.UserId = subProp.GetString() ?? "";
                }
            }
        }
        catch
        {
            // Ignore JWT decode failure
        }
    }
}
