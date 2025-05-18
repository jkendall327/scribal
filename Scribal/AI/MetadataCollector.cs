using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Connectors.Google;
using OpenAI.Chat;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

#pragma warning disable SKEXP0070 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Scribal.AI;

public class MetadataCollector(TimeProvider timeProvider, ILogger<MetadataCollector> logger)
{
    public ChatModels.Metadata CollectMetadata(string? sid, long startTimestamp, ChatMessageContent message)
    {
        var elapsed = timeProvider.GetElapsedTime(startTimestamp);
        var promptTokens = 0;
        var completionTokens = 0;

        if (TryExtractGeminiTokens(message, sid, out var geminiPromptTokens, out var geminiCompletionTokens))
        {
            promptTokens = geminiPromptTokens;
            completionTokens = geminiCompletionTokens;
        }
        else if (TryExtractOpenAiTokens(message, sid, out var openAiPromptTokens, out var openAiCompletionTokens))
        {
            promptTokens = openAiPromptTokens;
            completionTokens = openAiCompletionTokens;
        }
        else if (TryExtractAnthropicTokens(message,
                     sid,
                     out var anthropicPromptTokens,
                     out var anthropicCompletionTokens))
        {
            promptTokens = anthropicPromptTokens;
            completionTokens = anthropicCompletionTokens;
        }
        else
        {
            logger.LogWarning("Could not determine token usage from metadata for SID '{Sid}'. Metadata: {@Metadata}",
                sid,
                message.Metadata);
        }

        var metadata = new ChatModels.Metadata(elapsed, promptTokens, completionTokens);

        logger.LogDebug(
            "Final metadata for SID '{Sid}': Elapsed: {Elapsed}, PromptTokens: {PromptTokens}, CompletionTokens: {CompletionTokens}",
            sid,
            metadata.Elapsed,
            metadata.PromptTokens,
            metadata.CompletionTokens);

        return metadata;
    }

    private bool TryExtractGeminiTokens(ChatMessageContent message,
        string? sid,
        out int promptTokens,
        out int completionTokens)
    {
        promptTokens = 0;
        completionTokens = 0;

        if (message.Metadata is not GeminiMetadata geminiMetadata)
        {
            return false;
        }

        promptTokens = geminiMetadata.PromptTokenCount;
        completionTokens = geminiMetadata.CandidatesTokenCount;

        logger.LogInformation(
            "Collected Gemini metadata for SID '{Sid}'. Prompt tokens: {PromptTokens}, Completion tokens: {CompletionTokens}",
            sid,
            promptTokens,
            completionTokens);

        return true;
    }

    private bool TryExtractOpenAiTokens(ChatMessageContent message,
        string? sid,
        out int promptTokens,
        out int completionTokens)
    {
        promptTokens = 0;
        completionTokens = 0;

        if (message.Metadata?.TryGetValue("Usage", out var u) is not true || u is not ChatTokenUsage usage)
        {
            return false;
        }

        promptTokens = usage.InputTokenCount;
        completionTokens = usage.OutputTokenCount;

        logger.LogInformation(
            "Collected OpenAI metadata for SID '{Sid}'. Prompt tokens: {PromptTokens}, Completion tokens: {CompletionTokens}",
            sid,
            promptTokens,
            completionTokens);

        return true;
    }

    private bool TryExtractAnthropicTokens(ChatMessageContent message,
        string? sid,
        out int promptTokens,
        out int completionTokens)
    {
        promptTokens = 0;
        completionTokens = 0;

        if (message.Metadata?.TryGetValue("anthropic_metadata", out var anthropicMetaObj) != true ||
            anthropicMetaObj is not Dictionary<string, object> anthropicMetaDict)
        {
            return false;
        }

        if (anthropicMetaDict.TryGetValue("usage", out var usageObj) &&
            usageObj is Dictionary<string, object> usageDict)
        {
            if (usageDict.TryGetValue("input_tokens", out var inTokens) && inTokens is int pTokens)
            {
                promptTokens = pTokens;
            }

            if (usageDict.TryGetValue("output_tokens", out var outTokens) && outTokens is int cTokens)
            {
                completionTokens = cTokens;
            }

            logger.LogInformation(
                "Collected Anthropic metadata for SID '{Sid}'. Prompt tokens: {PromptTokens}, Completion tokens: {CompletionTokens}",
                sid,
                promptTokens,
                completionTokens);

            return true;
        }

        logger.LogWarning(
            "Anthropic metadata found for SID '{Sid}', but 'usage' dictionary is missing or not in the expected format",
            sid);

        return false; // Indicate that while anthropic_metadata was present, 'usage' was not as expected.
    }
}