using System.Drawing.Imaging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Poe2PriceChecker;

internal sealed class OpenAiVisionHelper
{
    private const string DefaultModel = "gpt-5.4-mini";
    private const string ResponsesEndpoint = "https://api.openai.com/v1/responses";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _debugDirectory;
    private readonly OpenAiApiKeyStore _apiKeyStore;

    public OpenAiVisionHelper(string debugDirectory, OpenAiApiKeyStore apiKeyStore)
    {
        _debugDirectory = debugDirectory;
        _apiKeyStore = apiKeyStore;
        Directory.CreateDirectory(_debugDirectory);
    }

    public async Task<AiStashAnalysisResult> AnalyzeStashAsync(
        Bitmap stashCrop,
        string modeLabel,
        StashLayoutProfile layout,
        CancellationToken cancellationToken)
    {
        if (!_apiKeyStore.TryGetOpenAiApiKey(out var apiKey))
        {
            throw new MissingOpenAiApiKeyException("No OpenAI API key configured. Open Settings and paste your API key in the OpenAI API key section.");
        }

        var model = Environment.GetEnvironmentVariable("OPENAI_STASH_MODEL");
        if (string.IsNullOrWhiteSpace(model))
        {
            model = DefaultModel;
        }

        Directory.CreateDirectory(_debugDirectory);
        var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var imagePath = Path.Combine(_debugDirectory, $"stash-ai-request-{stamp}.png");
        var latestImagePath = Path.Combine(_debugDirectory, "latest-request-image.png");
        CurrencyScanner.SaveBitmap(stashCrop, imagePath);
        CurrencyScanner.SaveBitmap(stashCrop, latestImagePath);

        var request = BuildRequest(stashCrop, modeLabel, layout, model);
        var requestInfoPath = Path.Combine(_debugDirectory, $"stash-ai-request-{stamp}.json");
        var latestRequestInfoPath = Path.Combine(_debugDirectory, "latest-request.json");
        var requestInfo = new
        {
            model,
            modeLabel,
            imageWidth = stashCrop.Width,
            imageHeight = stashCrop.Height,
            layout.DisplayCropRegion,
            layout.SlotOffset,
            note = "Base64 image data is omitted from this debug file."
        };
        await File.WriteAllTextAsync(requestInfoPath, JsonSerializer.Serialize(requestInfo, JsonOptions), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(latestRequestInfoPath, JsonSerializer.Serialize(requestInfo, JsonOptions), cancellationToken).ConfigureAwait(false);

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(90)
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
        var responsePath = Path.Combine(_debugDirectory, $"stash-ai-response-{stamp}.json");
        var latestResponsePath = Path.Combine(_debugDirectory, "latest-response.json");
        await File.WriteAllTextAsync(responsePath, rawResponse, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(latestResponsePath, rawResponse, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"OpenAI analysis failed: {(int)response.StatusCode} {response.ReasonPhrase}\r\n{rawResponse}");
        }

        var outputJson = ExtractOutputText(rawResponse);
        var outputPath = Path.Combine(_debugDirectory, $"stash-ai-output-{stamp}.json");
        var latestOutputPath = Path.Combine(_debugDirectory, "latest-output.json");
        await File.WriteAllTextAsync(outputPath, outputJson, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(latestOutputPath, outputJson, cancellationToken).ConfigureAwait(false);

        AiStashAnalysis? analysis = null;
        string? parseError = null;
        try
        {
            analysis = JsonSerializer.Deserialize<AiStashAnalysis>(outputJson, ReadOptions);
            if (analysis is not null)
            {
                SanitizeAnalysis(analysis);
            }
        }
        catch (JsonException ex)
        {
            parseError = ex.Message;
        }

        return new AiStashAnalysisResult(
            model,
            imagePath,
            responsePath,
            outputPath,
            analysis,
            outputJson,
            parseError);
    }

    private static object BuildRequest(
        Bitmap stashCrop,
        string modeLabel,
        StashLayoutProfile layout,
        string model)
    {
        var prompt = string.Join("\n", [
            "Analyze this Path of Exile 2 stash-tab crop as a developer layout helper for building a fixed-layout local scanner.",
            "Return JSON only.",
            "Your primary job is layout, not item identification.",
            "Coordinates must be pixel coordinates relative to this sent crop image, not the full monitor.",
            "Prefer visible slot frame bounds over item-art bounds.",
            "Use width/height for the clickable slot rectangle.",
            "Return every visible slot rectangle you can see, including empty slots and utility/non-grid slots.",
            "Mark occupied=false for blank slots.",
            "In notes, summarize row/section groupings and mention if a row, bottom bar, or right/left edge appears clipped or scrollable.",
            "For itemNameGuess, usually use null. Only name an item when the icon is unmistakable and the exact Path of Exile 2 item name is known.",
            "Never use generic labels like 'Unknown currency', 'currency item', 'fragment', or 'Vaal Fragment'. Use null instead.",
            "Important: Path of Exile 2 does not have an item named 'Vaal Fragment'.",
            "For non-currency tabs, prefer null itemNameGuess unless a known item list is provided below.",
            "For countGuess, usually use null. Only provide a count when the stack digits are visually clear.",
            "This is only a mapper/helper pass. Do not explain outside JSON.",
            string.Empty,
            $"Selected app mode: {modeLabel}",
            BuildKnownItemsHint(modeLabel),
            $"Image size: {stashCrop.Width}x{stashCrop.Height}",
            $"Source crop region on screen: {layout.DisplayCropRegion.X},{layout.DisplayCropRegion.Y},{layout.DisplayCropRegion.Width},{layout.DisplayCropRegion.Height}",
            $"Slot offset used by local scanner: {layout.SlotOffset.X},{layout.SlotOffset.Y}"
        ]);

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
                            text = prompt
                        },
                        new
                        {
                            type = "input_image",
                            image_url = ToPngDataUrl(stashCrop)
                        }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "stash_tab_analysis",
                    strict = true,
                    schema = BuildResponseSchema()
                }
            },
            max_output_tokens = 4000,
            store = false
        };
    }

    private static string BuildKnownItemsHint(string modeLabel)
    {
        if (!modeLabel.Contains("Currency", StringComparison.OrdinalIgnoreCase))
        {
            return "Known item list: not provided for this tab. Prefer null over uncertain icon names.";
        }

        return "Known Path of Exile 2 currency names for this tab include: " +
            "Orb of Transmutation, Greater Orb of Transmutation, Perfect Orb of Transmutation, " +
            "Orb of Augmentation, Greater Orb of Augmentation, Perfect Orb of Augmentation, " +
            "Orb of Alchemy, Vaal Orb, Orb of Annulment, Orb of Chance, Divine Orb, " +
            "Regal Orb, Greater Regal Orb, Perfect Regal Orb, Exalted Orb, Greater Exalted Orb, Perfect Exalted Orb, " +
            "Lesser Jeweller's Orb, Greater Jeweller's Orb, Artificer's Orb, Arcanist's Etcher, " +
            "Armourer's Scrap, Blacksmith's Whetstone, Glassblower's Bauble, Gemcutter's Prism, " +
            "Chaos Orb, Greater Chaos Orb, Scroll of Wisdom, Regal Shard, Chance Shard. " +
            "If the icon is not clearly one of these, use null.";
    }

    private static void SanitizeAnalysis(AiStashAnalysis analysis)
    {
        foreach (var slot in analysis.Slots)
        {
            if (IsGenericOrInvalidItemGuess(slot.ItemNameGuess) || slot.Confidence < 0.85)
            {
                slot.ItemNameGuess = null;
            }
        }
    }

    private static bool IsGenericOrInvalidItemGuess(string? itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return false;
        }

        var normalized = itemName.Trim().ToLowerInvariant();
        return normalized is "unknown currency" or "unknown item" or "currency item" or "vaal fragment" ||
            normalized.Contains("fragment", StringComparison.Ordinal) ||
            normalized.Contains("unknown", StringComparison.Ordinal);
    }

    private static JsonObject BuildResponseSchema()
    {
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new JsonArray("stashTypeGuess", "layoutConfidence", "slots", "notes"),
            ["properties"] = new JsonObject
            {
                ["stashTypeGuess"] = new JsonObject
                {
                    ["type"] = "string"
                },
                ["layoutConfidence"] = new JsonObject
                {
                    ["type"] = "number",
                    ["minimum"] = 0,
                    ["maximum"] = 1
                },
                ["slots"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new JsonArray("x", "y", "width", "height", "occupied", "itemNameGuess", "countGuess", "confidence"),
                        ["properties"] = new JsonObject
                        {
                            ["x"] = new JsonObject { ["type"] = "integer" },
                            ["y"] = new JsonObject { ["type"] = "integer" },
                            ["width"] = new JsonObject { ["type"] = "integer" },
                            ["height"] = new JsonObject { ["type"] = "integer" },
                            ["occupied"] = new JsonObject { ["type"] = "boolean" },
                            ["itemNameGuess"] = new JsonObject
                            {
                                ["type"] = new JsonArray("string", "null")
                            },
                            ["countGuess"] = new JsonObject
                            {
                                ["type"] = new JsonArray("integer", "null")
                            },
                            ["confidence"] = new JsonObject
                            {
                                ["type"] = "number",
                                ["minimum"] = 0,
                                ["maximum"] = 1
                            }
                        }
                    }
                },
                ["notes"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "string"
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
}
