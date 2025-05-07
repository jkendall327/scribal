using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.Google;
using OpenAI.Chat;

#pragma warning disable SKEXP0070 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Scribal.AI;

public class MetadataCollector(TimeProvider timeProvider, ILogger<MetadataCollector> logger)
{
    public ChatStreamItem.Metadata Collect(string? sid, long startTimestamp, ChatMessageContent finalMessageContent)
    {
        var elapsed = timeProvider.GetElapsedTime(startTimestamp);
        int promptTokens = 0;
        int completionTokens = 0;

        if (finalMessageContent.Metadata is GeminiMetadata geminiMetadata)
        {
            promptTokens = geminiMetadata.PromptTokenCount;
            completionTokens = geminiMetadata.CandidatesTokenCount;
            logger.LogInformation("Collected Gemini metadata for SID '{Sid}'. Prompt tokens: {PromptTokens}, Completion tokens: {CompletionTokens}", sid, promptTokens, completionTokens);
        }
        // OpenAI format
        else if (finalMessageContent.Metadata?.TryGetValue("Usage", out var u) is true && u is ChatTokenUsage usage)
        {
            promptTokens = usage.InputTokenCount;
            completionTokens = usage.OutputTokenCount;
            logger.LogInformation("Collected OpenAI metadata for SID '{Sid}'. Prompt tokens: {PromptTokens}, Completion tokens: {CompletionTokens}", sid, promptTokens, completionTokens);
        }
        // Anthropic format
        else if (finalMessageContent.Metadata?.TryGetValue("anthropic_metadata", out var anthropicMetaObj) == true &&
                 anthropicMetaObj is Dictionary<string, object> anthropicMetaDict)
        {
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
                logger.LogInformation("Collected Anthropic metadata for SID '{Sid}'. Prompt tokens: {PromptTokens}, Completion tokens: {CompletionTokens}", sid, promptTokens, completionTokens);
            }
            else
            {
                logger.LogWarning("Anthropic metadata found for SID '{Sid}', but 'usage' dictionary is missing or not in the expected format.", sid);
            }
        }
        else
        {
            logger.LogWarning("Could not determine token usage from metadata for SID '{Sid}'. Metadata: {@Metadata}", sid, finalMessageContent.Metadata);
        }

        var metadata = new ChatStreamItem.Metadata(
            Elapsed: elapsed,
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens);

        logger.LogDebug("Final metadata for SID '{Sid}': Elapsed: {Elapsed}, PromptTokens: {PromptTokens}, CompletionTokens: {CompletionTokens}",
            sid, metadata.Elapsed, metadata.PromptTokens, metadata.CompletionTokens);
        
        return metadata;
    }
}
