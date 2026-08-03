---
title: TLS
nav_order: 11
---

# TLS

The panel always serves **HTTP** on `HTTP_PORT` (8080). HTTPS on `HTTPS_PORT` (8443) is bound
**only** when you enable one of the two modes below. With neither, terminate TLS at your own
reverse proxy. See [`Tls/TlsConfigurator.cs`](https://github.com/GeekOfWires/tribes2-server/blob/main/src/TribesServerPanel/Tls/TlsConfigurator.cs).

## Mode 1 — self-signed

Set `SELF_SIGNED_CERT=1` and describe the cert. It's generated on first boot and **persisted**
to `SELF_SIGNED_PATH` (default `/data/self-signed.pfx`) so it's stable across restarts.

```env
SELF_SIGNED_CERT=1
SELF_SIGNED_SUBJECT=CN=tribes2.example.com      # or use SELF_SIGNED_CN
SELF_SIGNED_DNS=tribes2.example.com,www.example.com
SELF_SIGNED_IP=203.0.113.10
SELF_SIGNED_DAYS=365
SELF_SIGNED_PASSWORD=optional-pfx-password
```

DNS and IP **SANs** are honored, so you can issue a cert valid for a bare IP. Browsers will warn
(it's self-signed) — fine for a private/admin panel.

## Mode 2 — Let's Encrypt (ACME)

Set `LETS_ENCRYPT_CERT=1`. Certificates are obtained and renewed automatically via
**LettuceEncrypt** and persisted under `LETS_ENCRYPT_CERT_DIR` (default `/data/letsencrypt`).

```env
LETS_ENCRYPT_CERT=1
LETS_ENCRYPT_EMAIL=you@example.com
LETS_ENCRYPT_DOMAINS=tribes2.example.com
LETS_ENCRYPT_STAGING=0          # 1 while testing to avoid rate limits
LETS_ENCRYPT_PFX_PASSWORD=optional
```

Requirements:
- The domain(s) must resolve to this host and the **HTTP port must be reachable from the
  internet** — ACME validates via HTTP-01 over the always-on HTTP listener.
- Use `LETS_ENCRYPT_STAGING=1` while testing; switch to `0` for trusted certs.

## Notes

- If both modes are set, **Let's Encrypt wins** for the HTTPS listener.
- Persist **`/data`** so certs/keys (and the Data-Protection keys that sign auth cookies) survive
  restarts.
- Full variable list: [Configuration → TLS](configuration.md#tls).

## See also
- [Configuration reference](configuration.md) · [Building & deploying](building-and-deploying.md)
- Back to [docs index](README.md)
