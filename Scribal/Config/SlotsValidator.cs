namespace Scribal;

public static class SlotsValidator
{
    public static bool ValidateSlots(AiSettings settings, out string? error)
    {
        //------------------------------------------------------------------
        // 1.  Normalise the three possible slots into a list we can loop
        //------------------------------------------------------------------
        var slots = new (string Name, ModelSlot? Slot)[]
        {
            ("Primary", settings.Primary), ("Weak", settings.Weak), ("Embeddings", settings.Embeddings)
        };

        //------------------------------------------------------------------
        // 2.  Per-slot checks  (provider + model id must exist)
        //------------------------------------------------------------------
        foreach ((var name, var slot) in slots)
        {
            if (slot is null)
            {
                continue; // slot not configured – that’s OK
            }

            if (string.IsNullOrWhiteSpace(slot.Provider))
            {
                error = $"{name} slot is missing Provider.";

                return false;
            }

            if (string.IsNullOrWhiteSpace(slot.ModelId))
            {
                error = $"{name} slot is missing ModelId.";

                return false;
            }
        }

        //------------------------------------------------------------------
        // 3.  Per-provider API-key checks
        //------------------------------------------------------------------
        // Build a map   provider -> first non-null apiKey we see
        var providerKeys = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach ((var _, var slot) in slots.Where(s => s.Slot is not null))
        {
            var p = slot!.Provider;
            providerKeys.TryGetValue(p, out var recordedKey);

            providerKeys[p] = recordedKey ?? slot.ApiKey; // keep first non-null
        }

        // Any provider that appears in ANY slot must have a key somewhere.
        var providerMissingKey = providerKeys.FirstOrDefault(kv => string.IsNullOrWhiteSpace(kv.Value));

        if (providerMissingKey.Key is not null)
        {
            error = $"Provider '{providerMissingKey.Key}' is used but no API key was supplied.";

            return false;
        }

        //------------------------------------------------------------------
        // 5.  All good 🎉
        //------------------------------------------------------------------
        error = null;

        return true;
    }
}