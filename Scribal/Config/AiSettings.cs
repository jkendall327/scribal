namespace Scribal.Config;

public record ModelSlot
{
    public string Provider { get; set; } = default!; // "OpenAI", "Gemini", …
    public string ModelId { get; set; } = default!;
    public string? ApiKey { get; set; } // optional if the provider can share
}

public class AiSettings
{
    public ModelSlot? Primary { get; set; }
    public ModelSlot? Weak { get; set; }
    public ModelSlot? Embeddings { get; set; }
}