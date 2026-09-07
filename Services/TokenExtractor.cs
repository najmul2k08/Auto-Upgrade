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
            var (unique, _) = Deduplicate(jsonBlockTasks);
            return unique;
        }

        // METHOD 2: Regex extraction for JSON object cookies {"name":"GR_TOKEN", "value":"..."}
        var jsonCookieTasks = ExtractFromJsonCookieRegex(content, fileName);
        if (jsonCookieTasks.Count > 0)
        {
            var (unique, _) = Deduplicate(jsonCookieTasks);
            return unique;
        }

        // METHOD 3: Key-Value / Cookie format (GR_TOKEN=xxx; GR_REFRESH=yyy)
        var kvTasks = ExtractFromKeyValue(content, fileName);
        if (kvTasks.Count > 0)
        {
            var (unique, _) = Deduplicate(kvTasks);
            return unique;
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
                    tasks.Add(CreateTask(token, refresh, fileName, content));
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

                tasks.Add(CreateTask(token, refresh, fileName, content));
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
                tasks.Add(CreateTask(currentToken, currentRefresh, fileName, content));
                currentToken = null;
                currentRefresh = null;
            }
        }

        return tasks;
    }

    private static AccountTask CreateTask(string token, string refresh, string fileName, string? sourceText = null)
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

        // 1. Extract email & user_id from JWT payload
        TryExtractJwtInfo(token, task);

        // 2. Fallback email extraction: check sourceText (e.g. "Email: user@example.com")
        if ((string.IsNullOrWhiteSpace(task.Email) || !task.Email.Contains('@')) && !string.IsNullOrWhiteSpace(sourceText))
        {
            var headerMatch = EmailHeaderRegex.Match(sourceText);
            if (headerMatch.Success)
            {
                task.Email = headerMatch.Groups[1].Value.Trim();
            }
            else
            {
                var rawEmailMatch = Regex.Match(sourceText, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
                if (rawEmailMatch.Success)
                {
                    task.Email = rawEmailMatch.Value.Trim();
                }
            }
        }

        // 3. Fallback email extraction: check fileName (e.g. "... [user@example.com].txt")
        if ((string.IsNullOrWhiteSpace(task.Email) || !task.Email.Contains('@')) && !string.IsNullOrWhiteSpace(fileName))
        {
            var fileEmailMatch = Regex.Match(fileName, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}");
            if (fileEmailMatch.Success)
            {
                task.Email = fileEmailMatch.Value.Trim();
            }
        }

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

            if (string.IsNullOrEmpty(task.Email) || !task.Email.Contains('@'))
            {
                if (root.TryGetProperty("email", out var emailProp) && emailProp.GetString() is { } em && em.Contains('@'))
                {
                    task.Email = em.Trim();
                }
                else if (root.TryGetProperty("user_email", out var uEmailProp) && uEmailProp.GetString() is { } uem && uem.Contains('@'))
                {
                    task.Email = uem.Trim();
                }
                else if (root.TryGetProperty("sub_email", out var sEmailProp) && sEmailProp.GetString() is { } sem && sem.Contains('@'))
                {
                    task.Email = sem.Trim();
                }
                else if (root.TryGetProperty("name", out var nameProp) && nameProp.GetString() is { } nm && nm.Contains('@'))
                {
                    task.Email = nm.Trim();
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

    /// <summary>
    /// Filters out duplicate tasks. If there are 2 or more inputs with the same email, only the first one is retained.
    /// Also falls back to UserId or GrToken if email is unavailable.
    /// </summary>
    public static (List<AccountTask> UniqueTasks, int DuplicateCount) Deduplicate(
        IEnumerable<AccountTask> incomingTasks,
        IEnumerable<AccountTask>? existingTasks = null)
    {
        var uniqueTasks = new List<AccountTask>();
        int duplicateCount = 0;

        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenUserIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);

        if (existingTasks != null)
        {
            foreach (var task in existingTasks)
            {
                if (!string.IsNullOrWhiteSpace(task.Email))
                    seenEmails.Add(task.Email.Trim());

                if (!string.IsNullOrWhiteSpace(task.UserId))
                    seenUserIds.Add(task.UserId.Trim());

                if (!string.IsNullOrWhiteSpace(task.GrToken))
                    seenTokens.Add(task.GrToken.Trim());
            }
        }

        foreach (var task in incomingTasks)
        {
            bool isDuplicate = false;

            // 1. Primary rule: if there are 2 inputs with the same email, keep only one
            if (!string.IsNullOrWhiteSpace(task.Email))
            {
                if (seenEmails.Contains(task.Email.Trim()))
                {
                    isDuplicate = true;
                }
            }
            // 2. Secondary rule: check by UserId if email is not available
            else if (!string.IsNullOrWhiteSpace(task.UserId))
            {
                if (seenUserIds.Contains(task.UserId.Trim()))
                {
                    isDuplicate = true;
                }
            }
            // 3. Tertiary rule: check by Token if email and userId are not available
            else if (!string.IsNullOrWhiteSpace(task.GrToken))
            {
                if (seenTokens.Contains(task.GrToken.Trim()))
                {
                    isDuplicate = true;
                }
            }

            if (isDuplicate)
            {
                duplicateCount++;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(task.Email))
                seenEmails.Add(task.Email.Trim());

            if (!string.IsNullOrWhiteSpace(task.UserId))
                seenUserIds.Add(task.UserId.Trim());

            if (!string.IsNullOrWhiteSpace(task.GrToken))
                seenTokens.Add(task.GrToken.Trim());

            uniqueTasks.Add(task);
        }

        return (uniqueTasks, duplicateCount);
    }
}
