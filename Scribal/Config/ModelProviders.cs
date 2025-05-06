using Microsoft.SemanticKernel;
#pragma warning disable SKEXP0070

namespace Scribal;

public interface IModelProvider
{
    string Name { get; }
    void RegisterServices(IKernelBuilder kb, ModelSlot slot, string serviceSuffix);
}

public class OpenAIModelProvider : IModelProvider
{
    public string Name => "OpenAI";

    public void RegisterServices(IKernelBuilder kb, ModelSlot slot, string serviceSuffix)
    {
        kb.AddOpenAIChatCompletion(modelId: slot.ModelId, apiKey: slot.ApiKey, serviceId: slot.Provider + serviceSuffix);
    }
}

public class GeminiModelProvider : IModelProvider
{
    public string Name => "Gemini";

    public void RegisterServices(IKernelBuilder kb, ModelSlot slot, string serviceSuffix)
    {
        kb.AddGoogleAIGeminiChatCompletion(modelId: slot.ModelId, apiKey: slot.ApiKey, serviceId: slot.Provider + serviceSuffix);
    }
}

public class DeepSeekModelProvider : IModelProvider
{
    public string Name => "DeepSeek";

    public void RegisterServices(IKernelBuilder kb, ModelSlot slot, string serviceSuffix)
    {
        kb.AddOpenAIChatCompletion(modelId: slot.ModelId,
            apiKey: slot.ApiKey,
            endpoint: new("https://api.deepseek.com"),
            serviceId: slot.Provider + serviceSuffix);
    }
}