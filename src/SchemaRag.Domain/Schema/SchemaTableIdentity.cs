using System.Security.Cryptography;
using System.Text;

namespace SchemaRag.Domain.Schema;

public static class SchemaTableIdentity
{
    public static string CreateId(
        string dataSourceId,
        string schemaName,
        string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataSourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var canonicalName = string.Join(
            '\u001f',
            dataSourceId,
            schemaName,
            tableName);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalName));
        return $"tbl_{Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant()}";
    }
}