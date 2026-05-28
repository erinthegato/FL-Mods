using System.Net;
using System.Text.RegularExpressions;

namespace BackgroundRadio;

public sealed class BroadcastifyService
{
    public sealed record Feed(int Id, string Name, string Location, int Listeners);

    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    });

    private List<Feed>? _cachedFeeds;
    private static readonly List<Feed> _emptyFeeds = new();
    private DateTime _cacheTime;
    private const float CacheDuration = 120f;
    public string LastError { get; private set; } = "";

    private static readonly Regex _rowRx = new(@"<tr>(.*?)</tr>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex _linkRx = new(@"href=""/listen/feed/(\d+)"">([^<]+)", RegexOptions.Compiled);
    private static readonly Regex _countRx = new(@"data-sort-value=""(\d+)""", RegexOptions.Compiled);
    private static readonly Regex _locRx = new(@"class=""d-none d-md-table-cell[^""]*"">\s*<a[^>]*>([^<]+)", RegexOptions.Compiled);
    private static readonly Regex _relayUrlRx = new(@"relayUrl:\s*""([^""]+)""", RegexOptions.Compiled);

    public BroadcastifyService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        _http.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<List<Feed>> GetTopFeedsAsync()
    {
        if (_cachedFeeds != null && (DateTime.UtcNow - _cacheTime).TotalSeconds < CacheDuration)
            return _cachedFeeds;

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://www.broadcastify.com/listen/top/");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.5");

            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            string html = await resp.Content.ReadAsStringAsync();

            var feeds = new List<Feed>();
            var rows = _rowRx.Matches(html);

            foreach (Match row in rows)
            {
                string rowHtml = row.Groups[1].Value;
                var linkMatch = _linkRx.Match(rowHtml);
                if (!linkMatch.Success) continue;

                int id = int.Parse(linkMatch.Groups[1].Value);
                string name = WebUtility.HtmlDecode(linkMatch.Groups[2].Value).Trim();
                int listeners = 0;

                var countMatch = _countRx.Match(rowHtml);
                if (countMatch.Success)
                    int.TryParse(countMatch.Groups[1].Value, out listeners);

                var locMatch = _locRx.Match(rowHtml);
                string location = locMatch.Success ? WebUtility.HtmlDecode(locMatch.Groups[1].Value) : "";

                if (id > 0 && !string.IsNullOrEmpty(name))
                    feeds.Add(new Feed(id, name, location, listeners));
            }

            _cachedFeeds = feeds;
            _cacheTime = DateTime.UtcNow;
            return feeds;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return _cachedFeeds ?? _emptyFeeds;
        }
    }

    public async Task<string?> GetStreamUrlAsync(int feedId)
    {
        var fallback = $"https://broadcastify.cdnstream1.com/{feedId}";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.broadcastify.com/listen/feed/{feedId}/");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

            using var resp = await _http.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            string html = await resp.Content.ReadAsStringAsync();

            var relayMatch = _relayUrlRx.Match(html);
            if (relayMatch.Success)
                return relayMatch.Groups[1].Value.Replace("\\/", "/");

            LastError = relayMatch.Success ? "" : "Relay URL not found; using direct Broadcastify CDN URL.";
            return fallback;
        }
        catch (Exception ex)
        {
            LastError = $"Stream page failed; using direct Broadcastify CDN URL. {ex.Message}";
            return fallback;
        }
    }

    public List<Feed> GetCachedFeeds()
    {
        return _cachedFeeds ?? new List<Feed>();
    }

    public void InvalidateCache()
    {
        _cachedFeeds = null;
        _cacheTime = DateTime.MinValue;
    }
}
