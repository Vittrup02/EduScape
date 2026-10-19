using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Eduscape.Infrastructure.Storage;

/// <summary>
/// Cloudflare R2 through its S3-compatible API. The client is created on first use, so startup stays fast; presigning
/// happens locally without a network call.
/// </summary>
internal sealed class R2ImageStorage(IOptions<R2Options> options) : IImageStorage, IDisposable
{
    private readonly R2Options _options = options.Value;
    private readonly Lazy<AmazonS3Client> _client = new(() => CreateClient(options.Value));

    public bool IsConfigured => true;

    public string CreateUploadUrl(string key, string contentType, TimeSpan expiresIn) =>
        _client.Value.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.Add(expiresIn),
        });

    public string CreateReadUrl(string key, TimeSpan expiresIn) =>
        _client.Value.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiresIn),
        });

    public async Task<ReadResult> ReadAsync(string key, long maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            var metadata = await _client.Value.GetObjectMetadataAsync(_options.Bucket, key, cancellationToken);
            if (metadata.ContentLength > maxBytes)
            {
                return ReadResult.TooLarge;
            }

            using var response = await _client.Value.GetObjectAsync(_options.Bucket, key, cancellationToken);
            using var buffer = new MemoryStream((int)metadata.ContentLength);
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);
            return buffer.Length > maxBytes ? ReadResult.TooLarge : new ReadResult(ReadOutcome.Found, buffer.ToArray());
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return ReadResult.NotFound;
        }
    }

    public async Task WriteAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        await _client.Value.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _options.Bucket,
                Key = key,
                InputStream = stream,
                ContentType = contentType,
                DisablePayloadSigning = true,
            },
            cancellationToken);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
        _client.Value.DeleteObjectAsync(_options.Bucket, key, cancellationToken);

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    private static AmazonS3Client CreateClient(R2Options options) =>
        new(
            new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey),
            new AmazonS3Config
            {
                ServiceURL = options.Endpoint,
                AuthenticationRegion = "auto",
                ForcePathStyle = true,

                // R2 does not support the checksums newer AWS SDKs add to every request by default.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
}
