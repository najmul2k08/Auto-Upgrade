using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AutoUpgrade.Models;
using AutoUpgrade.Services;

namespace AutoUpgrade.Tests;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("================ RUNNING END-TO-END ENGINE TEST ================");

        // 1. Setup ProxyManager
        string proxyString = "residential-budget.novaproxy.io:6969:9ortphoir9:8xw7au99kq";
        var proxyManager = new ProxyManager();
        proxyManager.LoadFromText(proxyString);
        Console.WriteLine($"Proxies loaded: {proxyManager.Count}");

        // 2. Load 2 real accounts from C:\Users\najmo\Desktop\New
        string newDir = @"C:\Users\najmo\Desktop\New";
        var files = Directory.GetFiles(newDir, "*.txt").Take(2).ToList();

        var tasks = files.SelectMany(f => TokenExtractor.ExtractFromFile(f)).ToList();
        for (int i = 0; i < tasks.Count; i++) tasks[i].Id = i + 1;
        Console.WriteLine($"Accounts loaded: {tasks.Count}");

        // 3. Run WorkerEngine
        var engine = new WorkerEngine(proxyManager);
        engine.LogMessage += msg => Console.WriteLine($"[ENGINE] {msg}");

        await engine.StartAsync(tasks, threadCount: 2);

        Console.WriteLine("\n================ TEST SUMMARY ================");
        foreach (var t in tasks)
        {
            Console.WriteLine($"#{t.Id} | Email: {t.Email} | Status: {t.Status} | StatusType: {t.StatusType} | Message: {t.Message}");
        }
    }
}
