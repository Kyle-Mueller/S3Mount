using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Newtonsoft.Json;
using S3Mount.Models;

namespace S3Mount.Services;

public class CredentialService
{
    private const string CredentialTarget = "S3Mount_Configuration";
    
    public void SaveConfiguration(S3MountConfiguration config)
    {
        System.Diagnostics.Debug.WriteLine($"SaveConfiguration - Starting for {config.MountName} (ID: {config.Id})");
        
        var configs = GetAllConfigurations();
        System.Diagnostics.Debug.WriteLine($"SaveConfiguration - Current config count: {configs.Count}");
        
        // Remove existing config with same ID
        configs.RemoveAll(c => c.Id == config.Id);
        System.Diagnostics.Debug.WriteLine($"SaveConfiguration - After removing duplicates: {configs.Count}");
        
        // Add updated config
        configs.Add(config);
        System.Diagnostics.Debug.WriteLine($"SaveConfiguration - After adding new config: {configs.Count}");
        
        // Serialize and encrypt
        var json = JsonConvert.SerializeObject(configs);
        System.Diagnostics.Debug.WriteLine($"SaveConfiguration - Serialized JSON length: {json.Length}");
        
        var encrypted = ProtectData(json);
        System.Diagnostics.Debug.WriteLine($"SaveConfiguration - Encrypted data length: {encrypted.Length}");
        
        // Store in credential manager
        SaveCredential(CredentialTarget, encrypted);
        System.Diagnostics.Debug.WriteLine("SaveConfiguration - Saved to credential manager");
    }
    
    public List<S3MountConfiguration> GetAllConfigurations()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("GetAllConfigurations - Starting");
            var encrypted = GetCredential(CredentialTarget);
            
            if (string.IsNullOrEmpty(encrypted))
            {
                System.Diagnostics.Debug.WriteLine("GetAllConfigurations - No encrypted data found");
                return new List<S3MountConfiguration>();
            }
            
            System.Diagnostics.Debug.WriteLine($"GetAllConfigurations - Got encrypted data, length: {encrypted.Length}");
            var json = UnprotectData(encrypted);
            System.Diagnostics.Debug.WriteLine($"GetAllConfigurations - Decrypted JSON length: {json.Length}");
            
            var configs = JsonConvert.DeserializeObject<List<S3MountConfiguration>>(json) ?? new List<S3MountConfiguration>();
            System.Diagnostics.Debug.WriteLine($"GetAllConfigurations - Returning {configs.Count} configurations");
            
