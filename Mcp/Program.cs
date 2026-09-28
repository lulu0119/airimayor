using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

string baseUrl = "";
string token = "";
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--base-url" && i + 1 < args.Length)
    {
        baseUrl = args[++i];
    }
    else if (args[i] == "--token" && i + 1 < args.Length)
    {
        token = args[++i];
    }
}

if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("airimayor-mcp needs --base-url and --token.");
    return 1;
}

using var http = new HttpClient();
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
http.Timeout = Timeout.InfiniteTimeSpan;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInstructions = "";
    })
    .WithStdioServerTransport()
    .WithListToolsHandler(async (request, cancellationToken) =>
    {
        JsonObject catalog = await GetCatalogAsync(http, baseUrl, cancellationToken);
        var tools = new List<Tool>();
        if (catalog["tools"] is JsonArray listed)
        {
            foreach (JsonNode node in listed)
            {
                if (node is not JsonObject tool)
                {
                    continue;
                }
                tools.Add(new Tool
                {
                    Name = tool["name"]?.GetValue<string>() ?? "",
                    Description = tool["description"]?.GetValue<string>() ?? "",
                    InputSchema = ParseSchema(tool["parametersJson"]?.GetValue<string>()),
                });
            }
        }
        return new ListToolsResult { Tools = tools };
    })
    .WithCallToolHandler(async (request, cancellationToken) =>
    {
        string name = request.Params?.Name ?? "";
        string argumentsJson = request.Params?.Arguments == null
            ? "{}"
            : JsonSerializer.Serialize(request.Params.Arguments);
        var body = new JsonObject
        {
            ["name"] = name,
            ["argumentsJson"] = argumentsJson,
        };
        string callId = Guid.NewGuid().ToString("N");
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "invoke"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        message.Headers.TryAddWithoutValidation("X-Call-Id", callId);
        using HttpResponseMessage response = await SendInvokeAsync(http, message, baseUrl, callId, cancellationToken);
        string payload = await response.Content.ReadAsStringAsync(cancellationToken);
        JsonNode parsed = JsonNode.Parse(payload);
        bool success = parsed?["success"]?.GetValue<bool>() ?? false;
        string text = parsed?["text"]?.GetValue<string>() ?? "";
        var blocks = new List<ContentBlock>
        {
            new TextContentBlock { Text = text },
        };
        string image = parsed?["imagePngBase64"]?.GetValue<string>();
        if (!string.IsNullOrEmpty(image))
        {
            blocks.Add(ImageContentBlock.FromBytes(Convert.FromBase64String(image), "image/png"));
        }
        return new CallToolResult
        {
            IsError = !success,
            Content = blocks,
        };
    });

JsonObject first = await GetCatalogAsync(http, baseUrl, CancellationToken.None);
string instructions = first["instructions"]?.GetValue<string>() ?? "";
builder.Services.Configure<McpServerOptions>(options =>
{
    options.ServerInstructions = instructions;
});

await builder.Build().RunAsync();
return 0;

static async Task<HttpResponseMessage> SendInvokeAsync(
    HttpClient http,
    HttpRequestMessage message,
    string baseUrl,
    string callId,
    CancellationToken cancellationToken)
{
    try
    {
        return await http.SendAsync(message, cancellationToken);
    }
    catch (Exception)
    {
        await CancelCallAsync(http, baseUrl, callId);
        throw;
    }
}

static async Task CancelCallAsync(HttpClient http, string baseUrl, string callId)
{
    try
    {
        using HttpResponseMessage response = await http.PostAsync(
            new Uri(new Uri(baseUrl), "cancel?id=" + Uri.EscapeDataString(callId)),
            new StringContent(""));
    }
    catch (Exception e)
    {
        Console.Error.WriteLine("cancel " + callId + " failed: " + e.Message);
    }
}

static async Task<JsonObject> GetCatalogAsync(HttpClient http, string baseUrl, CancellationToken cancellationToken)
{
    using HttpResponseMessage response = await http.GetAsync(new Uri(new Uri(baseUrl), "tools"), cancellationToken);
    response.EnsureSuccessStatusCode();
    string payload = await response.Content.ReadAsStringAsync(cancellationToken);
    return JsonNode.Parse(payload) as JsonObject ?? new JsonObject();
}

static JsonElement ParseSchema(string parametersJson)
{
    if (string.IsNullOrWhiteSpace(parametersJson))
    {
        parametersJson = """{"type":"object","properties":{}}""";
    }
    using JsonDocument document = JsonDocument.Parse(parametersJson);
    return document.RootElement.Clone();
}
