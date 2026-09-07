using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoUpgrade.Models;
using AutoUpgrade.Services;

namespace AutoUpgrade.Tests;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("================ RUNNING DEDUPLICATION TESTS ================");

        // Test 1: Deduplicate list with 2 inputs having same email
        var batch1 = new List<AccountTask>
        {
            new() { Email = "user1@example.com", GrToken = "token1", UserId = "101" },
            new() { Email = "user2@example.com", GrToken = "token2", UserId = "102" },
            new() { Email = "user1@example.com", GrToken = "token3", UserId = "103" } // Duplicate email
        };

        var (unique1, dup1) = TokenExtractor.Deduplicate(batch1);
        Assert(unique1.Count == 2, $"Expected 2 unique tasks, got {unique1.Count}");
        Assert(dup1 == 1, $"Expected 1 duplicate, got {dup1}");
        Assert(unique1[0].Email == "user1@example.com" && unique1[1].Email == "user2@example.com", "Retained accounts match");
        Console.WriteLine("[PASS] Test 1: Same email deduplication retains only the first input.");

        // Test 2: Case insensitivity
        var batch2 = new List<AccountTask>
        {
            new() { Email = "ALEXEI.P@helpware.io", GrToken = "tokenA" },
            new() { Email = "alexei.p@HELPWARE.IO", GrToken = "tokenB" }
        };
        var (unique2, dup2) = TokenExtractor.Deduplicate(batch2);
        Assert(unique2.Count == 1, $"Expected 1 unique task, got {unique2.Count}");
        Assert(dup2 == 1, $"Expected 1 duplicate, got {dup2}");
        Console.WriteLine("[PASS] Test 2: Case-insensitive email comparison works.");

        // Test 3: Deduplicating against existing tasks in queue
        var existing = new List<AccountTask>
        {
            new() { Email = "existing@example.com", GrToken = "token_exist" }
        };
        var incoming = new List<AccountTask>
        {
            new() { Email = "new@example.com", GrToken = "token_new" },
            new() { Email = "existing@example.com", GrToken = "token_dup_incoming" }
        };
        var (unique3, dup3) = TokenExtractor.Deduplicate(incoming, existing);
        Assert(unique3.Count == 1, $"Expected 1 unique task, got {unique3.Count}");
        Assert(dup3 == 1, $"Expected 1 duplicate, got {dup3}");
        Assert(unique3[0].Email == "new@example.com", "Retained only non-duplicate incoming task");
        Console.WriteLine("[PASS] Test 3: Incoming accounts matching existing queue are skipped.");

        // Test 4: Real file test with duplicates
        string newDir = @"C:\Users\najmo\Desktop\New";
        if (Directory.Exists(newDir))
        {
            var files = Directory.GetFiles(newDir, "*.txt").Take(5).ToList();
            var duplicateFiles = files.Concat(files).ToList(); // 10 files, 5 duplicates
            var loaded = duplicateFiles.SelectMany(f => TokenExtractor.ExtractFromFile(f)).ToList();
            var (unique4, dup4) = TokenExtractor.Deduplicate(loaded);
            Assert(unique4.Count == 5, $"Expected 5 unique accounts from 10 duplicate files, got {unique4.Count}");
            Assert(dup4 == 5, $"Expected 5 duplicates, got {dup4}");
            Console.WriteLine($"[PASS] Test 4: Loaded {loaded.Count} tasks from duplicated files -> {unique4.Count} unique, {dup4} duplicates filtered.");
        }

        Console.WriteLine("\nALL DEDUPLICATION TESTS PASSED SUCCESSFULLY!");
    }

    static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] Assertion failed: {message}");
            Console.ResetColor();
            Environment.Exit(1);
        }
    }
}
