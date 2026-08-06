using System;
using Kerberos.NET.Client;
using Kerberos.NET.Credentials;
using Kerberos.NET.Entities;
using SMBLibrary.Client.Authentication;

namespace Frends.Smb.DeleteFiles.Helpers;

internal sealed class KerberosNetAuthenticationClient : IAuthenticationClient, IDisposable
{
    private readonly KerberosClient kerberosClient;
    private readonly KerberosPasswordCredential credential;
    private readonly string spn;
    private byte[] sessionKey;
    private bool authenticated;

    internal KerberosNetAuthenticationClient(
        string domain,
        string username,
        string password,
        string server,
        string kdcAddress = null)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("Kerberos authentication requires the username in 'DOMAIN\\user' form (realm cannot be empty).", nameof(domain));
        if (string.IsNullOrWhiteSpace(server))
            throw new ArgumentException("Kerberos authentication requires a server name for the CIFS SPN.", nameof(server));

        kerberosClient = new KerberosClient();

        if (!string.IsNullOrEmpty(kdcAddress))
            kerberosClient.PinKdc(domain, kdcAddress);

        credential = new KerberosPasswordCredential(username, password, domain);
        spn = $"cifs/{server}";
    }

    /// <summary>
    /// Initializes the security context for Kerberos authentication and returns the initial token to be sent to the server.
    /// </summary>
    /// <param name="inputToken">The input token from the server, if any.</param>
    /// <returns>The initial token to be sent to the server.</returns>
    public byte[] InitializeSecurityContext(byte[] inputToken)
    {
        if (!authenticated)
        {
            kerberosClient.Authenticate(credential).GetAwaiter().GetResult();
            authenticated = true;
        }

        KrbApReq ticket = kerberosClient.GetServiceTicket(spn).GetAwaiter().GetResult();

        if (kerberosClient.Cache.GetCacheItem(spn) is not KerberosClientCacheEntry cachedItem)
        {
            throw new InvalidOperationException($"Cache entry for SPN '{spn}' was not found or the entry is of an invalid type.");
        }

        sessionKey = cachedItem.SessionKey.KeyValue.ToArray();
        return ticket.EncodeGssApi().ToArray();
    }

    /// <summary>
    /// Gets the session key for the Kerberos authentication.
    /// </summary>
    /// <returns>The session key as a byte array.</returns>
    public byte[] GetSessionKey() => sessionKey;

    /// <summary>
    /// Disposes the Kerberos client and releases any resources.
    /// </summary>
    public void Dispose()
    {
        kerberosClient.Dispose();
    }
}