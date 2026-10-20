using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Eduscape.Tests.Api;

/// <summary>Throw-away PEM files in the same shape as the README's openssl command produces.</summary>
public sealed class TestCertificates : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("eduscape-dp-").FullName;

    /// <summary>A self-signed RSA certificate followed by its unencrypted private key.</summary>
    public string NewRsaPem()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = SelfSigned(new CertificateRequest("CN=eduscape-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return WriteFile(certificate.ExportCertificatePem() + "\n" + rsa.ExportPkcs8PrivateKeyPem());
    }

    /// <summary>A certificate without its private key, which cannot decrypt anything.</summary>
    public string NewPemWithoutPrivateKey()
    {
        using var rsa = RSA.Create(2048);
        using var certificate = SelfSigned(new CertificateRequest("CN=eduscape-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        return WriteFile(certificate.ExportCertificatePem());
    }

    /// <summary>An EC certificate: valid PEM, but XML encryption needs RSA.</summary>
    public string NewEcPem()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var certificate = SelfSigned(new CertificateRequest("CN=eduscape-test", ec, HashAlgorithmName.SHA256));
        return WriteFile(certificate.ExportCertificatePem() + "\n" + ec.ExportPkcs8PrivateKeyPem());
    }

    public string MissingPath() => Path.Combine(_directory, "missing.pem");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static X509Certificate2 SelfSigned(CertificateRequest request) =>
        request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));

    private string WriteFile(string contents)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, contents);
        return path;
    }
}
