using System.Text;

using Npgsql;

using NpgsqlTypes;

using SchemaRag.Application.Abstractions;
using SchemaRag.Domain.Schema;
using SchemaRag.Infrastructure.DataSources;

namespace SchemaRag.Infrastructure.Schema;

public sealed class PostgresSchemaCatalog(
    ConfiguredPostgresDataSourceRegistry dataSourceRegistry) : ISchemaCatalog
{
    private const int MaxTableDescriptionLength = 500;
    private const int MaxColumnDescriptionLength = 300;

    public async Task<IReadOnlyList<SchemaDocument>> GetAllAsync(
        string dataSourceId,
        CancellationToken cancellationToken)
    {
        dataSourceRegistry.GetRequired(dataSourceId);
        var includedSchemas = dataSourceRegistry.GetIncludedSchemas(dataSourceId);
        await using var connection = await dataSourceRegistry.OpenReadOnlyConnectionAsync(
            dataSourceId,
            cancellationToken);

        var columns = await ReadColumnsAsync(connection, includedSchemas, cancellationToken);
        var relationships = await ReadRelationshipsAsync(
            connection,
            includedSchemas,
            cancellationToken);

        return BuildDocuments(dataSourceId, columns, relationships);
    }

    private static async Task<IReadOnlyList<ColumnMetadata>> ReadColumnsAsync(
        NpgsqlConnection connection,
        IReadOnlyList<string> includedSchemas,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                table_namespace.nspname AS schema_name,
                table_class.relname AS table_name,
                COALESCE(pg_catalog.obj_description(table_class.oid, 'pg_class'), '') AS table_description,
                attribute.attnum AS ordinal_position,
                attribute.attname AS column_name,
                pg_catalog.format_type(attribute.atttypid, attribute.atttypmod) AS data_type,
                NOT attribute.attnotnull AS is_nullable,
                COALESCE(pg_catalog.col_description(table_class.oid, attribute.attnum), '') AS column_description,
                EXISTS (
                    SELECT 1
                    FROM pg_catalog.pg_constraint primary_key
                    WHERE primary_key.conrelid = table_class.oid
                      AND primary_key.contype = 'p'
                      AND attribute.attnum = ANY(primary_key.conkey)
                ) AS is_primary_key
            FROM pg_catalog.pg_class table_class
            JOIN pg_catalog.pg_namespace table_namespace
              ON table_namespace.oid = table_class.relnamespace
            JOIN pg_catalog.pg_attribute attribute
              ON attribute.attrelid = table_class.oid
            WHERE table_class.relkind IN ('r', 'p')
              AND attribute.attnum > 0
              AND NOT attribute.attisdropped
              AND table_class.relname <> '__EFMigrationsHistory'
              AND table_namespace.nspname = ANY(@included_schemas)
            ORDER BY table_namespace.nspname, table_class.relname, attribute.attnum
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(
            "included_schemas",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            includedSchemas.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<ColumnMetadata>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ColumnMetadata(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt16(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6),
                reader.GetString(7),
                reader.GetBoolean(8)));
        }

        return rows;
    }

    private static async Task<IReadOnlyList<RelationshipMetadata>> ReadRelationshipsAsync(
        NpgsqlConnection connection,
        IReadOnlyList<string> includedSchemas,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                source_namespace.nspname AS source_schema,
                source_table.relname AS source_table,
                source_column.attname AS source_column,
                target_namespace.nspname AS target_schema,
                target_table.relname AS target_table,
                target_column.attname AS target_column
            FROM pg_catalog.pg_constraint foreign_key
            JOIN pg_catalog.pg_class source_table
              ON source_table.oid = foreign_key.conrelid
            JOIN pg_catalog.pg_namespace source_namespace
              ON source_namespace.oid = source_table.relnamespace
            JOIN pg_catalog.pg_class target_table
              ON target_table.oid = foreign_key.confrelid
            JOIN pg_catalog.pg_namespace target_namespace
              ON target_namespace.oid = target_table.relnamespace
            JOIN LATERAL unnest(foreign_key.conkey) WITH ORDINALITY source_key(attnum, position)
              ON true
            JOIN LATERAL unnest(foreign_key.confkey) WITH ORDINALITY target_key(attnum, position)
              ON target_key.position = source_key.position
            JOIN pg_catalog.pg_attribute source_column
              ON source_column.attrelid = source_table.oid
             AND source_column.attnum = source_key.attnum
            JOIN pg_catalog.pg_attribute target_column
              ON target_column.attrelid = target_table.oid
             AND target_column.attnum = target_key.attnum
            WHERE foreign_key.contype = 'f'
              AND source_namespace.nspname = ANY(@included_schemas)
              AND target_namespace.nspname = ANY(@included_schemas)
            ORDER BY source_namespace.nspname, source_table.relname, foreign_key.conname, source_key.position
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(
            "included_schemas",
            NpgsqlDbType.Array | NpgsqlDbType.Text,
            includedSchemas.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<RelationshipMetadata>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new RelationshipMetadata(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5)));
        }

        return rows;
    }

    internal static IReadOnlyList<SchemaDocument> BuildDocuments(
        string dataSourceId,
        IReadOnlyList<ColumnMetadata> columnMetadata,
        IReadOnlyList<RelationshipMetadata> relationshipMetadata)
    {
        var relationships = CreateRelationships(relationshipMetadata);

        return columnMetadata
            .GroupBy(column => (column.SchemaName, column.TableName))
            .OrderBy(group => group.Key.SchemaName, StringComparer.Ordinal)
            .ThenBy(group => group.Key.TableName, StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First();
                var tableKey = (first.SchemaName, first.TableName);
                var columns = group
                    .OrderBy(column => column.OrdinalPosition)
                    .Select(column => new SchemaColumn(
                        column.ColumnName,
                        column.DataType,
                        NormalizeMetadata(
                            column.ColumnDescription,
                            $"{column.DataType} column.",
                            MaxColumnDescriptionLength),
                        column.IsNullable,
                        column.IsPrimaryKey))
                    .ToArray();

                return new SchemaDocument(
                    dataSourceId,
                    first.SchemaName,
                    first.TableName,
                    NormalizeMetadata(
                        first.TableDescription,
                        $"PostgreSQL table {first.SchemaName}.{first.TableName}.",
                        MaxTableDescriptionLength),
                    columns,
                    relationships.TryGetValue(tableKey, out var tableRelationships)
                        ? tableRelationships
                        : []);
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<(string SchemaName, string TableName), IReadOnlyList<SchemaRelationship>>
        CreateRelationships(IReadOnlyList<RelationshipMetadata> metadata)
    {
        var relationships = metadata.SelectMany(item => new[]
        {
            new RelationshipEntry(
                (item.SourceSchema, item.SourceTable),
                new SchemaRelationship(
                    item.SourceColumn,
                    item.TargetSchema,
                    item.TargetTable,
                    item.TargetColumn,
                    $"References {item.TargetSchema}.{item.TargetTable}.")),
            new RelationshipEntry(
                (item.TargetSchema, item.TargetTable),
                new SchemaRelationship(
                    item.TargetColumn,
                    item.SourceSchema,
                    item.SourceTable,
                    item.SourceColumn,
                    $"Referenced by {item.SourceSchema}.{item.SourceTable}."))
        });

        return relationships
            .GroupBy(entry => entry.Table)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SchemaRelationship>)group
                    .Select(entry => entry.Relationship)
                    .Distinct()
                    .OrderBy(relationship => relationship.Column, StringComparer.Ordinal)
                    .ThenBy(relationship => relationship.ReferencedSchemaName, StringComparer.Ordinal)
                    .ThenBy(relationship => relationship.ReferencedTable, StringComparer.Ordinal)
                    .ToArray());
    }

    private static string NormalizeMetadata(string value, string fallback, int maximumLength)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value;
        var normalized = new StringBuilder(Math.Min(source.Length, maximumLength));
        var previousWasWhitespace = false;
        foreach (var character in source)
        {
            if (normalized.Length >= maximumLength)
            {
                break;
            }

            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                if (!previousWasWhitespace)
                {
                    normalized.Append(' ');
                    previousWasWhitespace = true;
                }

                continue;
            }

            normalized.Append(character switch
            {
                '<' => '‹',
                '>' => '›',
                _ => character
            });
            previousWasWhitespace = false;
        }

        return normalized.ToString().Trim();
    }

    internal sealed record ColumnMetadata(
        string SchemaName,
        string TableName,
        string TableDescription,
        short OrdinalPosition,
        string ColumnName,
        string DataType,
        bool IsNullable,
        string ColumnDescription,
        bool IsPrimaryKey);

    internal sealed record RelationshipMetadata(
        string SourceSchema,
        string SourceTable,
        string SourceColumn,
        string TargetSchema,
        string TargetTable,
        string TargetColumn);

    private sealed record RelationshipEntry(
        (string SchemaName, string TableName) Table,
        SchemaRelationship Relationship);
}