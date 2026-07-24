using System;
using Kerberos.NET.Client;
using Kerberos.NET.Credentials;
using Kerberos.NET.Entities;
using SMBLibrary.Client.Authentication;

namespace Frends.Smb.MoveFiles.Helpers;

internal sealed class KerberosNetAuthenticationClient : IAuthenticationClient, IDisposable
{
    private readonly KerberosClient kerberosClient;
    private readonly KerberosPasswordCredential credential;
    private readonly string spn;
    private byte[] sessionKey;

    internal KerberosNetAuthenticationClient(
        string domain,
        string username,
        string password,
        string server,
        string kdcAddress = null)
    {
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
        kerberosClient.Authenticate(credential).Wait();

        KrbApReq ticket = kerberosClient.GetServiceTicket(spn).GetAwaiter().GetResult();
        var cachedItem = (KerberosClientCacheEntry)kerberosClient.Cache.GetCacheItem(spn);
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