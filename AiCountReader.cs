using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Poe2PriceChecker;

internal sealed class AiCountReader
{
    public const string DefaultModel = "gpt-5.4-mini";

    private const string ResponsesEndpoint = "https://api.openai.com/v1/responses";
    private const decimal Gpt54MiniInputPerMillion = 0.75m;
    private const decimal Gpt54MiniOutputPerMillion = 4.50m;
    private const int TileWidth = 190;
    private const int TileHeight = 222;
    private const int CropSize = 164;
    private const int TilePadding = 12;
    private const int MaxColumns = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _debugDirectory;
    private readonly OpenAiApiKeyStore _apiKeyStore;

    public AiCountReader(string debugDirectory, OpenAiApiKeyStore apiKeyStore)
    {
        _debugDirectory = debugDirectory;
        _apiKeyStore = apiKeyStore;
        Directory.CreateDirectory(_debugDirectory);
    }

    public async Task<AiCountReadResult> ReadCountsAsync(
        string profileKey,
        string profileLabel,
        string stashCropPath,
        IReadOnlyList<AiCountSlotSource> slots,
        CancellationToken cancellationToken)
    {
        if (!_apiKeyStore.TryGetOpenAiApiKey(out var apiKey))
        {
            throw new MissingOpenAiApiKeyException("No OpenAI API key configured. Open Settings and paste your API key in the OpenAI API key section.");
        }

        var model = Environment.GetEnvironmentVariable("OPENAI_COUNT_MODEL");
        if (string.IsNullOrWhiteSpace(model))
        {
            model = DefaultModel;
        }

        if (!File.Exists(stashCropPath))
        {
            throw new FileNotFoundException("Current stash crop image was not found.", stashCropPath);
        }

        var occupiedSlots = slots
            .Where(slot => slot.Occupied)
            .ToArray();
        if (occupiedSlots.Length == 0)
        {
            throw new InvalidOperationException("No occupied slots are available for AI count reading.");
        }

        Directory.CreateDirectory(_debugDirectory);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var cleanupErrors = CleanAiCountDebugFolder(_debugDirectory);
        if (cleanupErrors.Count > 0)
        {
            TryWriteCleanupErrors(stamp, cleanupErrors);
        }

        using var stashCrop = CurrencyScanner.LoadBitmapWithoutFileLock(stashCropPath);
        var includedSlots = occupiedSlots
            .Where(slot => IntersectsImage(slot.CropBounds, stashCrop.Size))
            .ToArray();
        var excludedSlots = occupiedSlots
            .Where(slot => !IntersectsImage(slot.CropBounds, stashCrop.Size))
            .Select(slot => new AiCountExcludedSlot(
                profileKey,
                profileLabel,
                slot.SlotIndex,
                slot.ResultIndex,
                slot.CropBounds,
                "slot crop bounds are outside the current stash crop image",
                slot.CurrentQuantity,
                slot.IsCountOverridden))
            .ToArray();
        if (includedSlots.Length == 0)
        {
            throw new InvalidOperationException("Occupied slots were found, but none could be included in the AI count contact sheet.");
        }

        using var contactSheet = BuildContactSheet(stashCrop, includedSlots, excludedSlots, profileKey, profileLabel, out var tileMap);

        var imagePath = Path.Combine(_debugDirectory, $"ai-count-contact-sheet-{profileKey}-{stamp}.png");
        var mapPath = Path.Combine(_debugDirectory, $"ai-count-map-{profileKey}-{stamp}.json");
        var requestPath = Path.Combine(_debugDirectory, $"ai-count-request-{profileKey}-{stamp}.json");
        var rawResponsePath = Path.Combine(_debugDirectory, $"ai-count-raw-response-{profileKey}-{stamp}.json");
        var outputPath = Path.Combine(_debugDirectory, $"ai-count-output-{profileKey}-{stamp}.json");
        var parsedPath = Path.Combine(_debugDirectory, $"ai-count-parsed-{profileKey}-{stamp}.json");

        CurrencyScanner.SaveBitmap(contactSheet, imagePath);
        await File.WriteAllTextAsync(mapPath, JsonSerializer.Serialize(tileMap, JsonOptions), cancellationToken).ConfigureAwait(false);

        var requestInfo = new
        {
            model,
            profileKey,
            profileLabel,
            contactSheetPath = imagePath,
            tileMapPath = mapPath,
            tileCount = tileMap.Tiles.Count,
            excludedOccupiedSlots = tileMap.ExcludedOccupiedSlots,
            prompt = BuildPrompt()
        };
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(requestInfo, JsonOptions), cancellationToken).ConfigureAwait(false);

