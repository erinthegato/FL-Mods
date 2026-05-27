using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace NPCAI;

public sealed class ChatService : IDisposable
{
    private readonly HttpClient _http = new();
    private string _apiKey = "";
    private string _endpoint = "https://api.deepseek.com";
    private string _model = "deepseek-chat";
    private readonly List<(string role, string text)> _history = new();
    private string _systemContext = "";
    private const int MaxHistory = 20;

    private Process? _ttsProcess;
    private StreamWriter? _ttsStdin;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);
    public string LastResponse { get; private set; } = "";
    public float AudioVolume { get; set; } = 0.7f;
    public CancellationToken Cancellation { get; set; }

    public ChatService()
    {
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public void Configure(string apiKey, string endpoint, string model)
    {
        _apiKey = apiKey ?? "";
        _endpoint = !string.IsNullOrWhiteSpace(endpoint) ? endpoint.TrimEnd('/') : "https://api.deepseek.com";
        _model = !string.IsNullOrWhiteSpace(model) ? model : "deepseek-chat";
    }

    public void SetSystemContext(string context) => _systemContext = context ?? "";

    public void ClearHistory()
    {
        _history.Clear();
        _systemContext = "";
    }

    public async Task<string> SendMessageAsync(string message)
    {
        if (!IsConfigured)
            return "API key not configured. Set it in FL-Modkit config and restart.";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_endpoint}/v1/chat/completions")
            {
                Content = new StringContent(BuildRequestBody(message), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {_apiKey}");

            using var response = await _http.SendAsync(request, Cancellation);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                LastResponse = ParseErrorResponse(responseJson);
                return LastResponse;
            }

            var text = ParseResponse(responseJson);

            _history.Add(("user", message));
            _history.Add(("assistant", text));
            if (_history.Count > MaxHistory)
                _history.RemoveRange(0, 2);

            LastResponse = text;
            return text;
        }
        catch (OperationCanceledException)
        {
            LastResponse = "Request cancelled.";
            return LastResponse;
        }
        catch (HttpRequestException ex)
        {
            LastResponse = $"API error ({(ex.StatusCode.HasValue ? ((int)ex.StatusCode.Value).ToString() : "?")})";
            return LastResponse;
        }
        catch (Exception ex)
        {
            LastResponse = $"Error: {ex.GetType().Name}";
            return LastResponse;
        }
    }

    public void SpeakText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (text.StartsWith("API error") || text.StartsWith("Request timed out") ||
            text.StartsWith("API key") || text.StartsWith("Error:") ||
            text.StartsWith("Request cancelled"))
            return;

        try
        {
            if (_ttsProcess is { HasExited: false })
            {
                _ttsStdin?.WriteLine(text);
                _ttsStdin?.Flush();
                return;
            }

            StartTtsProcess(text);
        }
        catch
        {
        }
    }

    private void StartTtsProcess(string initialText)
    {
        var vol = Math.Clamp((int)(AudioVolume * 100), 0, 100);
        var psi = new ProcessStartInfo("powershell")
        {
            Arguments = $"-NoProfile -Command \"$v=New-Object -ComObject SAPI.SpVoice; $v.Volume={vol}; while($t=[Console]::In.ReadLine()){{ $v.Speak($t) }}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true
        };

        _ttsProcess = Process.Start(psi);
        if (_ttsProcess == null) return;

        _ttsStdin = _ttsProcess.StandardInput;
        _ttsStdin.WriteLine(initialText);
        _ttsStdin.Flush();
    }

    private string BuildRequestBody(string message)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        writer.WriteStartObject();
        writer.WriteString("model", _model);
        writer.WriteStartArray("messages");

        if (!string.IsNullOrWhiteSpace(_systemContext))
        {
            writer.WriteStartObject();
            writer.WriteString("role", "system");
            writer.WriteString("content", _systemContext);
            writer.WriteEndObject();
        }

        foreach (var (role, text) in _history)
        {
            writer.WriteStartObject();
            writer.WriteString("role", role);
            writer.WriteString("content", text);
            writer.WriteEndObject();
        }

        writer.WriteStartObject();
        writer.WriteString("role", "user");
        writer.WriteString("content", message);
        writer.WriteEndObject();

        writer.WriteEndArray();

        writer.WriteNumber("temperature", 0.9);

        writer.WriteNumber("max_tokens", 250);
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string ParseErrorResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var msg = "unknown";
                if (error.TryGetProperty("message", out var m)) msg = m.GetString() ?? msg;
                return $"API error: {msg}";
            }
            return "API error";
        }
        catch
        {
            return "API error";
        }
    }

    private static string ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return "(empty response)";

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var msg))
                return "(empty response)";

            if (!msg.TryGetProperty("content", out var content) || content.ValueKind == JsonValueKind.Null)
                return "(empty response)";

            return content.GetString()?.Trim() ?? "(empty response)";
        }
        catch
        {
            return "(parse error)";
        }
    }

    public void Dispose()
    {
        try
        {
            _ttsStdin?.Close();
            _ttsProcess?.Kill();
            _ttsProcess?.Dispose();
        }
        catch { }

        _http.Dispose();
    }
}
