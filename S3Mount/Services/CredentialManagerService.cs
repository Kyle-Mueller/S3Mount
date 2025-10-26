using System.Runtime.InteropServices;
using System.Text;

namespace S3Mount.Services;

/// <summary>
/// Service for storing and retrieving credentials from Windows Credential Manager
/// </summary>
public class CredentialManagerService
{
    private readonly LogService _log = LogService.Instance;
    private const string CredentialPrefix = "S3Mount_";

    /// <summary>
    /// Store credentials in Windows Credential Manager
    /// </summary>
    public bool StoreCredentials(string remoteName, string accessKey, string secretKey)
    {
        try
        {
            var targetName = $"{CredentialPrefix}{remoteName}";
            
            _log.Debug($"?? Storing credentials for: {remoteName}");

            var credentialData = $"{accessKey}|{secretKey}";
            var credentialBytes = Encoding.Unicode.GetBytes(credentialData);
            
            // Allocate memory for the credential blob
            IntPtr credentialBlob = Marshal.AllocHGlobal(credentialBytes.Length);
            Marshal.Copy(credentialBytes, 0, credentialBlob, credentialBytes.Length);

            // Store as Generic Windows Credential
            var credential = new NativeMethods.CREDENTIAL
            {
                Type = NativeMethods.CRED_TYPE_GENERIC,
                TargetName = targetName,
                CredentialBlob = credentialBlob,
                CredentialBlobSize = credentialBytes.Length,
                Persist = NativeMethods.CRED_PERSIST_LOCAL_MACHINE,
                AttributeCount = 0,
                UserName = accessKey
            };

            bool result = NativeMethods.CredWrite(ref credential, 0);

            // Free allocated memory
            Marshal.FreeHGlobal(credentialBlob);

            if (result)
            {
                _log.Success($"? Credentials stored for: {remoteName}");
            }
            else
            {
                _log.Error($"? Failed to store credentials for: {remoteName}");
            }

            return result;
        }
        catch (Exception ex)
        {
            _log.Error($"? Error storing credentials: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Retrieve credentials from Windows Credential Manager
    /// </summary>
    public (string? accessKey, string? secretKey) RetrieveCredentials(string remoteName)
    {
        try
        {
            var targetName = $"{CredentialPrefix}{remoteName}";
            
            _log.Debug($"?? Retrieving credentials for: {remoteName}");

            bool result = NativeMethods.CredRead(targetName, NativeMethods.CRED_TYPE_GENERIC, 0, out IntPtr credPtr);

            if (!result)
            {
                _log.Warning($"?? No credentials found for: {remoteName}");
                return (null, null);
            }

            try
            {
                var cred = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credPtr);
                
                if (cred.CredentialBlob == IntPtr.Zero)
                {
                    return (null, null);
                }

                byte[] credentialBlob = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, credentialBlob, 0, cred.CredentialBlobSize);

                string credentialString = Encoding.Unicode.GetString(credentialBlob);
                var parts = credentialString.Split('|');

                if (parts.Length == 2)
                {
                    _log.Success($"? Credentials retrieved for: {remoteName}");
                    return (parts[0], parts[1]);
                }

                return (null, null);
            }
            finally
            {
                NativeMethods.CredFree(credPtr);
            }
        }
        catch (Exception ex)
        {
            _log.Error($"? Error retrieving credentials: {ex.Message}");
            return (null, null);
        }
    }

    /// <summary>
    /// Delete credentials from Windows Credential Manager
    /// </summary>
    public bool DeleteCredentials(string remoteName)
    {
        try
        {
            var targetName = $"{CredentialPrefix}{remoteName}";
            
            _log.Debug($"??? Deleting credentials for: {remoteName}");

            bool result = NativeMethods.CredDelete(targetName, NativeMethods.CRED_TYPE_GENERIC, 0);

            if (result)
            {
                _log.Success($"? Credentials deleted for: {remoteName}");
            }
            else
            {
                _log.Warning($"?? No credentials to delete for: {remoteName}");
            }

            return result;
        }
        catch (Exception ex)
        {
            _log.Error($"? Error deleting credentials: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Check if credentials exist for a remote
    /// </summary>
    public bool CredentialsExist(string remoteName)
    {
        var (accessKey, secretKey) = RetrieveCredentials(remoteName);
        return accessKey != null && secretKey != null;
    }

    private static class NativeMethods
    {
        public const int CRED_TYPE_GENERIC = 1;
        public const int CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredWrite([In] ref CREDENTIAL credential, [In] int flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern void CredFree([In] IntPtr credential);
    }
}
