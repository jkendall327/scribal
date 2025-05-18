namespace Scribal;

public enum MessageType
{
    Informational,
    Hint,
    Warning,
    Error
}

public enum MessageStyle
{
    None,
    Italics,
    Bold,
    Underline
}

public record MessageOptions(MessageType Type = MessageType.Informational, MessageStyle Style = MessageStyle.None);