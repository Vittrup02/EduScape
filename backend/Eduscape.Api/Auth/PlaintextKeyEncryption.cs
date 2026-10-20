using System.Xml.Linq;
using Eduscape.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Eduscape.Api.Auth;

/// <summary>
/// Encrypts Data Protection keys that were stored before the certificate was configured. Data Protection never
/// rewrites a stored key, so without this the key that was active at the switch would stay readable in the database
/// until it expires (90 days). The key material is unchanged, so tokens it protected stay valid: no one is signed out.
/// </summary>
public sealed class PlaintextKeyEncryption(EduscapeDbContext db, IOptions<KeyManagementOptions> options)
{
    // The format Data Protection itself writes when an IXmlEncryptor is configured (XmlEncryptionExtensions).
    private static readonly XNamespace DataProtectionNamespace = "http://schemas.asp.net/2015/03/dataProtection";
    private static readonly XName RequiresEncryption = DataProtectionNamespace + "requiresEncryption";
    private static readonly XName EncryptedSecret = DataProtectionNamespace + "encryptedSecret";

    /// <summary>Encrypts every unencrypted key. Returns the number of keys encrypted.</summary>
    public async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        var encryptor = options.Value.XmlEncryptor
            ?? throw new InvalidOperationException("No Data Protection certificate is configured.");

        var keys = await db.DataProtectionKeys.ToListAsync(cancellationToken);
        var encrypted = 0;
        foreach (var key in keys)
        {
            if (key.Xml is null)
            {
                continue;
            }

            var root = XElement.Parse(key.Xml);
            var secrets = root.DescendantsAndSelf().Where(e => (bool?)e.Attribute(RequiresEncryption) == true).ToList();
            if (secrets.Count == 0)
            {
                continue;
            }

            foreach (var secret in secrets)
            {
                var info = encryptor.Encrypt(new XElement(secret));
                secret.ReplaceWith(new XElement(
                    EncryptedSecret,
                    new XAttribute("decryptorType", info.DecryptorType.AssemblyQualifiedName!),
                    info.EncryptedElement));
            }

            // "Warning: the key below is in an unencrypted form."
            root.DescendantNodes().OfType<XComment>().Where(c => c.Value.Contains("unencrypted")).Remove();

            key.Xml = root.ToString(SaveOptions.DisableFormatting);
            encrypted++;
        }

        await db.SaveChangesAsync(cancellationToken);
        return encrypted;
    }
}

/// <summary>Runs <see cref="PlaintextKeyEncryption"/> once in the background after startup, so startup stays light.</summary>
public sealed class PlaintextKeyEncryptionService(
    IServiceScopeFactory scopes,
    ILogger<PlaintextKeyEncryptionService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let startup finish before touching the database.
        await Task.Yield();
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var encrypted = await scope.ServiceProvider.GetRequiredService<PlaintextKeyEncryption>().RunOnceAsync(stoppingToken);
            if (encrypted > 0)
            {
                logger.LogInformation("Encrypted {Count} Data Protection keys that were stored unencrypted", encrypted);
            }
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            // E.g. the database is briefly unreachable; the next start tries again. The keys still work meanwhile.
            logger.LogWarning(ex, "Encrypting unencrypted Data Protection keys failed");
        }
    }
}
