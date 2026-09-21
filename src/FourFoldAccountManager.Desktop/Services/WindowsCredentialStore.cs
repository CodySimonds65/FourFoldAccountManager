using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FourFoldAccountManager.Desktop.Services;

/// <summary>
/// Stores saved account credentials in the current Windows user's Credential Manager.
/// The account JSON file never contains usernames or passwords.
/// </summary>
public sealed class WindowsCredentialStore
{
    private const uint GenericCredentialType = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public AccountCredentials? Read(Guid accountId)
    {
        var targetName = GetTargetName(accountId);
        if (!CredRead(targetName, GenericCredentialType, 0, out var credentialPointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, "Windows Credential Manager could not read this profile's saved login.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            var username = Marshal.PtrToStringUni(credential.UserName) ?? string.Empty;
            if (credential.CredentialBlobSize > 5120 || credential.CredentialBlobSize % 2 != 0)
            {
                throw new InvalidDataException("The saved login has an invalid password size.");
            }

            var passwordBytes = new byte[credential.CredentialBlobSize];
            try
            {
                if (passwordBytes.Length > 0)
                {
                    Marshal.Copy(credential.CredentialBlob, passwordBytes, 0, passwordBytes.Length);
                }

                return new AccountCredentials(username, Encoding.Unicode.GetString(passwordBytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(passwordBytes);
            }
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public void Save(Guid accountId, AccountCredentials? credentials)
    {
        var targetName = GetTargetName(accountId);
        if (credentials is null || (string.IsNullOrWhiteSpace(credentials.Username) && credentials.Password.Length == 0))
        {
            Delete(accountId);
            return;
        }

        if (string.IsNullOrWhiteSpace(credentials.Username) || credentials.Password.Length == 0)
        {
            throw new ArgumentException("A saved login must include both a username and a password.", nameof(credentials));
        }

        var passwordBytes = Encoding.Unicode.GetBytes(credentials.Password);
        if (passwordBytes.Length > 5120)
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            throw new ArgumentException("The password is too long to save in Windows Credential Manager.", nameof(credentials));
        }

        var targetPointer = Marshal.StringToHGlobalUni(targetName);
        var usernamePointer = Marshal.StringToHGlobalUni(credentials.Username);
        var passwordHandle = GCHandle.Alloc(passwordBytes, GCHandleType.Pinned);
        try
        {
            var nativeCredential = new NativeCredential
            {
                Type = GenericCredentialType,
                TargetName = targetPointer,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = passwordHandle.AddrOfPinnedObject(),
                Persist = PersistLocalMachine,
                UserName = usernamePointer
            };

            if (!CredWrite(ref nativeCredential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Windows Credential Manager could not save this profile's login.");
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            passwordHandle.Free();
            Marshal.FreeHGlobal(targetPointer);
            Marshal.FreeHGlobal(usernamePointer);
        }
    }

    public void Delete(Guid accountId)
    {
        if (CredDelete(GetTargetName(accountId), GenericCredentialType, 0))
        {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error != ErrorNotFound)
        {
            throw new Win32Exception(error, "Windows Credential Manager could not remove this profile's saved login.");
        }
    }

    private static string GetTargetName(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new ArgumentException("An account ID is required.", nameof(accountId));
        }

        return $"FourFoldAccountManager:account:{accountId:N}";
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string targetName, uint type, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
