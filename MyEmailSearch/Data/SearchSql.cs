namespace MyEmailSearch.Data;

public sealed record SqlStatement(string Text, IReadOnlyList<(string Name, object? Value)> Parameters);

public static class SearchSql
{
    public static readonly string[] Columns =
    [
        "id", "message_id", "file_path", "from_address", "from_name", "to_addresses", "cc_addresses",
        "bcc_addresses", "subject", "date_sent_unix", "date_received_unix", "folder", "account",
        "has_attachments", "attachment_names", "body_preview", "body_text", "indexed_at_unix",
        "last_modified_ticks"
    ];

    private static readonly string SelectList = string.Join(", ", Columns.Select(column => $"emails.{column}"));

    public static SqlStatement Select(SearchQuery query)
    {
        var (source, parameters, ranked) = Filter(query);
        parameters.Add(("@limit", query.Take));
        parameters.Add(("@offset", query.Skip));

        var order = ranked ? "bm25(emails_fts)" : "emails.date_sent_unix DESC";
        return new SqlStatement(
            $"SELECT {SelectList} {source} ORDER BY {order} LIMIT @limit OFFSET @offset;", parameters);
    }

    public static SqlStatement Count(SearchQuery query)
    {
        var (source, parameters, _) = Filter(query);
        return new SqlStatement($"SELECT COUNT(*) {source};", parameters);
    }

    public static string? FtsMatch(string? terms)
    {
        if (string.IsNullOrWhiteSpace(terms))
        {
            return null;
        }

        var phrase = terms.Trim();
        var isPrefix = phrase.EndsWith('*');
        if (isPrefix)
        {
            phrase = phrase[..^1].TrimEnd();
        }

        if (phrase.Length == 0)
        {
            return null;
        }

        var quoted = $"\"{phrase.Replace("\"", "\"\"")}\"";
        return isPrefix ? quoted + "*" : quoted;
    }

    private static (string Source, List<(string Name, object? Value)> Parameters, bool Ranked) Filter(SearchQuery query)
    {
        List<string> conditions = [];
        List<(string Name, object? Value)> parameters = [];
        var source = "FROM emails";

        var match = FtsMatch(query.ContentTerms);
        if (match is not null)
        {
            source = "FROM emails INNER JOIN emails_fts ON emails.id = emails_fts.rowid";
            conditions.Add("emails_fts MATCH @ftsQuery");
            parameters.Add(("@ftsQuery", match));
        }

        if (!string.IsNullOrWhiteSpace(query.FromAddress))
        {
            if (query.FromAddress.Contains('*'))
            {
                conditions.Add("emails.from_address LIKE @fromAddress");
                parameters.Add(("@fromAddress", query.FromAddress.Replace('*', '%')));
            }
            else
            {
                conditions.Add("emails.from_address = @fromAddress COLLATE NOCASE");
                parameters.Add(("@fromAddress", query.FromAddress));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.ToAddress))
        {
            conditions.Add("emails.to_addresses LIKE @toAddress");
            parameters.Add(("@toAddress", $"%{query.ToAddress}%"));
        }

        if (!string.IsNullOrWhiteSpace(query.Subject))
        {
            conditions.Add("emails.subject LIKE @subject");
            parameters.Add(("@subject", $"%{query.Subject}%"));
        }

        if (query.DateFrom is { } dateFrom)
        {
            conditions.Add("emails.date_sent_unix >= @dateFrom");
            parameters.Add(("@dateFrom", dateFrom.ToUnixTimeSeconds()));
        }

        if (query.DateTo is { } dateTo)
        {
            conditions.Add("emails.date_sent_unix <= @dateTo");
            parameters.Add(("@dateTo", dateTo.ToUnixTimeSeconds()));
        }

        if (!string.IsNullOrWhiteSpace(query.Account))
        {
            conditions.Add("emails.account = @account");
            parameters.Add(("@account", query.Account));
        }

        if (!string.IsNullOrWhiteSpace(query.Folder))
        {
            conditions.Add("emails.folder = @folder");
            parameters.Add(("@folder", query.Folder));
        }

        if (conditions.Count > 0)
        {
            source += " WHERE " + string.Join(" AND ", conditions);
        }

        return (source, parameters, match is not null);
    }
}
