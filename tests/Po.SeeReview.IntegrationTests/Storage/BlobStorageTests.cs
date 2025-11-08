using Po.SeeReview.IntegrationTests.TestFixtures;
using Azure.Storage.Blobs;
using Xunit;

namespace Po.SeeReview.IntegrationTests.Storage;

public class BlobStorageTests : IClassFixture<AzuriteFixture>, IDisposable
{
    private readonly AzuriteFixture _fixture;
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;
    private readonly string _testComicId;

    public BlobStorageTests(AzuriteFixture fixture)
    {
        _fixture = fixture;
        _fixture.EnsureAzuriteAvailable();

        // Use Azurite connection string for local testing
        _blobServiceClient = new BlobServiceClient(_fixture.ConnectionString);
        _containerClient = _blobServiceClient.GetBlobContainerClient("comics");
        _testComicId = $"test-comic-{Guid.NewGuid()}";
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task UploadComicImage_ValidImage_UploadsSuccessfully()
    {
        _fixture.EnsureAzuriteAvailable();
        // Arrange
        await _containerClient.CreateIfNotExistsAsync();
        var imageBytes = GenerateTestImageBytes();
        var blobName = $"{_testComicId}.png";
        var blobClient = _containerClient.GetBlobClient(blobName);

        // Act
        var uploadOptions = new Azure.Storage.Blobs.Models.BlobUploadOptions
        {
            HttpHeaders = new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "image/png"
            }
        };
        await blobClient.UploadAsync(new BinaryData(imageBytes), uploadOptions);

        // Assert
        var exists = await blobClient.ExistsAsync();
        Assert.True(exists.Value);

        var properties = await blobClient.GetPropertiesAsync();
        Assert.Equal("image/png", properties.Value.ContentType);
        Assert.Equal(imageBytes.Length, properties.Value.ContentLength);
    }

    [Fact]
    public async Task DownloadComicImage_ExistingBlob_DownloadsSuccessfully()
    {
        _fixture.EnsureAzuriteAvailable();
        // Arrange
        await _containerClient.CreateIfNotExistsAsync();
        var imageBytes = GenerateTestImageBytes();
        var blobName = $"{_testComicId}.png";
        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(new BinaryData(imageBytes), overwrite: true);

        // Act
        var downloadResponse = await blobClient.DownloadContentAsync();
        var downloadedBytes = downloadResponse.Value.Content.ToArray();

        // Assert
        Assert.Equal(imageBytes.Length, downloadedBytes.Length);
        Assert.Equal(imageBytes, downloadedBytes);
    }

    [Fact]
    public async Task DeleteComicImage_ExistingBlob_DeletesSuccessfully()
    {
        _fixture.EnsureAzuriteAvailable();
        // Arrange
        await _containerClient.CreateIfNotExistsAsync();
        var imageBytes = GenerateTestImageBytes();
        var blobName = $"{_testComicId}.png";
        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(new BinaryData(imageBytes), overwrite: true);

        // Act
        await blobClient.DeleteAsync();

        // Assert
        var exists = await blobClient.ExistsAsync();
        Assert.False(exists.Value);
    }

    [Fact]
    public async Task GetBlobUrl_ExistingBlob_ReturnsValidUrl()
    {
        _fixture.EnsureAzuriteAvailable();
        // Arrange
        await _containerClient.CreateIfNotExistsAsync();
        var imageBytes = GenerateTestImageBytes();
        var blobName = $"{_testComicId}.png";
        var blobClient = _containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(new BinaryData(imageBytes), overwrite: true);

        // Act
        var blobUrl = blobClient.Uri.ToString();

        // Assert
        Assert.NotNull(blobUrl);
        Assert.Contains("comics", blobUrl);
        Assert.Contains($"{_testComicId}.png", blobUrl);
    }

    [Fact]
    public async Task UploadComicImage_OverwriteExisting_ReplacesBlob()
    {
        _fixture.EnsureAzuriteAvailable();
        // Arrange
        await _containerClient.CreateIfNotExistsAsync();
        var originalBytes = GenerateTestImageBytes();
        var newBytes = GenerateTestImageBytes(size: 2048);
        var blobName = $"{_testComicId}.png";
        var blobClient = _containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(new BinaryData(originalBytes), overwrite: true);

        // Act
        await blobClient.UploadAsync(new BinaryData(newBytes), overwrite: true);

        // Assert
        var properties = await blobClient.GetPropertiesAsync();
        Assert.Equal(newBytes.Length, properties.Value.ContentLength);
    }

    [Fact]
    public async Task ListBlobs_ContainerWithBlobs_ReturnsAllBlobs()
    {
        _fixture.EnsureAzuriteAvailable();
        // Arrange
        await _containerClient.CreateIfNotExistsAsync();
        var testBlobs = new List<string>
        {
            $"test-list-{Guid.NewGuid()}.png",
            $"test-list-{Guid.NewGuid()}.png",
            $"test-list-{Guid.NewGuid()}.png"
        };

        foreach (var blobName in testBlobs)
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            await blobClient.UploadAsync(new BinaryData(GenerateTestImageBytes()), overwrite: true);
        }

        // Act
        var blobs = new List<string>();
        await foreach (var blobItem in _containerClient.GetBlobsAsync())
        {
            if (blobItem.Name.StartsWith("test-list-"))
            {
                blobs.Add(blobItem.Name);
            }
        }

        // Assert
        Assert.True(blobs.Count >= testBlobs.Count);

        // Cleanup
        foreach (var blobName in testBlobs)
        {
            await _containerClient.GetBlobClient(blobName).DeleteIfExistsAsync();
        }
    }

    public void Dispose()
    {
        // Cleanup test blobs
        try
        {
            var blobClient = _containerClient.GetBlobClient($"{_testComicId}.png");
            blobClient.DeleteIfExists();
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private static byte[] GenerateTestImageBytes(int size = 1024)
    {
        // Generate a simple test image (PNG header + random data)
        var bytes = new byte[size];
        // PNG signature
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4E;
        bytes[3] = 0x47;
        bytes[4] = 0x0D;
        bytes[5] = 0x0A;
        bytes[6] = 0x1A;
        bytes[7] = 0x0A;

        // Fill rest with random data
        var random = new Random();
        random.NextBytes(bytes.AsSpan(8));

        return bytes;
    }
}
