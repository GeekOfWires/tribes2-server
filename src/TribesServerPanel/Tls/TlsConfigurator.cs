using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LettuceEncrypt;

namespace TribesServerPanel.Tls;

/// <summary>
/// Configures Kestrel endpoints + TLS from environment variables:
///   SELF_SIGNED_CERT=1  -> generate (and persist) a self-signed cert from SELF_SIGNED_*.
///   LETS_ENCRYPT_CERT=1 -> provision via ACME (LettuceEncrypt) from LETS_ENCRYPT_*.
///   neither             -> HTTP only (terminate TLS at an external proxy).
/// </summary>
public static class TlsConfigurator
{
    public static void Configure(WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var httpPort = cfg.GetValue("HTTP_PORT", 8080);
        var httpsPort = cfg.GetValue("HTTPS_PORT", 8443);

        var selfSigned = cfg.GetValue("SELF_SIGNED_CERT", 0) == 1;
        var letsEncrypt = cfg.GetValue("LETS_ENCRYPT_CERT", 0) == 1;

        // ASPNETCORE_URLS would conflict with explicit Listen calls; clear it.
        builder.WebHost.UseSetting("urls", null);

        if (letsEncrypt)
        {
            var le = builder.Services.AddLettuceEncrypt(o =>
            {
                o.AcceptTermsOfService = true;
                o.EmailAddress = cfg["LETS_ENCRYPT_EMAIL"] ?? "";
                o.DomainNames = (cfg["LETS_ENCRYPT_DOMAINS"] ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                o.UseStagingServer = cfg.GetValue("LETS_ENCRYPT_STAGING", 0) == 1;
            });
            var dir = cfg["LETS_ENCRYPT_CERT_DIR"] ?? "/data/letsencrypt";
            Directory.CreateDirectory(dir);
            le.PersistDataToDirectory(new DirectoryInfo(dir), cfg["LETS_ENCRYPT_PFX_PASSWORD"] ?? "");
        }

        X509Certificate2? ssCert = selfSigned ? GetOrCreateSelfSigned(cfg) : null;

        builder.WebHost.ConfigureKestrel(k =>
        {
            // HTTP: always on (serves the panel directly, and ACME HTTP-01 needs it).
            k.ListenAnyIP(httpPort);

            if (letsEncrypt)
                k.ListenAnyIP(httpsPort, lo => lo.UseHttps(h => { })); // LettuceEncrypt supplies the cert
            else if (ssCert is not null)
                k.ListenAnyIP(httpsPort, lo => lo.UseHttps(ssCert));
        });
    }

    private static X509Certificate2 GetOrCreateSelfSigned(IConfiguration cfg)
    {
        var path = cfg["SELF_SIGNED_PATH"] ?? "/data/self-signed.pfx";
        var pass = cfg["SELF_SIGNED_PASSWORD"] ?? "";

        if (File.Exists(path))
        {
            try { return X509CertificateLoader.LoadPkcs12FromFile(path, pass, X509KeyStorageFlags.Exportable); }
            catch { /* regenerate below */ }
        }

        var subject = cfg["SELF_SIGNED_SUBJECT"] ?? $"CN={cfg["SELF_SIGNED_CN"] ?? "tribes2-panel"}";
        var days = cfg.GetValue("SELF_SIGNED_DAYS", 365);

        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false)); // server auth

        var san = new SubjectAlternativeNameBuilder();
        foreach (var d in Split(cfg["SELF_SIGNED_DNS"])) san.AddDnsName(d);
        foreach (var ip in Split(cfg["SELF_SIGNED_IP"]))
            if (IPAddress.TryParse(ip, out var addr)) san.AddIpAddress(addr);
        req.CertificateExtensions.Add(san.Build());

        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(days));

        try
        {
            var pfx = cert.Export(X509ContentType.Pfx, pass);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, pfx);
        }
        catch { /* non-fatal: use the in-memory cert */ }

        // Reload so the private key is bound for Kestrel.
        return X509CertificateLoader.LoadPkcs12(cert.Export(X509ContentType.Pfx, pass), pass, X509KeyStorageFlags.Exportable);
    }

    private static string[] Split(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
