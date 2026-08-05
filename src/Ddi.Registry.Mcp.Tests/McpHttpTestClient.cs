using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ddi.Registry.Mcp.Tests
{
    /// <summary>
    /// Minimal MCP Streamable HTTP JSON-RPC client over a raw HttpClient. Not a production
    /// client: it exists so the integration tests exercise the real /mcp endpoint through the
    /// WebApplicationFactory (preserving the X-Test-Principal header) without depending on an
    /// SDK transport. Every POST sends Accept: application/json, text/event-stream and the
    /// MCP-Protocol-Version header; the SSE (or JSON) response is unwrapped to a JsonElement.
    /// </summary>
    public sealed class McpHttpTestClient
    {
        /// <summary>An initialize-handshake protocol version supported by the 2.0.0 SDK.</summary>
        public const string ProtocolVersion = "2025-06-18";

        private const string JsonRpcVersion = "2.0";
        private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _http;
        private readonly string _path;
        private int _nextId = 1;

        public McpHttpTestClient(HttpClient http, string path)
        {
            _http = http;
            _path = path;
        }

        /// <summary>Creates a client for the factory, adds the test principal header and completes the initialize handshake.</summary>
        public static async Task<McpHttpTestClient> ConnectAsync(McpWebApplicationFactory factory, string? principal = "full")
        {
            var httpClient = factory.CreateClient();
            if (principal != null)
                httpClient.DefaultRequestHeaders.Add(TestAuthHandler.HeaderName, principal);
            var client = new McpHttpTestClient(httpClient, "/mcp");
            await client.InitializeAsync();
            return client;
        }

        public static async Task<McpHttpTestClient> ConnectWithBearerTokenAsync(McpWebApplicationFactory factory, string accessToken)
        {
            var httpClient = factory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var client = new McpHttpTestClient(httpClient, "/mcp");
            await client.InitializeAsync();
            return client;
        }

        public async Task InitializeAsync()
        {
            await SendAsync("initialize", InitializeParams(), _nextId++);
        }

        public async Task<IReadOnlyList<McpToolInfo>> ListToolsAsync()
        {
            var response = await SendAsync("tools/list", new Dictionary<string, object?>(), _nextId++);
            var result = response.GetProperty("result");
            var tools = new List<McpToolInfo>();
            foreach (var tool in result.GetProperty("tools").EnumerateArray())
                tools.Add(new McpToolInfo { Name = tool.GetProperty("name").GetString() ?? string.Empty });
            return tools;
        }

        public async Task<McpToolCallResult> CallToolAsync(string name, IDictionary<string, object> arguments)
        {
            var parameters = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["arguments"] = arguments
            };
            var response = await SendAsync("tools/call", parameters, _nextId++);
            if (response.TryGetProperty("error", out var error))
            {
                var message = error.TryGetProperty("message", out var m) ? m.GetString() : "JSON-RPC error";
                return new McpToolCallResult { Content = message ?? "JSON-RPC error", IsError = true };
            }
            var result = response.GetProperty("result");
            var content = new StringBuilder();
            if (result.TryGetProperty("content", out var contentBlocks))
            {
                foreach (var block in contentBlocks.EnumerateArray())
                    if (block.TryGetProperty("text", out var text))
                        content.Append(text.GetString());
            }
            var isError = result.TryGetProperty("isError", out var isErr) && isErr.ValueKind == JsonValueKind.True;
            return new McpToolCallResult { Content = content.ToString(), IsError = isError };
        }

        /// <summary>Sends the raw initialize POST, returning the HttpResponseMessage (used for the 401 test).</summary>
        public static async Task<HttpResponseMessage> SendInitializeAsync(HttpClient http, string path)
        {
            using var request = BuildRequest(path, "initialize", InitializeParams(), 1);
            return await http.SendAsync(request);
        }

        private async Task<JsonElement> SendAsync(string method, object? parameters, int id)
        {
            using var request = BuildRequest(_path, method, parameters, id);
            using var response = await _http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await ParseResponseAsync(response);
        }

        private static Dictionary<string, object?> InitializeParams() => new()
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new Dictionary<string, object?>(),
            ["clientInfo"] = new Dictionary<string, object?> { ["name"] = "mcp-test-client", ["version"] = "1.0.0" }
        };

        private static HttpRequestMessage BuildRequest(string path, string method, object? parameters, int id)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
            var body = new Dictionary<string, object?>
            {
                ["jsonrpc"] = JsonRpcVersion,
                ["method"] = method,
                ["id"] = id
            };
            if (parameters != null) body["params"] = parameters;
            request.Content = new StringContent(JsonSerializer.Serialize(body, s_jsonOptions), Encoding.UTF8, "application/json");
            return request;
        }

        private static async Task<JsonElement> ParseResponseAsync(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException($"MCP endpoint returned {(int)response.StatusCode} with an empty body.");

            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (mediaType.Contains("text/event-stream"))
            {
                foreach (var payload in SseDataPayloads(body))
                {
                    using var doc = JsonDocument.Parse(payload);
                    return doc.RootElement.Clone();
                }
                throw new InvalidOperationException($"SSE response contained no data events:\n{body}");
            }

            using var jsonDoc = JsonDocument.Parse(body);
            return jsonDoc.RootElement.Clone();
        }

        private static IEnumerable<string> SseDataPayloads(string sse)
        {
            var current = new StringBuilder();
            foreach (var rawLine in sse.Replace("\r\n", "\n").Split('\n'))
            {
                if (rawLine.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (current.Length > 0) current.Append('\n');
                    current.Append(rawLine.AsSpan("data:".Length).TrimStart().ToString());
                }
                else if (rawLine.Length == 0)
                {
                    if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                }
                // event:/id:/retry:/comment lines carry no JSON-RPC payload.
            }
            if (current.Length > 0) yield return current.ToString();
        }
    }

    public sealed class McpToolInfo
    {
        public string Name { get; set; } = string.Empty;
    }

    public sealed class McpToolCallResult
    {
        public string Content { get; set; } = string.Empty;
        public bool IsError { get; set; }
    }
}
