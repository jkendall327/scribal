namespace Scribal;

public record ModelSlot
{
    public string Provider { get; init; } = default!; // "OpenAI", "Gemini", …
    public string ModelId { get; init; } = default!;
    public string? ApiKey { get; init; } // optional if the provider can share
}

public class AiSettings
{
    public ModelSlot? Primary { get; init; } = new();
    public ModelSlot? Weak { get; init; }
    public ModelSlot? Embeddings { get; init; }
}