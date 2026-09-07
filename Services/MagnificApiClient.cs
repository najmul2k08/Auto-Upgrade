using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AutoUpgrade.Models;

namespace AutoUpgrade.Services;

public class MagnificApiClient
{
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";
    private static readonly Regex PurchaseIdRegex = new(@"[""']?purchaseId[""']?\s*:\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PriceIdRegex = new(@"[""']?priceId[""']?\s*:\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task ExecuteUpgradeAsync(AccountTask task, ProxyItem? proxy, Action<string> log, CancellationToken cancellationToken)
    {
        string cookieHeader = $"GR_TOKEN={task.GrToken}; GR_REFRESH={task.GrRefresh};";

        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(25),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        if (proxy != null)
        {
            handler.UseProxy = true;
            handler.Proxy = proxy.ToWebProxy();
            task.ProxyUsed = proxy.ToString();
        }
        else
        {
            handler.UseProxy = false;
            task.ProxyUsed = "Direct (No Proxy)";
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(35)
        };

        try
        {
            // -------------------------------------------------------------
            // STEP 1: GET https://www.magnific.com/app (Resolve Customer ID)
            // -------------------------------------------------------------
            task.Status = "Step 1/4: Resolving User ID...";
            task.StatusType = TaskStatusType.Running;

            if (string.IsNullOrEmpty(task.UserId))
            {
                TokenExtractor.TryExtractJwtInfo(task.GrToken, task);
            }

            if (!string.IsNullOrEmpty(task.UserId))
            {
                log($"[{task.Id}] Step 1: Customer ID identified from token: {task.UserId}");
            }
            else
            {
                log($"[{task.Id}] Step 1: GET https://www.magnific.com/app (Proxy: {task.ProxyUsed})");

                try
                {
                    using var req1 = new HttpRequestMessage(HttpMethod.Get, "https://www.magnific.com/app");
                    req1.Headers.Host = "www.magnific.com";
                    req1.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                    req1.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
                    req1.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                    req1.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
                    req1.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

                    using var res1 = await client.SendAsync(req1, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    
                    foreach (var header in res1.Headers)
                    {
                        if (string.Equals(header.Key, "x-session-user-id", StringComparison.OrdinalIgnoreCase))
                        {
                            task.UserId = string.Join(",", header.Value).Trim();
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    log($"[{task.Id}] Step 1 notice: {ex.Message}");
                }

                if (string.IsNullOrEmpty(task.UserId))
                {
                    task.Status = "Error: user_id missing";
                    task.StatusType = TaskStatusType.Error;
                    task.Message = "Could not find x-session-user-id header or valid token customer ID.";
                    log($"[{task.Id}] ❌ Error: customerId not found.");
                    return;
                }

                log($"[{task.Id}] Got user_id: {task.UserId}");
            }

            // -------------------------------------------------------------
            // STEP 2: GET https://www.magnific.com/user/api/my-subscriptions
            // -------------------------------------------------------------
            task.Status = "Step 2/4: Getting Subscriptions...";
            log($"[{task.Id}] Step 2: GET https://www.magnific.com/user/api/my-subscriptions");

            using (var req2 = new HttpRequestMessage(HttpMethod.Get, "https://www.magnific.com/user/api/my-subscriptions"))
            {
                req2.Headers.Host = "www.magnific.com";
                req2.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                req2.Headers.TryAddWithoutValidation("Accept", "*/*");
                req2.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                req2.Headers.TryAddWithoutValidation("Referer", "https://www.magnific.com/user/my-subscriptions?upgrade=plan&selected-plan=MAGNIFIC&selected-frequency=yearly&origin_cta=pricing_upgrade");
                req2.Headers.TryAddWithoutValidation("sec-fetch-site", "same-origin");
                req2.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
                req2.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
                req2.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

                using var res2 = await client.SendAsync(req2, cancellationToken);
                string body2 = await res2.Content.ReadAsStringAsync(cancellationToken);

                var pMatch = PurchaseIdRegex.Match(body2);
                if (!pMatch.Success)
                {
                    task.Status = "No Active Subscription";
                    task.StatusType = TaskStatusType.Declined;
                    task.Message = "Account has no active subscription (Free account).";
                    log($"[{task.Id}] ⚠️ Account has no active subscription (purchaseId not found).");
                    return;
                }

                task.PurchaseId = pMatch.Groups[1].Value;
                log($"[{task.Id}] Got purchaseId: {task.PurchaseId}");
            }

            // -------------------------------------------------------------
            // STEP 3: GET https://www.magnific.com/user/api/my-subscriptions/upgrade-product
            // -------------------------------------------------------------
            // -------------------------------------------------------------
            // STEP 3: GET https://www.magnific.com/user/api/my-subscriptions/upgrade-product
            // -------------------------------------------------------------
            task.Status = "Step 3/4: Getting Price ID...";
            string upgradeUrl = $"https://www.magnific.com/user/api/my-subscriptions/upgrade-product?customerId={Uri.EscapeDataString(task.UserId)}&purchaseId={Uri.EscapeDataString(task.PurchaseId)}&seats=2";
            log($"[{task.Id}] Step 3: GET {upgradeUrl}");

            int step3Retries = 0;
            string body3 = string.Empty;
            Match priceMatch = Match.Empty;
            int lastStep3Code = 200;

            while (step3Retries < 5)
            {
                using var req3 = new HttpRequestMessage(HttpMethod.Get, upgradeUrl);
                req3.Headers.Host = "www.magnific.com";
                req3.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                req3.Headers.TryAddWithoutValidation("Accept", "*/*");
                req3.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                req3.Headers.TryAddWithoutValidation("Referer", "https://www.magnific.com/user/my-subscriptions?selected-plan=MAGNIFIC&selected-frequency=yearly");
                req3.Headers.TryAddWithoutValidation("sec-fetch-site", "same-origin");
                req3.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
                req3.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
                req3.Headers.TryAddWithoutValidation("Cookie", cookieHeader);

                using var res3 = await client.SendAsync(req3, cancellationToken);
                body3 = await res3.Content.ReadAsStringAsync(cancellationToken);
                lastStep3Code = (int)res3.StatusCode;

                if (lastStep3Code >= 500 && lastStep3Code <= 599)
                {
                    step3Retries++;
                    if (step3Retries < 5)
                    {
                        log($"[{task.Id}] [RETRY] Step 3 received HTTP {lastStep3Code}. Retrying ({step3Retries}/5) in 1.5s...");
                        await Task.Delay(1500, cancellationToken);
                        continue;
                    }
                }

                priceMatch = PriceIdRegex.Match(body3);
                break;
            }

            if (!priceMatch.Success)
            {
                task.Status = "Error: priceId missing";
                task.StatusType = TaskStatusType.Error;
                task.Message = $"priceId not found in response. HTTP {lastStep3Code}: {TrimSnippet(body3)}";
                log($"[{task.Id}] ❌ Error: priceId missing. HTTP {lastStep3Code}. Snippet: {TrimSnippet(body3)}");
                return;
            }

            task.PriceId = priceMatch.Groups[1].Value;
            log($"[{task.Id}] Got priceId: {task.PriceId}");

            // -------------------------------------------------------------
            // STEP 4: PUT https://www.magnific.com/user/api/my-subscriptions/upgrade-product/purchase
            // -------------------------------------------------------------
            task.Status = "Step 4/4: Attempting Upgrade...";
            log($"[{task.Id}] Step 4: PUT https://www.magnific.com/user/api/my-subscriptions/upgrade-product/purchase");

            string purchasePayload = $"{{\"purchaseId\":\"{task.PurchaseId}\",\"priceId\":\"{task.PriceId}\",\"priceSeats\":1,\"isTeams\":false,\"canUpgradeOrganization\":false,\"isUpgradeToTeam\":false,\"metaData\":{{\"origin_cta\":\"pricing_upgrade\"}}}}";

            const int maxStep4Retries = 5;
            int step4Retries = 0;

            while (step4Retries < maxStep4Retries)
            {
                using var req4 = new HttpRequestMessage(HttpMethod.Put, "https://www.magnific.com/user/api/my-subscriptions/upgrade-product/purchase");
                req4.Headers.Host = "www.magnific.com";
                req4.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                req4.Headers.TryAddWithoutValidation("Accept", "*/*");
                req4.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
                req4.Headers.TryAddWithoutValidation("Origin", "https://www.magnific.com");
                req4.Headers.TryAddWithoutValidation("Referer", "https://www.magnific.com/user/my-subscriptions");
                req4.Headers.TryAddWithoutValidation("sec-fetch-site", "same-origin");
                req4.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
                req4.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
                req4.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
                req4.Content = new StringContent(purchasePayload, Encoding.UTF8, "application/json");

                using var res4 = await client.SendAsync(req4, cancellationToken);
                string body4 = await res4.Content.ReadAsStringAsync(cancellationToken);
                int statusCode4 = (int)res4.StatusCode;

                // Handle transient HTTP 500 / 5xx server errors with up to 5 retries
                if (statusCode4 >= 500 && statusCode4 <= 599)
                {
                    step4Retries++;
                    if (step4Retries < maxStep4Retries)
                    {
                        log($"[{task.Id}] [RETRY] Step 4 received HTTP {statusCode4}. Retrying ({step4Retries}/{maxStep4Retries}) in 1.5s...");
                        await Task.Delay(1500, cancellationToken);
                        continue;
                    }
                    else
                    {
                        task.Status = $"Error: HTTP {statusCode4}";
                        task.StatusType = TaskStatusType.Error;
                        task.Message = $"HTTP {statusCode4} after {maxStep4Retries} retries: {TrimSnippet(body4)}";
                        log($"[{task.Id}] ❌ Exceeded {maxStep4Retries} retries (HTTP {statusCode4}): {TrimSnippet(body4)}");
                        break;
                    }
                }

                // KEYCHECK EVALUATION:
                // IF "<SOURCE>" Contains "PAYMENT_DECLINED" -> Payment Declined
                // ELSE -> Payment Approved (HTTP 204 No Content or 200 OK)
                if (body4.Contains("PAYMENT_DECLINED", StringComparison.OrdinalIgnoreCase))
                {
                    task.Status = "Payment Declined";
                    task.StatusType = TaskStatusType.Declined;
                    task.Message = "PAYMENT_DECLINED";
                    log($"[{task.Id}] ⚠️ Status: Payment Declined for {task.Email ?? task.UserId}");
                    break;
                }
                else if (res4.IsSuccessStatusCode || statusCode4 == 204 || statusCode4 == 200)
                {
                    task.Status = "Payment Approved";
                    task.StatusType = TaskStatusType.Approved;
                    task.Message = $"Purchase successful (HTTP {statusCode4})";
                    log($"[{task.Id}] ✅ Status: Payment Approved for {task.Email ?? task.UserId}!");
                    break;
                }
                else
                {
                    task.Status = $"Error: HTTP {statusCode4}";
                    task.StatusType = TaskStatusType.Error;
                    task.Message = TrimSnippet(body4);
                    log($"[{task.Id}] ❌ Unexpected response HTTP {statusCode4}: {TrimSnippet(body4)}");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            task.Status = "Cancelled";
            task.StatusType = TaskStatusType.Pending;
            task.Message = "Task was cancelled by user.";
            log($"[{task.Id}] ⏹️ Task cancelled.");
        }
        catch (Exception ex)
        {
            task.Status = "Connection Error";
            task.StatusType = TaskStatusType.Error;
            task.Message = ex.Message;
            log($"[{task.Id}] ❌ Exception: {ex.Message}");
        }
        finally
        {
            task.Timestamp = DateTime.Now.ToString("HH:mm:ss");
        }
    }

    private static string TrimSnippet(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        string clean = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return clean.Length > 120 ? clean[..120] + "..." : clean;
    }
}

