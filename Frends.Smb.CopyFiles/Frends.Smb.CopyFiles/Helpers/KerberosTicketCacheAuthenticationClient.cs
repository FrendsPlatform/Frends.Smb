using System;
using Kerberos.NET.Client;
using SMBLibrary.Client.Authentication;

namespace Frends.Smb.CopyFiles.Helpers;

internal sealed class KerberosTicketCacheAuthenticationClient : IAuthenticationClient, IDisposable
{
    private readonly KerberosClient kerberosClient;
    private readonly string spn;
    private byte[] sessionKey = Array.Empty<byte>();

    internal KerberosTicketCacheAuthenticationClient(
        string krbCacheFile,
        string krbDomain,
        string server,
        string kdcAddress = null)
    {
        kerberosClient = new KerberosClient
        {
            Cache = new Krb5TicketCache(krbCacheFile),
            CacheInMemory = false,
        };

        if (!string.IsNullOrEmpty(krbDomain))
            kerberosClient.PinKdc(krbDomain, kdcAddress ?? krbDomain);

        spn = $"cifs/{server}";
    }

    /// <summary>
    /// Initializes the security context for Kerberos authentication and returns the initial token to be sent to the server.
    /// </summary>
    /// <param name="inputToken">The input token from the server, if any.</param>
    /// <returns>The initial token to be sent to the server.</returns>
    public byte[] InitializeSecurityContext(byte[] inputToken)
    {
        var ticket = kerberosClient.GetServiceTicket(spn).GetAwaiter().GetResult();
        if (kerberosClient.Cache.GetCacheItem(spn) is not KerberosClientCacheEntry cachedItem)
        {
            throw new InvalidOperationException(
                $"Cache entry for SPN '{spn}' was not found or the entry is of an invalid type.");
        }

        sessionKey = cachedItem.SessionKey.KeyValue.ToArray();
        return ticket.EncodeGssApi().ToArray();
    }

    /// <summary>
    /// Gets the session key for the Kerberos authentication.
    /// </summary>
    /// <returns>The session key as a byte array.</returns>
    public byte[] GetSessionKey()
    {
        if (sessionKey.Length < 16)
            throw new InvalidOperationException("Session key is not available or is shorter than the required 16 bytes. Ensure InitializeSecurityContext has been called and completed successfully.");

        byte[] signingSessionKey = new byte[16];
        Array.Copy(sessionKey, signingSessionKey, 16);
        return signingSessionKey;
    }

    /// <summary>
    /// Disposes the Kerberos client and releases any resources.
    /// </summary>
    public void Dispose()
    {
        kerberosClient.Dispose();
    }
}
