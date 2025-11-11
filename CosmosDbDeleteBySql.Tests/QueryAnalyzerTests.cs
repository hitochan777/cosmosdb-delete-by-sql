using Xunit;

namespace CosmosDbDeleteBySql.Tests;

public class QueryAnalyzerTests
{
    [Theory]
    [InlineData("SELECT * FROM c WHERE c.pk = '1'", "/pk", true)]
    [InlineData("SELECT * FROM c WHERE c.pk = \"1\"", "/pk", true)]
    [InlineData("SELECT * FROM c WHERE c.pk in ('1', '2')", "/pk", false)]
    [InlineData("SELECT * FROM c WHERE c.pk = '1' and c.id = '2'", "/pk", false)]
    public void CanUseDeleteAllItemsInPartition_ReturnsCorrectResult(
        string query,
        string partitionKeyPath,
        bool expectedResult)
    {
        // Act
        var result = QueryAnalyzer.CanUseDeleteAllItemsInPartition(query, partitionKeyPath);

        // Assert
        Assert.Equal(expectedResult, result);
    }

    [Theory]
    [InlineData("SELECT * FROM c WHERE c.pk = '1'", "pk", true)]
    public void CanUseDeleteAllItemsInPartition_WithNoLeadingSlashInPath_AlsoRecognizedAsValidPath(
        string query,
        string partitionKeyPath,
        bool expectedResult
    )
  {
      // Act
      var result = QueryAnalyzer.CanUseDeleteAllItemsInPartition(query, partitionKeyPath);

      // Assert
      Assert.Equal(expectedResult, result);
  }

    [Theory]
    [InlineData("SELECT * FROM c WHERE c.tenantId='tenant-123'", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE c.tenantId  =  'tenant-123'", "/tenantId", "tenant-123")]
    public void CanUseDeleteAllItemsInPartitionWithVariousWhitespace_HandlesCorrectly(
        string query,
        string partitionKeyPath,
        string expectedValue)
  {
        // Act
        var result = QueryAnalyzer.ExtractPartitionKeyValue(query, partitionKeyPath);

        // Assert
        Assert.Equal(expectedValue, result);
  }
}
