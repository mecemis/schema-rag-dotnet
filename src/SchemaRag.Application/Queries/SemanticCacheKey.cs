using System.Security.Cryptography;
using System.Text;

using SchemaRag.Application.Indexing;
using SchemaRag.Domain.Schema;

namespace SchemaRag.Application.Queries;

/// <summary>
/// Derives the scope a cached statement belongs to. Cached SQL may only be
/// reused when the same data source still exposes the same tables with the same
/// column layout, so the fingerprint covers both the allow list and the schema
/// version of every table in it. Re-indexing a changed schema therefore retires
/// the affected cache entries without an explicit invalidation step.
/// </summary>
public static class SemanticCacheKey
{
    private const char FieldSeparator = '\u001f';
    private const char RecordSeparator = '\u001e';

    public static string CreateSchemaFingerprint(IReadOnlyList<SchemaDocument> allowedSchemas)
    {
        ArgumentNullException.ThrowIfNull(allowedSchemas);

        var content = new StringBuilder();
        foreach (var schema in allowedSchemas.OrderBy(schema => schema.TableId, StringComparer.Ordinal))
        {
            content.Append(schema.TableId)
                .Append(FieldSeparator)
                .Append(SchemaDocumentVersion.Create(schema))
                .Append(RecordSeparator);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Collapses the wording differences that should not produce a second cache
    /// entry, so repeating a question overwrites the point it wrote before.
    /// </summary>
    public static string NormalizeQuestion(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var normalized = new StringBuilder(question.Length);
        var pendingSeparator = false;
        foreach (var character in question.Trim())
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSeparator = normalized.Length > 0;
                continue;
            }

            if (pendingSeparator)
            {
                normalized.Append(' ');
                pendingSeparator = false;
            }

            normalized.Append(char.ToLowerInvariant(character));
        }

        return normalized.ToString();
    }
}