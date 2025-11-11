using System.Text.RegularExpressions;

public static class QueryAnalyzer
{
    /// <summary>
    /// Analyzes a SQL query to determine if it filters on a single partition key value.
    /// Returns the partition key value if detected, otherwise null.
    /// </summary>
    public static string? ExtractPartitionKeyValue(string query, string partitionKeyPath)
    {
        // Remove leading slash from partition key path and get the property name
        var partitionKeyProperty = partitionKeyPath.TrimStart('/').Split('/').Last();

        // Normalize the query: remove extra whitespace and convert to lowercase for analysis
        var normalizedQuery = Regex.Replace(query, @"\s+", " ").Trim();

        // Pattern to match partition key equality in WHERE clause
        // Supports formats like:
        // - WHERE c.partitionKey = 'value'
        // - WHERE c.partitionKey = "value"
        // - WHERE c['partitionKey'] = 'value'
        // - WHERE partitionKey = 'value'
        var patterns = new[]
        {
            // c.property = 'value' or c.property = "value"
            $@"\b(?:c\.{Regex.Escape(partitionKeyProperty)}|{Regex.Escape(partitionKeyProperty)})\s*=\s*['""]([^'""]+)['""]",
            // c['property'] = 'value' or c["property"] = "value"
            $@"\bc\[['""]({Regex.Escape(partitionKeyProperty)})['""]\]\s*=\s*['""]([^'""]+)['""]"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(normalizedQuery, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                // For the second pattern, the value is in the third group
                var valueGroup = match.Groups.Count > 2 ? match.Groups[2] : match.Groups[1];
                var value = valueGroup.Value;

                // Verify that there are no OR conditions that might include other partition keys
                if (ContainsOrCondition(normalizedQuery))
                {
                    return null;
                }

                // Verify that the partition key is only compared with equality (not >, <, !=, etc.)
                if (ContainsNonEqualityComparison(normalizedQuery, partitionKeyProperty))
                {
                    return null;
                }

                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a query contains OR conditions that might span multiple partitions
    /// </summary>
    private static bool ContainsOrCondition(string normalizedQuery)
    {
        // Look for OR keyword (not inside quotes)
        var orPattern = @"\bOR\b";
        return Regex.IsMatch(normalizedQuery, orPattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Checks if the partition key is used with non-equality operators
    /// </summary>
    private static bool ContainsNonEqualityComparison(string normalizedQuery, string partitionKeyProperty)
    {
        // Look for partition key with non-equality operators (>, <, !=, <>, >=, <=, IN, BETWEEN)
        var nonEqualityPatterns = new[]
        {
            $@"\b(?:c\.{Regex.Escape(partitionKeyProperty)}|{Regex.Escape(partitionKeyProperty)})\s*(?:!=|<>|>|<|>=|<=)",
            $@"\b(?:c\.{Regex.Escape(partitionKeyProperty)}|{Regex.Escape(partitionKeyProperty)})\s+(?:IN|BETWEEN)\b"
        };

        return nonEqualityPatterns.Any(pattern =>
            Regex.IsMatch(normalizedQuery, pattern, RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// Determines if the query can use DeleteAllItemsInPartitionKey based on query analysis
    /// </summary>
    public static bool CanUseDeleteAllItemsInPartition(string query, string partitionKeyPath)
    {
        var partitionKeyValue = ExtractPartitionKeyValue(query, partitionKeyPath);
        return partitionKeyValue != null;
    }
}
