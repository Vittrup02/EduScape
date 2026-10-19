namespace Eduscape.Infrastructure.Storage;

/// <summary>
/// Settings from the <c>R2</c> configuration section (<c>R2__AccountId</c> etc. as environment variables, user-secrets
/// locally). The bucket must be created with the EU jurisdiction, so it is reached through the EU endpoint.
/// </summary>
public sealed class R2Options
{
    public const string SectionName = "R2";

    public string? AccountId { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string? Bucket { get; set; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(AccountId)
        && !string.IsNullOrWhiteSpace(AccessKeyId)
        && !string.IsNullOrWhiteSpace(SecretAccessKey)
        && !string.IsNullOrWhiteSpace(Bucket);

    /// <summary>The S3 endpoint of an EU-jurisdiction bucket; also the origin the frontend's CSP must allow.</summary>
    public string Endpoint => $"https://{AccountId}.eu.r2.cloudflarestorage.com";
}
