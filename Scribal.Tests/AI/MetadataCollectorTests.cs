using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.SemanticKernel.ChatCompletion;
using Scribal.AI;
using ChatMessageContent = Microsoft.SemanticKernel.ChatMessageContent;

#pragma warning disable SKEXP0070 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

namespace Scribal.Tests.AI;

public class MetadataCollectorTests
{
    private readonly MetadataCollector _sut;
    private readonly long _testStartTimestamp;
    private readonly TimeSpan _expectedElapsedTime = TimeSpan.FromSeconds(5);

    public MetadataCollectorTests()
    {
        var mockTimeProvider = new FakeTimeProvider();
        _testStartTimestamp = mockTimeProvider.GetTimestamp();
        mockTimeProvider.Advance(_expectedElapsedTime);

        _sut = new(mockTimeProvider, NullLogger<MetadataCollector>.Instance);
    }

    [Fact]
    public void CollectMetadata_WithValidAnthropicMetadata_ReturnsCorrectTokensAndElapsed()
    {
        // Arrange
        var anthropicUsage = new Dictionary<string, object>
        {
            {
                "input_tokens", 30
            },
            {
                "output_tokens", 40
            }
        };
        var anthropicMetadata = new Dictionary<string, object>
        {
            {
                "usage", anthropicUsage
            }
        };
        var metadataDict = new Dictionary<string, object?>
        {
            {
                "anthropic_metadata", anthropicMetadata
            }
        };
        var message = new ChatMessageContent(AuthorRole.Assistant, "content", metadata: metadataDict);

        // Act
        var result = _sut.CollectMetadata("anthropic-sid", _testStartTimestamp, message);

        // Assert
        Assert.Equal(30, result.PromptTokens);
        Assert.Equal(40, result.CompletionTokens);
        Assert.Equal(_expectedElapsedTime, result.Elapsed);
    }

    [Fact]
    public void CollectMetadata_WithAnthropicMetadataMissingUsage_ReturnsZeroTokens()
    {
        // Arrange
        var anthropicMetadata = new Dictionary<string, object>(); // Missing "usage"
        var metadataDict = new Dictionary<string, object?>
        {
            {
                "anthropic_metadata", anthropicMetadata
            }
        };
        var message = new ChatMessageContent(AuthorRole.Assistant, "content", metadata: metadataDict);

        // Act
        var result = _sut.CollectMetadata("anthropic-sid", _testStartTimestamp, message);

        // Assert
        Assert.Equal(0, result.PromptTokens);
        Assert.Equal(0, result.CompletionTokens);
        Assert.Equal(_expectedElapsedTime, result.Elapsed);
    }

    [Fact]
    public void CollectMetadata_WithAnthropicMetadataUsageMissingTokens_ReturnsZeroOrPartialTokens()
    {
        // Arrange
        var anthropicUsage = new Dictionary<string, object>
        {
            // Missing "input_tokens" and "output_tokens"
        };
        var anthropicMetadata = new Dictionary<string, object>
        {
            {
                "usage", anthropicUsage
            }
        };
        var metadataDict = new Dictionary<string, object?>
        {
            {
                "anthropic_metadata", anthropicMetadata
            }
        };
        var message = new ChatMessageContent(AuthorRole.Assistant, "content", metadata: metadataDict);

        // Act
        var result = _sut.CollectMetadata("anthropic-sid", _testStartTimestamp, message);

        // Assert
        Assert.Equal(0, result.PromptTokens);
        Assert.Equal(0, result.CompletionTokens);
        Assert.Equal(_expectedElapsedTime, result.Elapsed);
    }

    [Fact]
    public void CollectMetadata_WithAnthropicMetadataUsagePartialTokens_ReturnsPartialTokens()
    {
        // Arrange
        var anthropicUsage = new Dictionary<string, object>
        {
            {
                "input_tokens", 50
            } // "output_tokens" missing
        };
        var anthropicMetadata = new Dictionary<string, object>
        {
            {
                "usage", anthropicUsage
            }
        };
        var metadataDict = new Dictionary<string, object?>
        {
            {
                "anthropic_metadata", anthropicMetadata
            }
        };
        var message = new ChatMessageContent(AuthorRole.Assistant, "content", metadata: metadataDict);

        // Act
        var result = _sut.CollectMetadata("anthropic-sid", _testStartTimestamp, message);

        // Assert
        Assert.Equal(50, result.PromptTokens);
        Assert.Equal(0, result.CompletionTokens); // Output tokens should be 0
        Assert.Equal(_expectedElapsedTime, result.Elapsed);
    }

    [Fact]
    public void CollectMetadata_WithNullMetadata_ReturnsZeroTokens()
    {
        // Arrange
        var message = new ChatMessageContent(AuthorRole.Assistant, "content", metadata: null);

        // Act
        var result = _sut.CollectMetadata("any-sid", _testStartTimestamp, message);

        // Assert
        Assert.Equal(0, result.PromptTokens);
        Assert.Equal(0, result.CompletionTokens);
        Assert.Equal(_expectedElapsedTime, result.Elapsed);
    }

    [Fact]
    public void CollectMetadata_WithEmptyMetadata_ReturnsZeroTokens()
    {
        // Arrange
        var message =
            new ChatMessageContent(AuthorRole.Assistant, "content", metadata: new Dictionary<string, object?>());

        // Act
        var result = _sut.CollectMetadata("any-sid", _testStartTimestamp, message);

        // Assert
        Assert.Equal(0, result.PromptTokens);
        Assert.Equal(0, result.CompletionTokens);
        Assert.Equal(_expectedElapsedTime, result.Elapsed);
    }

    [Fact]
    public void CollectMetadata_WithUnknownMetadataStructure_ReturnsZeroTokens()
    {
        // Arrange
        var metadataDict = new Dictionary<string, object?>
        {
            {
                "UnknownProvider", new
                {
                    P = 5,
                    C = 7
                }
            }
        };
        var message = new ChatMessageContent(AuthorRole.Assistant, "content", metadata: metadataDict);

        // Act
        var result = _sut.CollectMetadata("unknown-sid", _testStartTimestamp, message);

        // Assert
        Assert.Equal(0, result.PromptTokens);
        Assert.Equal(0, result.CompletionTokens);
        Assert.Equal(_expectedElapsedTime, result.Elapsed);
    }

    [Fact]
    public void CollectMetadata_OpenAiMetadataWithWrongUsageType_ReturnsZeroTokens()
    {
        // Arrange
        var metadataDict = new Dictionary<string, object?>
        {
            {
                "Usage", "not_a_ChatTokenUsage_object"
            }
        };
        var message = new ChatMessageContent(AuthorRole.Assistant, "content", metadata: metadataDict);

        // Act
        var result = _sut.CollectMetadata("openai-sid", _testStartTimestamp, message);

        // Assert
        Assert.Equal(0, result.PromptTokens);
        Assert.Equal(0, result.CompletionTokens);
        Assert.Equal(_expectedElapsedTime, result.Elapsed);
    }
}