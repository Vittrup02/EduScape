using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace Eduscape.Api.Auth;

/// <summary>
/// Where the certificate that encrypts the Data Protection keys at rest comes from (<c>DataProtection:*</c>;
/// on Render <c>DataProtection__CertificatePath=/etc/secrets/dataprotection.pem</c>, a secret file).
/// </summary>
public sealed class KeyEncryptionOptions
{
    public const string SectionName = "DataProtection";

    public const string CertificatePathSetting = SectionName + ":" + nameof(CertificatePath);
    public const string PreviousCertificatePathsSetting = SectionName + ":" + nameof(PreviousCertificatePaths);

    /// <summary>PEM file with the certificate and its unencrypted RSA private key. New keys are encrypted with it.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    /// Comma-separated PEM files of earlier certificates. They only decrypt keys written before a rotation.
    /// </summary>
    public string? PreviousCertificatePaths { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(CertificatePath);

    public IEnumerable<string> PreviousPaths =>
        (PreviousCertificatePaths ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public static class KeyEncryption
{
    public const string MissingCertificateMessage =
        "The Data Protection keys in the database must be encrypted outside Development, but no certificate is "
        + "configured. Set DataProtection__CertificatePath to a PEM file with the certificate and its RSA private key "
        + "(on Render: the secret file /etc/secrets/dataprotection.pem). The README explains how to create and install it.";

    /// <summary>
    /// Encrypts the key ring with the configured certificate. Without one the keys stay unencrypted, which is only
    /// allowed in Development; elsewhere the host refuses to start. The check runs when the host starts, not here,
    /// because EF's migration bundle builds this host in Production (without the certificate) but never starts it.
    /// </summary>
    public static IDataProtectionBuilder ProtectKeysWithConfiguredCertificate(
        this IDataProtectionBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var section = configuration.GetSection(KeyEncryptionOptions.SectionName);
        builder.Services.AddOptions<KeyEncryptionOptions>()
            .Bind(section)
            .Validate(options => options.IsConfigured || environment.IsDevelopment(), MissingCertificateMessage)
            .ValidateOnStart();

        var options = section.Get<KeyEncryptionOptions>() ?? new KeyEncryptionOptions();
        if (!options.IsConfigured)
        {
            return builder;
        }

        // Reading two small files; the key ring itself is still loaded on first use.
        builder.ProtectKeysWithCertificate(LoadCertificate(options.CertificatePath!));
        var previous = options.PreviousPaths.Select(LoadCertificate).ToArray();
        if (previous.Length > 0)
        {
            builder.UnprotectKeysWithAnyCertificate(previous);
        }

        builder.Services.AddScoped<PlaintextKeyEncryption>();
        builder.Services.AddHostedService<PlaintextKeyEncryptionService>();
        return builder;
    }

    /// <summary>Loads a PEM file with an RSA certificate and its private key, with an error that says what is wrong.</summary>
    public static X509Certificate2 LoadCertificate(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"The Data Protection certificate file '{path}' does not exist.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509Certificate2.CreateFromPemFile(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                $"The Data Protection certificate file '{path}' cannot be read by the app's user. "
                + "Render's secret files are readable by group 1000 (see the Dockerfile).", ex);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"The Data Protection certificate file '{path}' must hold a PEM certificate followed by its unencrypted "
                + "private key (BEGIN CERTIFICATE and BEGIN PRIVATE KEY).", ex);
        }

        using var privateKey = certificate.GetRSAPrivateKey();
        if (privateKey is null)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                $"The Data Protection certificate in '{path}' must use an RSA key; XML encryption does not support other key types.");
        }

        return certificate;
    }
}
