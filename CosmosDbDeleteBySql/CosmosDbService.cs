using Azure.Identity;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

public class CosmosDbService : IDisposable
{
    private CosmosClient _client { get; set ; }
    private Container _container { get;  set; }
    public string PartitionKeyPath { get; set; }

    private CosmosDbService(CosmosClient client, Container container, string partitionKeyPath)
    {
        _client = client;
        _container = container;
        PartitionKeyPath = partitionKeyPath;
    }

    public static async Task<CosmosDbService> CreateAsync(string endpoint, string database, string containerName)
    {
        var credential = new DefaultAzureCredential();
        var client = new CosmosClient(endpoint, credential);
        var container = client.GetContainer(database, containerName);
        var containerProperties = await container.ReadContainerAsync();
        var partitionKeyPath = containerProperties.Resource.PartitionKeyPath;
        return new CosmosDbService(client, container, partitionKeyPath);
    }

    public async Task<List<JObject>> FetchItemsAsync(string query, int? limit = null)
    {
        var queryDefinition = new QueryDefinition(query);
        var iterator = _container.GetItemQueryIterator<JObject>(queryDefinition);

        var items = new List<JObject>();

        while (iterator.HasMoreResults && (!limit.HasValue || items.Count < limit.Value))
        {
            var response = await iterator.ReadNextAsync();
            foreach (var doc in response)
            {
                items.Add(doc);

                if (limit.HasValue && items.Count >= limit.Value)
                    break;
            }
        }

        return items;
    }

    public async Task<int> DeleteItemsAsync(
        string query,
        Action<int> onProgress,
        CancellationToken cancellationToken = default)
    {
        var queryDefinition = new QueryDefinition(query);
        var iterator = _container.GetItemQueryIterator<JObject>(queryDefinition);

        int totalDeleted = 0;
        const int batchSize = 100;
        var deleteTasks = new List<Task>();

        while (iterator.HasMoreResults)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            var response = await iterator.ReadNextAsync(cancellationToken);

            foreach (var doc in response)
            {
                var id = doc["id"]!.ToString();
                var partitionKeyValue = GetPartitionKeyValue(doc, PartitionKeyPath);

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

    public async Task DeleteAllItemsByPartitionKeyAsync(PartitionKey partitionKey)
    {
        // TODO: Not implemented
        // Note: This API may not be available in all SDK versions or requires specific Cosmos DB account configuration
        // Attempting to call the bulk delete API if available
        try
        {
            throw new NotImplementedException();
        }
        catch
        {
            throw new NotSupportedException("DeleteAllItemsInPartitionKey is not supported on this account or SDK version");
        }
    }

    public async Task<int> CountItemsAsync(string query)
    {
        // Use COUNT query for efficiency
        var countQuery = query.Replace("SELECT *", "SELECT VALUE COUNT(1)", StringComparison.OrdinalIgnoreCase);

        // If the replacement didn't work, fall back to streaming count
        if (countQuery == query)
        {
            var queryDefinition = new QueryDefinition(query);
            var iterator = _container.GetItemQueryIterator<JObject>(queryDefinition);

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
                await _container.DeleteItemAsync<JObject>(id, partitionKey);
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

    private static PartitionKey GetPartitionKeyValue(JObject document, string partitionKeyPath)
    {
        var path = partitionKeyPath.TrimStart('/');
        JToken? current = document;

        foreach (var segment in path.Split('/'))
        {
            current = current?[segment];
            if (current == null)
            {
                return PartitionKey.None;
            }
        }

        return current.Type switch
        {
            JTokenType.String => new PartitionKey(current.Value<string>()),
            JTokenType.Integer => new PartitionKey(current.Value<long>()),
            JTokenType.Float => new PartitionKey(current.Value<double>()),
            JTokenType.Boolean => new PartitionKey(current.Value<bool>()),
            JTokenType.Null => PartitionKey.Null,
            _ => PartitionKey.None
        };
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