        var request = BuildRequest(contactSheet, model);
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, ResponsesEndpoint);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var rawResponse = SecretRedactor.Redact(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
            apiKey);
        await File.WriteAllTextAsync(rawResponsePath, rawResponse, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new OpenAiCountReaderException($"OpenAI count read failed: {(int)response.StatusCode} {response.ReasonPhrase}\r\n{rawResponse}", rawResponsePath);
        }

        var outputJson = ExtractOutputText(rawResponse);
        await File.WriteAllTextAsync(outputPath, outputJson, cancellationToken).ConfigureAwait(false);

        var usage = ExtractUsage(rawResponse, model);
        AiCountResponse? parsed = null;
        string? parseError = null;
        try
        {
            parsed = JsonSerializer.Deserialize<AiCountResponse>(outputJson, ReadOptions);
        }
        catch (JsonException ex)
        {
            parseError = ex.Message;
        }

        if (parsed is not null)
        {
            await File.WriteAllTextAsync(parsedPath, JsonSerializer.Serialize(parsed, JsonOptions), cancellationToken).ConfigureAwait(false);
        }

        return new AiCountReadResult(
            model,
            imagePath,
            mapPath,
            requestPath,
            rawResponsePath,
            outputPath,
            parsed is null ? null : parsedPath,
            outputJson,
            parsed?.Results ?? [],
            parseError,
            usage,
            tileMap);
    }

    private static Bitmap BuildContactSheet(
        Bitmap stashCrop,
        IReadOnlyList<AiCountSlotSource> slots,
        IReadOnlyList<AiCountExcludedSlot> excluded,
        string profileKey,
        string profileLabel,
        out AiCountTileMap tileMap)
    {
        var columns = Math.Min(MaxColumns, Math.Max(1, (int)Math.Ceiling(Math.Sqrt(slots.Count))));
        var rows = (int)Math.Ceiling(slots.Count / (double)columns);
        var width = columns * TileWidth + TilePadding;
        var height = rows * TileHeight + TilePadding;
        var output = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var tiles = new List<AiCountTileMapEntry>();

        using var graphics = Graphics.FromImage(output);
        graphics.Clear(Color.FromArgb(30, 30, 30));
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using var tileBrush = new SolidBrush(Color.FromArgb(245, 245, 245));
        using var labelBrush = new SolidBrush(Color.White);
        using var labelFont = new Font("Segoe UI", 24f, FontStyle.Bold);
        using var borderPen = new Pen(Color.FromArgb(80, 80, 80), 2);
        using var labelFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        for (var index = 0; index < slots.Count; index++)
        {
            var slot = slots[index];
            var tileId = $"T{index + 1:000}";
            var column = index % columns;
            var row = index / columns;
            var tileX = TilePadding + column * TileWidth;
            var tileY = TilePadding + row * TileHeight;
            var tileRect = new Rectangle(tileX, tileY, TileWidth - TilePadding, TileHeight - TilePadding);
            var cropArea = new Rectangle(tileX + 12, tileY + 12, CropSize, CropSize);
            var labelArea = new Rectangle(tileX + 8, cropArea.Bottom + 8, TileWidth - TilePadding - 16, 36);

            graphics.FillRectangle(tileBrush, tileRect);
            graphics.DrawRectangle(borderPen, tileRect);

            var safeCropBounds = ClampRectangle(slot.CropBounds, stashCrop.Size);
            using var slotCrop = stashCrop.Clone(safeCropBounds, stashCrop.PixelFormat);
            graphics.DrawImage(slotCrop, cropArea);

            graphics.FillRectangle(Brushes.Black, labelArea);
            graphics.DrawString(tileId, labelFont, labelBrush, labelArea, labelFormat);

            tiles.Add(new AiCountTileMapEntry(
                tileId,
                profileKey,
                profileLabel,
                slot.SlotIndex,
                slot.ResultIndex,
                slot.CropBounds,
                slot.CurrentQuantity,
                slot.CountMethod,
                slot.ItemName,
                slot.IsCountOverridden,
                slot.IsCountOverridden,
                slot.IsCountOverridden ? "manual-count-override" : null));
        }

        tileMap = new AiCountTileMap(
            DateTimeOffset.UtcNow,
            profileKey,
            profileLabel,
            tiles,
            excluded,
            "Tile IDs are sequential contact-sheet labels. They are mapped back to real stash slots by this sidecar.");
        return output;
    }

    private static bool IntersectsImage(Rectangle rectangle, Size imageSize)
    {
        return Rectangle.Intersect(new Rectangle(Point.Empty, imageSize), rectangle) is { Width: > 0, Height: > 0 };
    }

    private static Rectangle ClampRectangle(Rectangle rectangle, Size imageSize)
    {
        var x = Math.Clamp(rectangle.X, 0, Math.Max(0, imageSize.Width - 1));
        var y = Math.Clamp(rectangle.Y, 0, Math.Max(0, imageSize.Height - 1));
        var width = Math.Min(rectangle.Width, imageSize.Width - x);
        var height = Math.Min(rectangle.Height, imageSize.Height - y);
        return new Rectangle(x, y, Math.Max(1, width), Math.Max(1, height));
    }

    private static object BuildRequest(Bitmap contactSheet, string model)
    {
        return new
        {
            model,
            input = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = BuildPrompt()
                        },
                        new
                        {
                            type = "input_image",
                            image_url = ToPngDataUrl(contactSheet)
                        }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "poe2_count_reads",
                    strict = true,
                    schema = BuildResponseSchema()
                }
            },
            max_output_tokens = 6000,
            store = false
        };
    }

    private static string BuildPrompt()
    {
        return string.Join("\n", [
            "You are reading indexed item-slot crops from a Path of Exile 2 stash contact sheet.",
            string.Empty,
            "Task:",
            "For each tile, read only the actual top-left stack count shown inside the item slot.",
            string.Empty,
            "Rules:",
            "* Read only the real top-left stash count inside the item slot.",
            "* Ignore bottom labels, prices, app overlay values, item names, or other UI text.",
            "* Tile labels are IDs only, not counts.",
            "* If no top-left count is visible, return status \"no_count_visible\".",
            "* If a count appears present but is unreadable or ambiguous, return status \"unclear\".",
            "* Do not infer counts from item type or surrounding tiles.",
            "* Do not guess.",
            "* Return strict JSON only. No markdown. No explanation.",
            string.Empty,
            "JSON format:",
            "{",
            "\"results\": [",
            "{ \"tileId\": \"T001\", \"count\": 82, \"status\": \"ok\", \"reason\": null },",
            "{ \"tileId\": \"T002\", \"count\": null, \"status\": \"no_count_visible\", \"reason\": null },",
            "{ \"tileId\": \"T003\", \"count\": null, \"status\": \"unclear\", \"reason\": \"digit clipped\" }",
            "]",
            "}"
        ]);
    }

    private static JsonObject BuildResponseSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("results"),
            ["properties"] = new JsonObject
            {
                ["results"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("tileId", "count", "status", "reason"),
                        ["properties"] = new JsonObject
                        {
                            ["tileId"] = new JsonObject { ["type"] = "string" },
                            ["count"] = new JsonObject { ["type"] = new JsonArray("integer", "null") },
                            ["status"] = new JsonObject
                            {
                                ["type"] = "string",
                                ["enum"] = new JsonArray("ok", "no_count_visible", "unclear")
                            },
                            ["reason"] = new JsonObject { ["type"] = new JsonArray("string", "null") }
                        }
                    }
                }
            }
        };
    }

    private static string ToPngDataUrl(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private static IReadOnlyList<string> CleanAiCountDebugFolder(string debugDirectory)
    {
        var errors = new List<string>();
        var patterns = new[]
        {
            "ai-count-contact-sheet-*.png",
            "ai-count-output-*.json",
            "ai-count-parsed-*.json",
            "ai-count-raw-response-*.json",
            "ai-count-map-*.json",
            "ai-count-request-*.json",
            "ai-count-error-*.txt",
            "ai-count-*-error-*.txt",
            "ai-count-recalculation-error-*.txt",
            "ai-count-cleanup-error-*.txt"
        };

        foreach (var path in patterns
            .SelectMany(pattern => Directory.EnumerateFiles(debugDirectory, pattern, SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex)
            {
                errors.Add($"{path}: {ex.Message}");
            }
        }

        return errors;
    }

    private void TryWriteCleanupErrors(string stamp, IReadOnlyList<string> cleanupErrors)
    {
        try
        {
            File.WriteAllLines(
                Path.Combine(_debugDirectory, $"ai-count-cleanup-error-{stamp}.txt"),
                cleanupErrors);
        }
        catch
        {
            // Cleanup is best-effort. Never block the AI count read because a debug note could not be written.
        }
    }

    private static string ExtractOutputText(string rawResponse)
    {
        using var document = JsonDocument.Parse(rawResponse);
        var root = document.RootElement;
        if (root.TryGetProperty("output_text", out var outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return rawResponse;
        }

        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var text) &&
                    text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }
        }

        return builder.Length == 0 ? rawResponse : builder.ToString();
    }

    private static AiCountUsage? ExtractUsage(string rawResponse, string model)
    {
        try
        {
            using var document = JsonDocument.Parse(rawResponse);
            if (!document.RootElement.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var inputTokens = GetInt(usage, "input_tokens") ?? GetInt(usage, "prompt_tokens");
            var outputTokens = GetInt(usage, "output_tokens") ?? GetInt(usage, "completion_tokens");
            var totalTokens = GetInt(usage, "total_tokens") ?? ((inputTokens ?? 0) + (outputTokens ?? 0));
            decimal? estimatedCost = null;
            if (model.Equals(DefaultModel, StringComparison.OrdinalIgnoreCase) &&
                inputTokens is not null &&
                outputTokens is not null)
            {
                estimatedCost =
                    inputTokens.Value / 1_000_000m * Gpt54MiniInputPerMillion +
                    outputTokens.Value / 1_000_000m * Gpt54MiniOutputPerMillion;
            }

            return new AiCountUsage(inputTokens, outputTokens, totalTokens, estimatedCost);
        }
        catch
        {
            return null;
        }
    }

    private static int? GetInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out var value)
            ? value
            : null;
    }
}

