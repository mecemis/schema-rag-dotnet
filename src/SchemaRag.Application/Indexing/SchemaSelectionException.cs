namespace SchemaRag.Application.Indexing;

public sealed class SchemaSelectionException(string message) : ArgumentException(message);