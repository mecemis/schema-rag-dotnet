using System.Globalization;

using SchemaRag.Domain.Schema;

namespace SchemaRag.Application.Sql;

public sealed class SqlQueryValidator
{
    private static readonly HashSet<string> ForbiddenKeywords = new(
        [
            "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE",
            "TRUNCATE", "MERGE", "GRANT", "REVOKE", "COPY", "CALL",
            "DO", "LOCK"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SystemSchemas = new(
        ["PG_CATALOG", "INFORMATION_SCHEMA", "PG_TOAST", "PG_TEMP"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ForbiddenFunctions = new(
        [
            "PG_SLEEP", "SET_CONFIG", "PG_ADVISORY_LOCK", "PG_ADVISORY_XACT_LOCK",
            "PG_TRY_ADVISORY_LOCK", "PG_TRY_ADVISORY_XACT_LOCK", "PG_CANCEL_BACKEND",
            "PG_TERMINATE_BACKEND", "LO_IMPORT", "LO_EXPORT", "DBLINK"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly int _maxRows;

    public SqlQueryValidator(int maxRows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRows, 1);
        _maxRows = maxRows;
    }

    public SqlValidationResult Validate(
        string? candidate,
        IReadOnlyList<SchemaDocument>? allowedSchemas = null)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return SqlValidationResult.Failure("The model returned empty SQL.");
        }

        var sql = candidate.Trim();
        if (sql.Contains('`', StringComparison.Ordinal))
        {
            return SqlValidationResult.Failure("Markdown code fences are not allowed.");
        }

        var scan = Scan(sql);
        if (scan.Error is not null)
        {
            return SqlValidationResult.Failure(scan.Error);
        }

        if (scan.SemicolonIndexes.Count > 1 ||
            (scan.SemicolonIndexes.Count == 1 && scan.SemicolonIndexes[0] != sql.Length - 1))
        {
            return SqlValidationResult.Failure("Multiple SQL statements are not allowed.");
        }

        if (scan.SemicolonIndexes.Count == 1)
        {
            sql = sql[..^1].TrimEnd();
            scan = Scan(sql);
        }

        var tokens = scan.Tokens;
        if (tokens.Count == 0 || !IsAllowedStart(tokens[0].Text))
        {
            return SqlValidationResult.Failure("SQL must begin with SELECT or WITH.");
        }

        var unsafeToken = tokens.FirstOrDefault(token =>
            !token.IsQuoted && ForbiddenKeywords.Contains(token.Text));
        if (unsafeToken is not null)
        {
            return SqlValidationResult.Failure(
                $"The keyword {unsafeToken.Text.ToUpperInvariant()} is not allowed.");
        }

        if (tokens.Any(token => SystemSchemas.Contains(token.Text)))
        {
            return SqlValidationResult.Failure("PostgreSQL system schemas are not allowed.");
        }

        if (tokens.Any(token =>
                !token.IsQuoted && token.Text.Equals("INTO", StringComparison.OrdinalIgnoreCase)))
        {
            return SqlValidationResult.Failure("SELECT INTO is not allowed.");
        }

        if (ContainsRowLockClause(tokens))
        {
            return SqlValidationResult.Failure("Row-locking clauses are not allowed.");
        }

        if (tokens.Any(token =>
                !token.IsQuoted && token.Text.Equals("FETCH", StringComparison.OrdinalIgnoreCase)))
        {
            return SqlValidationResult.Failure("FETCH row limits are not supported.");
        }

        if (tokens.Any(token =>
                ForbiddenFunctions.Contains(token.Text) &&
                IsFunctionCall(sql, token)))
        {
            return SqlValidationResult.Failure("The query calls a forbidden PostgreSQL function.");
        }

        if (tokens[0].Text.Equals("WITH", StringComparison.OrdinalIgnoreCase) &&
            !tokens.Any(token =>
                token.Depth == 0 &&
                !token.IsQuoted &&
                token.Text.Equals("SELECT", StringComparison.OrdinalIgnoreCase)))
        {
            return SqlValidationResult.Failure("A CTE must end in a SELECT query.");
        }

        if (allowedSchemas is not null)
        {
            var relationValidation = ValidateRelations(sql, scan, allowedSchemas);
            if (relationValidation is not null)
            {
                return relationValidation;
            }
        }

        return ApplyMaximumLimit(sql, tokens);
    }

    private static SqlValidationResult? ValidateRelations(
        string sql,
        SqlScanResult scan,
        IReadOnlyList<SchemaDocument> allowedSchemas)
    {
        var cteNames = FindCteNames(scan.Tokens);
        var allowedQualifiedNames = allowedSchemas
            .Select(schema => schema.QualifiedTableName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < scan.Tokens.Count; index++)
        {
            var keyword = scan.Tokens[index];
            if (keyword.IsQuoted ||
                (!keyword.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase) &&
                 !keyword.Text.Equals("JOIN", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (keyword.Text.Equals("FROM", StringComparison.OrdinalIgnoreCase) &&
                ContainsCommaJoin(scan, keyword, index))
            {
                return SqlValidationResult.Failure("Comma-separated table lists are not allowed; use explicit JOIN clauses.");
            }

            if (index + 1 >= scan.Tokens.Count)
            {
                return SqlValidationResult.Failure("A table reference is required after FROM or JOIN.");
            }

            var relationToken = scan.Tokens[index + 1];
            if (relationToken.Text.Equals("SELECT", StringComparison.OrdinalIgnoreCase) &&
                relationToken.Depth > keyword.Depth)
            {
                continue;
            }

            var relationName = relationToken.Text;
            if (index + 2 < scan.Tokens.Count &&
                scan.Tokens[index + 2].Depth == relationToken.Depth &&
                HasDotBetween(sql, relationToken, scan.Tokens[index + 2]))
            {
                relationName = $"{relationToken.Text}.{scan.Tokens[index + 2].Text}";
            }

            if (cteNames.Contains(relationName))
            {
                continue;
            }

            if (!relationName.Contains('.', StringComparison.Ordinal))
            {
                return SqlValidationResult.Failure(
                    $"Table '{relationName}' must be schema-qualified.");
            }

            if (!allowedQualifiedNames.Contains(relationName))
            {
                return SqlValidationResult.Failure(
                    $"Table '{relationName}' is not part of the retrieved schema context.");
            }
        }

        return null;
    }

    private static HashSet<string> FindCteNames(IReadOnlyList<SqlToken> tokens)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tokens.Count == 0 || !tokens[0].Text.Equals("WITH", StringComparison.OrdinalIgnoreCase))
        {
            return names;
        }

        var topLevelTokens = tokens.Where(token => token.Depth == 0).ToArray();
        for (var index = 1; index < topLevelTokens.Length - 1; index++)
        {
            if (topLevelTokens[index].Text.Equals("SELECT", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (topLevelTokens[index + 1].Text.Equals("AS", StringComparison.OrdinalIgnoreCase) &&
                !topLevelTokens[index].Text.Equals("RECURSIVE", StringComparison.OrdinalIgnoreCase))
            {
                names.Add(topLevelTokens[index].Text);
            }
        }

        return names;
    }

    private static bool ContainsCommaJoin(
        SqlScanResult scan,
        SqlToken fromToken,
        int fromTokenIndex)
    {
        var end = scan.Tokens
            .Skip(fromTokenIndex + 1)
            .FirstOrDefault(token =>
                token.Depth == fromToken.Depth &&
                !token.IsQuoted &&
                IsFromClauseBoundary(token.Text));
        var endPosition = end?.Start ?? int.MaxValue;

        return scan.Commas.Any(comma =>
            comma.Depth == fromToken.Depth &&
            comma.Position > fromToken.Start &&
            comma.Position < endPosition);
    }

    private static bool IsFromClauseBoundary(string token) =>
        token.Equals("JOIN", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("WHERE", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("GROUP", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("HAVING", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("ORDER", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("LIMIT", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("OFFSET", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("UNION", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("INTERSECT", StringComparison.OrdinalIgnoreCase) ||
        token.Equals("EXCEPT", StringComparison.OrdinalIgnoreCase);

    private static bool HasDotBetween(string sql, SqlToken first, SqlToken second)
    {
        var between = sql.AsSpan(first.Start + first.Length, second.Start - first.Start - first.Length);
        var sawDot = false;
        foreach (var character in between)
        {
            if (character == '.')
            {
                if (sawDot)
                {
                    return false;
                }

                sawDot = true;
                continue;
            }

            if (!char.IsWhiteSpace(character))
            {
                return false;
            }
        }

        return sawDot;
    }

    private SqlValidationResult ApplyMaximumLimit(
        string sql,
        IReadOnlyList<SqlToken> tokens)
    {
        var topLevelTokens = tokens.Where(token => token.Depth == 0).ToArray();
        var limitIndexes = topLevelTokens
            .Select((token, index) => (token, index))
            .Where(item =>
                !item.token.IsQuoted &&
                item.token.Text.Equals("LIMIT", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (limitIndexes.Length > 1)
        {
            return SqlValidationResult.Failure("Only one top-level LIMIT is allowed.");
        }

        if (limitIndexes.Length == 0)
        {
            return AddLimitBeforeOffset(sql, topLevelTokens);
        }

        var limitIndex = limitIndexes[0].index;
        if (limitIndex + 1 >= topLevelTokens.Length ||
            !int.TryParse(
                topLevelTokens[limitIndex + 1].Text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var rowLimit))
        {
            return SqlValidationResult.Failure("LIMIT must use a positive integer.");
        }


        if (!ContainsOnlyWhitespace(
                sql,
                topLevelTokens[limitIndex],
                topLevelTokens[limitIndex + 1]))
        {
            return SqlValidationResult.Failure("LIMIT must use a positive integer.");
        }

        if (rowLimit < 1 || !HasValidLimitTail(sql, topLevelTokens, limitIndex + 2))
        {
            return SqlValidationResult.Failure("LIMIT must use a positive integer.");
        }

        if (rowLimit <= _maxRows)
        {
            return SqlValidationResult.Success(sql);
        }

        var valueToken = topLevelTokens[limitIndex + 1];
        var normalized = string.Concat(
            sql.AsSpan(0, valueToken.Start),
            _maxRows.ToString(CultureInfo.InvariantCulture),
            sql.AsSpan(valueToken.Start + valueToken.Length));

        return SqlValidationResult.Success(normalized);
    }

    private SqlValidationResult AddLimitBeforeOffset(
        string sql,
        IReadOnlyList<SqlToken> topLevelTokens)
    {
        var offsetIndex = topLevelTokens.FindIndex(token =>
            !token.IsQuoted && token.Text.Equals("OFFSET", StringComparison.OrdinalIgnoreCase));

        if (offsetIndex < 0)
        {
            return SqlValidationResult.Success($"{sql} LIMIT {_maxRows}");
        }

        if (offsetIndex + 1 >= topLevelTokens.Count ||
            !int.TryParse(topLevelTokens[offsetIndex + 1].Text, out var offset) ||
            offset < 0 ||
            !ContainsOnlyWhitespace(
                sql,
                topLevelTokens[offsetIndex],
                topLevelTokens[offsetIndex + 1]) ||
            offsetIndex + 2 != topLevelTokens.Count)
        {
            return SqlValidationResult.Failure("OFFSET must use a non-negative integer.");
        }

        var offsetToken = topLevelTokens[offsetIndex];
        var normalized = string.Concat(
            sql.AsSpan(0, offsetToken.Start),
            $"LIMIT {_maxRows} ",
            sql.AsSpan(offsetToken.Start));
        return SqlValidationResult.Success(normalized);
    }

    private static bool HasValidLimitTail(
        string sql,
        IReadOnlyList<SqlToken> topLevelTokens,
        int nextIndex)
    {
        if (nextIndex == topLevelTokens.Count)
        {
            return true;
        }

        return nextIndex + 2 == topLevelTokens.Count &&
            topLevelTokens[nextIndex].Text.Equals("OFFSET", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(topLevelTokens[nextIndex + 1].Text, out var offset) &&
            offset >= 0 &&
            ContainsOnlyWhitespace(
                sql,
                topLevelTokens[nextIndex],
                topLevelTokens[nextIndex + 1]);
    }

    private static bool ContainsOnlyWhitespace(
        string sql,
        SqlToken first,
        SqlToken second)
    {
        var start = first.Start + first.Length;
        for (var index = start; index < second.Start; index++)
        {
            if (!char.IsWhiteSpace(sql[index]))
            {
                return false;
            }
        }

        return second.Start > start;
    }

    private static bool IsFunctionCall(string sql, SqlToken token)
    {
        for (var index = token.Start + token.Length; index < sql.Length; index++)
        {
            if (char.IsWhiteSpace(sql[index]))
            {
                continue;
            }

            return sql[index] == '(';
        }

        return false;
    }

    private static bool ContainsRowLockClause(IReadOnlyList<SqlToken> tokens)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (token.IsQuoted || !token.Text.Equals("FOR", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var following = tokens
                .Skip(index + 1)
                .Take(3)
                .Where(candidate => candidate.Depth == token.Depth && !candidate.IsQuoted)
                .Select(candidate => candidate.Text)
                .ToArray();
            if (following.Length >= 1 &&
                (following[0].Equals("UPDATE", StringComparison.OrdinalIgnoreCase) ||
                 following[0].Equals("SHARE", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if (following.Length >= 2 &&
                following[0].Equals("KEY", StringComparison.OrdinalIgnoreCase) &&
                following[1].Equals("SHARE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (following.Length >= 3 &&
                following[0].Equals("NO", StringComparison.OrdinalIgnoreCase) &&
                following[1].Equals("KEY", StringComparison.OrdinalIgnoreCase) &&
                following[2].Equals("UPDATE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAllowedStart(string value) =>
        value.Equals("SELECT", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("WITH", StringComparison.OrdinalIgnoreCase);

    private static SqlScanResult Scan(string sql)
    {
        var tokens = new List<SqlToken>();
        var semicolonIndexes = new List<int>();
        var commas = new List<SqlPunctuation>();
        var depth = 0;

        for (var index = 0; index < sql.Length;)
        {
            var current = sql[index];
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                return SqlScanResult.Fail("SQL comments are not allowed.");
            }

            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                return SqlScanResult.Fail("SQL comments are not allowed.");
            }

            if (current == '$' && IsDollarQuoteStart(sql, index))
            {
                return SqlScanResult.Fail("Dollar-quoted strings are not supported.");
            }

            if (current == '\'')
            {
                if (!SkipQuoted(sql, ref index, '\''))
                {
                    return SqlScanResult.Fail("SQL contains an unterminated string literal.");
                }

                continue;
            }

            if (current == '"')
            {
                var start = index;
                var quotedValue = ReadQuotedIdentifier(sql, ref index);
                if (quotedValue is null)
                {
                    return SqlScanResult.Fail("SQL contains an unterminated quoted identifier.");
                }

                tokens.Add(new SqlToken(quotedValue, start, index - start, depth, true));
                continue;
            }

            if (current == '(')
            {
                depth++;
                index++;
                continue;
            }

            if (current == ')')
            {
                depth--;
                if (depth < 0)
                {
                    return SqlScanResult.Fail("SQL contains unbalanced parentheses.");
                }

                index++;
                continue;
            }

            if (current == ';')
            {
                semicolonIndexes.Add(index);
                index++;
                continue;
            }

            if (current == ',')
            {
                commas.Add(new SqlPunctuation(index, depth));
                index++;
                continue;
            }

            if (char.IsLetterOrDigit(current) || current == '_')
            {
                var start = index;
                while (index < sql.Length &&
                       (char.IsLetterOrDigit(sql[index]) || sql[index] is '_' or '$'))
                {
                    index++;
                }

                tokens.Add(new SqlToken(sql[start..index], start, index - start, depth, false));
                continue;
            }

            index++;
        }

        return depth == 0
            ? new SqlScanResult(tokens, semicolonIndexes, commas, null)
            : SqlScanResult.Fail("SQL contains unbalanced parentheses.");
    }

    private static bool SkipQuoted(string sql, ref int index, char quote)
    {
        index++;
        while (index < sql.Length)
        {
            if (sql[index] != quote)
            {
                index++;
                continue;
            }

            if (index + 1 < sql.Length && sql[index + 1] == quote)
            {
                index += 2;
                continue;
            }

            index++;
            return true;
        }

        return false;
    }

    private static string? ReadQuotedIdentifier(string sql, ref int index)
    {
        index++;
        var valueStart = index;
        while (index < sql.Length)
        {
            if (sql[index] != '"')
            {
                index++;
                continue;
            }

            if (index + 1 < sql.Length && sql[index + 1] == '"')
            {
                index += 2;
                continue;
            }

            var value = sql[valueStart..index].Replace("\"\"", "\"", StringComparison.Ordinal);
            index++;
            return value;
        }

        return null;
    }

    private static bool IsDollarQuoteStart(string sql, int index)
    {
        var closingDollar = sql.IndexOf('$', index + 1);
        if (closingDollar < 0)
        {
            return false;
        }

        for (var position = index + 1; position < closingDollar; position++)
        {
            if (!char.IsLetterOrDigit(sql[position]) && sql[position] != '_')
            {
                return false;
            }
        }

        return true;
    }

    private sealed record SqlToken(
        string Text,
        int Start,
        int Length,
        int Depth,
        bool IsQuoted);

    private sealed record SqlPunctuation(int Position, int Depth);

    private sealed record SqlScanResult(
        IReadOnlyList<SqlToken> Tokens,
        IReadOnlyList<int> SemicolonIndexes,
        IReadOnlyList<SqlPunctuation> Commas,
        string? Error)
    {
        public static SqlScanResult Fail(string error) => new([], [], [], error);
    }
}

internal static class ReadOnlyListExtensions
{
    public static int FindIndex<T>(
        this IReadOnlyList<T> source,
        Func<T, bool> predicate)
    {
        for (var index = 0; index < source.Count; index++)
        {
            if (predicate(source[index]))
            {
                return index;
            }
        }

        return -1;
    }
}