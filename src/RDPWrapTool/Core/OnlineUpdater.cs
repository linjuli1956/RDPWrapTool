using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace RDPWrapTool.Core;

/// <summary>
/// Downloads updated rdpwrap.ini files from online sources.
/// </summary>
public class OnlineUpdater
{
    // Default update URLs - user can customize
    public static readonly string[] DefaultUrls = new[]
    {
        "https://raw.githubusercontent.com/stascorp/rdpwrap/master/res/rdpwrap.ini",
        "https://raw.githubusercontent.com/anhkgg/SuperRDP/main/bin/rdpwrap.ini",
        "https://raw.githubusercontent.com/asmtron/rdpwrap/master/res/rdpwrap.ini"
    };

    private readonly string _iniPath;
    private readonly HttpClient _client;

    public event Action<string>? OnLog;
    private void Log(string msg) => OnLog?.Invoke(msg);

    public OnlineUpdater(string iniPath)
    {
        _iniPath = iniPath;
        _client = new HttpClient();
        _client.Timeout = TimeSpan.FromSeconds(30);
        // Set TLS support
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;
    }

    /// <summary>
    /// Download INI from a specific URL and save to the tool directory.
    /// </summary>
    public async Task<bool> DownloadAsync(string url)
    {
        try
        {
            Log($"[*] Downloading INI from: {url}");

            // Add User-Agent header
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("RDPWrapTool/1.0");

            var response = await _client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();

            // Basic validation - check if it looks like an INI file
            if (!content.Contains("[Main]") || !content.Contains("[PatchCodes]"))
            {
                Log("[-] Downloaded content does not appear to be a valid rdpwrap.ini file.");
                return false;
            }

            // Backup current INI
            string backupPath = _iniPath + ".bak";
            if (File.Exists(_iniPath))
                File.Copy(_iniPath, backupPath, true);

            // Save new INI
            await File.WriteAllTextAsync(_iniPath, content);

            Log($"[+] INI updated successfully ({content.Length} bytes).");
            Log($"[*] Backup saved to: {backupPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"[-] Download error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Try downloading from multiple default URLs.
    /// </summary>
    public async Task<bool> DownloadFromDefaultUrlsAsync()
    {
        foreach (var url in DefaultUrls)
        {
            Log($"[*] Trying: {url}");
            if (await DownloadAsync(url))
                return true;
            Log($"[-] Failed, trying next source...");
        }

        Log("[-] All download sources failed.");
        return false;
    }

    /// <summary>
    /// Download from a custom user-provided URL.
    /// </summary>
    public async Task<bool> DownloadFromCustomUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Log("[-] URL cannot be empty.");
            return false;
        }

        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
        {
            Log("[-] URL must start with http:// or https://");
            return false;
        }

        return await DownloadAsync(url);
    }
}
