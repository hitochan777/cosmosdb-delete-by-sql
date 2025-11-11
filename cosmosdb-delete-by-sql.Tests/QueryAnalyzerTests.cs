using Xunit;

namespace CosmosDbDeleteBySql.Tests;

public class QueryAnalyzerTests
{
    [Theory]
    [InlineData("SELECT * FROM c WHERE c.tenantId = 'tenant-123'", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE c.tenantId = \"tenant-123\"", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE tenantId = 'tenant-123'", "/tenantId", "tenant-123")]
    [InlineData("SELECT c.id, c.name FROM c WHERE c.tenantId = 'tenant-123'", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE c.status = 'active' AND c.tenantId = 'tenant-123'", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE c.tenantId = 'tenant-123' AND c.status = 'active'", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE c['tenantId'] = 'tenant-123'", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE c[\"tenantId\"] = 'tenant-123'", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE   c.tenantId   =   'tenant-123'  ", "/tenantId", "tenant-123")]
    [InlineData("select * from c where c.tenantId = 'tenant-123'", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE c.userId = 'user@example.com'", "/userId", "user@example.com")]
    [InlineData("SELECT * FROM c WHERE c.partitionKey = 'value-with-dashes'", "/partitionKey", "value-with-dashes")]
    [InlineData("SELECT * FROM c WHERE c.pk = 'ABC123'", "/pk", "ABC123")]
    public void ExtractPartitionKeyValue_WithValidEqualityQuery_ReturnsPartitionKeyValue(
        string query,
        string partitionKeyPath,
        string expectedValue)
    {
        // Act
        var result = QueryAnalyzer.ExtractPartitionKeyValue(query, partitionKeyPath);

        // Assert
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData("SELECT * FROM c WHERE c.tenantId IN ('tenant-1', 'tenant-2')", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.tenantId > 'tenant-123'", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.tenantId < 'tenant-123'", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.tenantId >= 'tenant-123'", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.tenantId <= 'tenant-123'", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.tenantId != 'tenant-123'", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.tenantId <> 'tenant-123'", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.tenantId BETWEEN 'a' AND 'z'", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.tenantId = 'tenant-1' OR c.tenantId = 'tenant-2'", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.status = 'active' OR c.tenantId = 'tenant-123'", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.status = 'active'", "/tenantId")]
    [InlineData("SELECT * FROM c", "/tenantId")]
    [InlineData("SELECT * FROM c WHERE c.otherField = 'value'", "/tenantId")]
    public void ExtractPartitionKeyValue_WithInvalidQuery_ReturnsNull(
        string query,
        string partitionKeyPath)
    {
        // Act
        var result = QueryAnalyzer.ExtractPartitionKeyValue(query, partitionKeyPath);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("SELECT * FROM c WHERE c.tenantId = 'tenant-123'", "/tenantId", true)]
    [InlineData("SELECT * FROM c WHERE tenantId = 'tenant-123'", "/tenantId", true)]
    [InlineData("SELECT * FROM c WHERE c.tenantId IN ('tenant-1', 'tenant-2')", "/tenantId", false)]
    [InlineData("SELECT * FROM c WHERE c.tenantId > 'tenant-123'", "/tenantId", false)]
    [InlineData("SELECT * FROM c WHERE c.status = 'active'", "/tenantId", false)]
    [InlineData("SELECT * FROM c WHERE c.tenantId = 'tenant-1' OR c.tenantId = 'tenant-2'", "/tenantId", false)]
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
    [InlineData("SELECT * FROM c WHERE c.compoundKey = 'value'", "compoundKey")]
    [InlineData("SELECT * FROM c WHERE c.nested.partitionKey = 'value'", "nested")]
    public void ExtractPartitionKeyValue_WithDifferentPartitionKeyNames_WorksCorrectly(
        string query,
        string partitionKeyPath)
    {
        // Act
        var result = QueryAnalyzer.ExtractPartitionKeyValue(query, "/" + partitionKeyPath);

        // Assert
        Assert.Equal("value", result);
    }

    [Theory]
    [InlineData("SELECT * FROM c WHERE c.pk = 'has spaces'", "/pk", "has spaces")]
    [InlineData("SELECT * FROM c WHERE c.pk = 'has-special!@#chars'", "/pk", "has-special!@#chars")]
    [InlineData("SELECT * FROM c WHERE c.pk = ''", "/pk", "")]
    public void ExtractPartitionKeyValue_WithSpecialCharacters_ReturnsCorrectValue(
        string query,
        string partitionKeyPath,
        string expectedValue)
    {
        // Act
        var result = QueryAnalyzer.ExtractPartitionKeyValue(query, partitionKeyPath);

        // Assert
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData("SELECT * FROM c WHERE c.tenantId = 'tenant-123' AND c.status = 'active' AND c.type = 'premium'", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE c.status = 'active' AND c.type = 'premium' AND c.tenantId = 'tenant-123'", "/tenantId", "tenant-123")]
    public void ExtractPartitionKeyValue_WithMultipleAndConditions_ExtractsPartitionKey(
        string query,
        string partitionKeyPath,
        string expectedValue)
    {
        // Act
        var result = QueryAnalyzer.ExtractPartitionKeyValue(query, partitionKeyPath);

        // Assert
        Assert.Equal(expectedValue, result);
    }

    [Fact]
    public void ExtractPartitionKeyValue_WithLeadingSlashInPath_WorksCorrectly()
    {
        // Arrange
        var query = "SELECT * FROM c WHERE c.tenantId = 'tenant-123'";

        // Act
        var resultWithSlash = QueryAnalyzer.ExtractPartitionKeyValue(query, "/tenantId");
        var resultWithoutSlash = QueryAnalyzer.ExtractPartitionKeyValue(query, "tenantId");

        // Assert
        Assert.Equal("tenant-123", resultWithSlash);
        Assert.Equal("tenant-123", resultWithoutSlash);
    }

    [Theory]
    [InlineData("SELECT * FROM c WHERE c.tenantId='tenant-123'", "/tenantId", "tenant-123")]
    [InlineData("SELECT * FROM c WHERE c.tenantId  =  'tenant-123'", "/tenantId", "tenant-123")]
    public void ExtractPartitionKeyValue_WithVariousWhitespace_HandlesCorrectly(
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
