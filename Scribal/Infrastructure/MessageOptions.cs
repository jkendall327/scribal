namespace Scribal;

public enum MessageType
{
    None,
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

public record MessageOptions(MessageType Type = MessageType.None, MessageStyle Style = MessageStyle.None);