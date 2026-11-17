using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Momoko.Launcher.Core;

internal sealed record StoredSecret(string UserName,string Secret);

// Windows Credential Manager keeps secrets in the current user's vault. The
// Momoko refresh token is stored per passport origin; the developer build's
// SDK test password per SDK origin. Nothing secret goes into JSON or logs.
internal static unsafe partial class CredentialStore
{
    private const uint Generic=1;
    private const uint PersistLocalMachine=2;
    private const int NotFound=1168;
    private const int MaxBlob=5*512;

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags,Type;
        public char* TargetName;
        public char* Comment;
        public long LastWritten;
        public uint BlobSize;
        public byte* Blob;
        public uint Persist,AttributeCount;
        public nint Attributes;
        public char* TargetAlias;
        public char* UserName;
    }

    [LibraryImport("advapi32.dll",SetLastError=true,EntryPoint="CredWriteW")]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(Credential* credential,uint flags);

    [LibraryImport("advapi32.dll",SetLastError=true,EntryPoint="CredReadW",StringMarshalling=StringMarshalling.Utf16)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target,uint type,uint flags,out Credential* credential);

    [LibraryImport("advapi32.dll",SetLastError=true,EntryPoint="CredDeleteW",StringMarshalling=StringMarshalling.Utf16)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target,uint type,uint flags);

    [LibraryImport("advapi32.dll",EntryPoint="CredFree")]
    private static partial void CredFree(void* buffer);

    public static void Save(string target,string userName,string secret,string comment)
    {
        if(string.IsNullOrEmpty(secret)||secret.Contains('\0'))throw new InvalidDataException("要保存的凭据无效。");
        var bytes=Encoding.Unicode.GetBytes(secret);
        if(bytes.Length>MaxBlob)throw new InvalidDataException("要保存的凭据过长。");
        try
        {
            fixed(char* targetName=target)
            fixed(char* user=userName)
            fixed(char* note=comment)
            fixed(byte* blob=bytes)
            {
                var credential=new Credential
                {
                    Type=Generic,TargetName=targetName,UserName=user,Comment=note,
                    BlobSize=(uint)bytes.Length,Blob=blob,Persist=PersistLocalMachine
                };
                if(!CredWrite(&credential,0))
                    throw new Win32Exception(Marshal.GetLastPInvokeError(),"Windows 无法保存启动器凭据。");
            }
        }
        finally{CryptographicOperations.ZeroMemory(bytes);}
    }

    public static StoredSecret? Load(string target)
    {
        if(!CredRead(target,Generic,0,out var credential))
        {
            var error=Marshal.GetLastPInvokeError();
            if(error==NotFound)return null;
            throw new Win32Exception(error,"Windows 无法读取启动器凭据。");
        }
        try
        {
            if(credential->Blob==null||credential->BlobSize==0||credential->BlobSize>MaxBlob||(credential->BlobSize&1)!=0)return null;
            var secret=new string((char*)credential->Blob,0,(int)credential->BlobSize/2);
            var user=credential->UserName==null?"":new string(credential->UserName);
            return new StoredSecret(user,secret);
        }
        finally{CredFree(credential);}
    }

    public static void Delete(string target)
    {
        if(CredDelete(target,Generic,0))return;
        var error=Marshal.GetLastPInvokeError();
        if(error!=NotFound)throw new Win32Exception(error,"Windows 无法删除启动器凭据。");
    }
}