            return configs;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"GetAllConfigurations - Error: {ex.Message}");
            return new List<S3MountConfiguration>();
        }
    }
    
    public S3MountConfiguration? GetConfiguration(Guid id)
    {
        return GetAllConfigurations().FirstOrDefault(c => c.Id == id);
    }
    
    public void DeleteConfiguration(Guid id)
    {
        var configs = GetAllConfigurations();
        configs.RemoveAll(c => c.Id == id);
        
        if (configs.Count == 0)
        {
            DeleteCredential(CredentialTarget);
        }
        else
        {
            var json = JsonConvert.SerializeObject(configs);
            var encrypted = ProtectData(json);
            SaveCredential(CredentialTarget, encrypted);
        }
    }
    
    private string ProtectData(string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        var entropy = Encoding.UTF8.GetBytes(Environment.UserName);
        var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(
            bytes, 
            entropy, 
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }
    
    private string UnprotectData(string protectedData)
    {
        var protectedBytes = Convert.FromBase64String(protectedData);
        var entropy = Encoding.UTF8.GetBytes(Environment.UserName);
        var bytes = System.Security.Cryptography.ProtectedData.Unprotect(
            protectedBytes, 
            entropy, 
            System.Security.Cryptography.DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
    
    private void SaveCredential(string target, string data)
    {
        // Ensure data isn't too large for Windows Credential Manager
        // Maximum size is approximately 2560 bytes
        var dataBytes = Encoding.Unicode.GetBytes(data);
        
        if (dataBytes.Length > 2560)
        {
            throw new InvalidOperationException($"Credential data is too large ({dataBytes.Length} bytes). Maximum is 2560 bytes.");
        }
        
        var credential = new CREDENTIAL
        {
            TargetName = target,
            Type = CRED_TYPE.GENERIC,
            Persist = CRED_PERSIST.LOCAL_MACHINE,
            CredentialBlob = data,
            CredentialBlobSize = dataBytes.Length,
            UserName = Environment.UserName
        };
        
        var nativeCredential = credential.ToNativeCredential();
        
        try
        {
            System.Diagnostics.Debug.WriteLine($"SaveCredential - Target: {target}, Size: {dataBytes.Length} bytes");
            
            if (!CredWrite(ref nativeCredential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                System.Diagnostics.Debug.WriteLine($"SaveCredential - CredWrite failed with error: {error}");
                throw new InvalidOperationException($"Failed to save credential. Error code: {error}");
            }
            
            System.Diagnostics.Debug.WriteLine("SaveCredential - Successfully saved");
        }
        finally
        {
            if (nativeCredential.CredentialBlob != IntPtr.Zero)
                Marshal.FreeHGlobal(nativeCredential.CredentialBlob);
            if (nativeCredential.TargetName != IntPtr.Zero)
                Marshal.FreeCoTaskMem(nativeCredential.TargetName);
            if (nativeCredential.UserName != IntPtr.Zero)
                Marshal.FreeCoTaskMem(nativeCredential.UserName);
        }
    }
    
    private string? GetCredential(string target)
    {
        IntPtr credPtr = IntPtr.Zero;
        
        try
        {
            if (!CredRead(target, CRED_TYPE.GENERIC, 0, out credPtr))
            {
                return null;
            }
            
            var nativeCredential = Marshal.PtrToStructure<NativeCredential>(credPtr);
            return Marshal.PtrToStringUni(nativeCredential.CredentialBlob, nativeCredential.CredentialBlobSize / 2);
        }
        finally
        {
            if (credPtr != IntPtr.Zero)
                CredFree(credPtr);
        }
    }
    
    private void DeleteCredential(string target)
    {
        CredDelete(target, CRED_TYPE.GENERIC, 0);
    }
    
    #region Win32 API
    
    [DllImport("Advapi32.dll", SetLastError = true, EntryPoint = "CredWriteW", CharSet = CharSet.Unicode)]
    private static extern bool CredWrite([In] ref NativeCredential userCredential, [In] uint flags);
    
    [DllImport("Advapi32.dll", SetLastError = true, EntryPoint = "CredReadW", CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);
    
    [DllImport("Advapi32.dll", SetLastError = true, EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, CRED_TYPE type, int flags);
    
    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern bool CredFree([In] IntPtr cred);
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public CRED_TYPE Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public CRED_PERSIST Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
    
    private struct CREDENTIAL
    {
        public CRED_TYPE Type;
        public string TargetName;
        public string CredentialBlob;
        public int CredentialBlobSize;
        public CRED_PERSIST Persist;
        public string UserName;
        
        public NativeCredential ToNativeCredential()
        {
            return new NativeCredential
            {
                Type = Type,
                TargetName = Marshal.StringToCoTaskMemUni(TargetName),
                CredentialBlob = Marshal.StringToCoTaskMemUni(CredentialBlob),
                CredentialBlobSize = CredentialBlobSize,
                Persist = Persist,
                UserName = Marshal.StringToCoTaskMemUni(UserName),
                AttributeCount = 0,
                Attributes = IntPtr.Zero
            };
        }
    }
    
    private enum CRED_TYPE : uint
    {
        GENERIC = 1,
        DOMAIN_PASSWORD = 2,
        DOMAIN_CERTIFICATE = 3,
        DOMAIN_VISIBLE_PASSWORD = 4,
        GENERIC_CERTIFICATE = 5,
        DOMAIN_EXTENDED = 6,
        MAXIMUM = 7,
        MAXIMUM_EX = 1007
    }
    
    private enum CRED_PERSIST : uint
    {
        SESSION = 1,
        LOCAL_MACHINE = 2,
        ENTERPRISE = 3
    }
    
    #endregion
}
