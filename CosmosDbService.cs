using System.Text.Json;
using Azure.Identity;
using Microsoft.Azure.Cosmos;

public class CosmosDbService : IDisposable
{
    private readonly CosmosClient _client;
    private readonly Container _container;
    private readonly string _partitionKeyPath;

    public string PartitionKeyPath => _partitionKeyPath;

    public CosmosDbService(string endpoint, string database, string containerName)
    {
        var credential = new DefaultAzureCredential();
        _client = new CosmosClient(endpoint, credential);
        _container = _client.GetContainer(database, containerName);

        // Get partition key path from container metadata
        var containerProperties = _container.ReadContainerAsync().GetAwaiter().GetResult();
        _partitionKeyPath = containerProperties.Resource.PartitionKeyPath;
    }

    public async Task<List<(string Id, PartitionKey PartitionKey, JsonDocument Document)>> FetchItemsAsync(string query, int? limit = null)
    {
        var queryDefinition = new QueryDefinition(query);
        var iterator = _container.GetItemQueryIterator<JsonDocument>(queryDefinition);

        var items = new List<(string Id, PartitionKey PartitionKey, JsonDocument Document)>();

        while (iterator.HasMoreResults && (!limit.HasValue || items.Count < limit.Value))
        {
            var response = await iterator.ReadNextAsync();
            foreach (var doc in response)
            {
                var id = doc.RootElement.GetProperty("id").GetString()!;
                var partitionKeyValue = GetPartitionKeyValue(doc.RootElement, _partitionKeyPath);
                items.Add((id, partitionKeyValue, doc));

                if (limit.HasValue && items.Count >= limit.Value)
                    break;
            }
        }

        return items;
    }

    public async Task<int> StreamDeleteItemsAsync(
        string query,
        Action<int> onProgress,
        CancellationToken cancellationToken = default)
    {
        var queryDefinition = new QueryDefinition(query);
        var iterator = _container.GetItemQueryIterator<JsonDocument>(queryDefinition);

        int totalDeleted = 0;
        const int batchSize = 100;
        var deleteTasks = new List<Task>();

        while (iterator.HasMoreResults)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var response = await iterator.ReadNextAsync(cancellationToken);

            foreach (var doc in response)
            {
                var id = doc.RootElement.GetProperty("id").GetString()!;
                var partitionKeyValue = GetPartitionKeyValue(doc.RootElement, _partitionKeyPath);

                deleteTasks.Add(DeleteItemWithRetryAsync(id, partitionKeyValue));

                // Process in batches to avoid overwhelming the service
                if (deleteTasks.Count >= batchSize)
                {
                    await Task.WhenAll(deleteTasks);
                    totalDeleted += deleteTasks.Count;
                    onProgress(totalDeleted);
                    deleteTasks.Clear();
                }
            }
        }

        // Process remaining items
        if (deleteTasks.Count > 0)
        {
            await Task.WhenAll(deleteTasks);
            totalDeleted += deleteTasks.Count;
            onProgress(totalDeleted);
        }

        return totalDeleted;
    }

    public async Task DeleteAllItemsInPartitionAsync(PartitionKey partitionKey)
    {
        await _container.DeleteAllItemsByPartitionKeyStreamAsync(partitionKey);
    }

    public async Task<int> CountItemsAsync(string query)
    {
        // Use COUNT query for efficiency
        var countQuery = query.Replace("SELECT *", "SELECT VALUE COUNT(1)", StringComparison.OrdinalIgnoreCase);

        // If the replacement didn't work, fall back to streaming count
        if (countQuery == query)
        {
            var queryDefinition = new QueryDefinition(query);
            var iterator = _container.GetItemQueryIterator<JsonDocument>(queryDefinition);

            int count = 0;
            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync();
                count += response.Count;
            }
            return count;
        }

        var countQueryDefinition = new QueryDefinition(countQuery);
        var countIterator = _container.GetItemQueryIterator<int>(countQueryDefinition);

        if (countIterator.HasMoreResults)
        {
            var response = await countIterator.ReadNextAsync();
            return response.FirstOrDefault();
        }

        return 0;
    }

    private async Task DeleteItemWithRetryAsync(string id, PartitionKey partitionKey)
    {
        int maxRetries = 3;
        int retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                await _container.DeleteItemAsync<JsonDocument>(id, partitionKey);
                return;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                    throw;

                await Task.Delay(ex.RetryAfter ?? TimeSpan.FromSeconds(1));
            }
        }
    }

    private PartitionKey GetPartitionKeyValue(JsonElement element, string partitionKeyPath)
    {
        var path = partitionKeyPath.TrimStart('/');
        var current = element;

        foreach (var segment in path.Split('/'))
        {
            if (current.TryGetProperty(segment, out var property))
            {
                current = property;
            }
            else
            {
                return PartitionKey.None;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => new PartitionKey(current.GetString()),
            JsonValueKind.Number => new PartitionKey(current.GetDouble()),
            JsonValueKind.True or JsonValueKind.False => new PartitionKey(current.GetBoolean()),
            JsonValueKind.Null => PartitionKey.Null,
            _ => PartitionKey.None
        };
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
