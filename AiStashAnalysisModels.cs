using System.Text.Json.Serialization;

namespace Poe2PriceChecker;

internal sealed class AiStashAnalysis
{
    [JsonPropertyName("stashTypeGuess")]
    public string StashTypeGuess { get; set; } = string.Empty;

    [JsonPropertyName("layoutConfidence")]
    public double LayoutConfidence { get; set; }

    [JsonPropertyName("slots")]
    public List<AiStashSlot> Slots { get; set; } = [];

    [JsonPropertyName("notes")]
    public List<string> Notes { get; set; } = [];
}

internal sealed class AiStashSlot
{
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("occupied")]
    public bool Occupied { get; set; }

    [JsonPropertyName("itemNameGuess")]
    public string? ItemNameGuess { get; set; }

    [JsonPropertyName("countGuess")]
    public int? CountGuess { get; set; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }
}

internal sealed record AiStashAnalysisResult(
    string Model,
    string RequestImagePath,
    string RawResponsePath,
    string OutputJsonPath,
    AiStashAnalysis? Analysis,
    string OutputJson,
    string? ParseError);