internal sealed class MissingOpenAiApiKeyException(string message) : Exception(message);

internal sealed class OpenAiCountReaderException(string message, string debugPath) : Exception(message)
{
    public string DebugPath { get; } = debugPath;
}

internal sealed record AiCountSlotSource(
    int ResultIndex,
    int SlotIndex,
    Rectangle CropBounds,
    bool Occupied,
    int? CurrentQuantity,
    bool IsCountOverridden,
    string CountMethod,
    string? ItemName);

internal sealed record AiCountReadResult(
    string Model,
    string ContactSheetPath,
    string TileMapPath,
    string RequestInfoPath,
    string RawResponsePath,
    string OutputJsonPath,
    string? ParsedJsonPath,
    string OutputJson,
    IReadOnlyList<AiCountTileResult> Results,
    string? ParseError,
    AiCountUsage? Usage,
    AiCountTileMap TileMap);

internal sealed record AiCountUsage(
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    decimal? EstimatedCostUsd);

internal sealed class AiCountResponse
{
    [JsonPropertyName("results")]
    public List<AiCountTileResult> Results { get; set; } = [];
}

internal sealed class AiCountTileResult
{
    [JsonPropertyName("tileId")]
    public string TileId { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int? Count { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

internal sealed record AiCountTileMap(
    DateTimeOffset CreatedUtc,
    string ProfileKey,
    string ProfileLabel,
    IReadOnlyList<AiCountTileMapEntry> Tiles,
    IReadOnlyList<AiCountExcludedSlot> ExcludedOccupiedSlots,
    string Note);

internal sealed record AiCountTileMapEntry(
    string TileId,
    string ProfileKey,
    string ProfileLabel,
    int SlotIndex,
    int ResultIndex,
    Rectangle CropBounds,
    int? ExistingCount,
    string CountMethod,
    string? ItemName,
    bool ManualOverride,
    bool Locked,
    string? LockReason);

internal sealed record AiCountExcludedSlot(
    string ProfileKey,
    string ProfileLabel,
    int SlotIndex,
    int ResultIndex,
    Rectangle CropBounds,
    string Reason,
    int? ExistingCount,
    bool ManualOverride);
