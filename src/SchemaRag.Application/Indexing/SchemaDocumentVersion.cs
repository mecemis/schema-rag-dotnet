using System.Security.Cryptography;
using System.Text;

using SchemaRag.Domain.Schema;

namespace SchemaRag.Application.Indexing;

public static class SchemaDocumentVersion
{
    public static string Create(SchemaDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var content = SchemaTextFormatter.Format(document);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}