namespace PowerRenameAvalonia.Models;

public enum RenameScope
{
    NameAndExtension,
    NameOnly,
    ExtensionOnly
}

public enum TextFormatting
{
    None,
    Lowercase,    // aa
    Uppercase,    // AA
    TitleCase,    // Aa
    SentenceCase  // Aa Aa
}

public enum RenameStatus
{
    Pending,
    Renamed,
    Error,
    Conflict
}
