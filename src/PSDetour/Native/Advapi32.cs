using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PSDetour.Native;

internal partial class NativeHelpers
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public UInt32 Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_USER
    {
        public SID_AND_ATTRIBUTES User;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_GROUPS
    {
        public Int32 GroupCount;
        public SID_AND_ATTRIBUTES Groups;  // Technically an array but dotnet marshalling needs some help
    }
}

internal static class Advapi32
{
    [DllImport("Advapi32.dll", SetLastError = true)]
    private unsafe static extern bool GetTokenInformation(
        SafeAccessTokenHandle TokenHandle,
        TokenInformationClass TokenInformationClass,
        void* TokenInformation,
        Int32 TokenInformationLength,
        out Int32 ReturnLength);

    public static SecurityIdentifier GetTokenUser(SafeAccessTokenHandle token)
    {
        unsafe
        {
            GetTokenInformation(token, TokenInformationClass.TokenUser, null, 0, out var returnLength);

            byte[] data = new byte[returnLength];
            fixed (byte* dataPtr = data)
            {
                if (!GetTokenInformation(token, TokenInformationClass.TokenUser, dataPtr, returnLength,
                    out var _))
                {
                    throw new Win32Exception();
                }

                ReadOnlySpan<NativeHelpers.TOKEN_USER> user = new(dataPtr, 1);
                return new(user[0].User.Sid);
            }
        }
    }

    public static SecurityIdentifier GetTokenLogonSid(SafeAccessTokenHandle token)
    {
        unsafe
        {
            GetTokenInformation(token, TokenInformationClass.TokenLogonSid, null, 0, out var returnLength);

            byte[] data = new byte[returnLength];
            fixed (byte* dataPtr = data)
            {
                if (!GetTokenInformation(token, TokenInformationClass.TokenLogonSid, dataPtr, returnLength,
                    out var _))
                {
                    throw new Win32Exception();
                }

                ReadOnlySpan<NativeHelpers.TOKEN_GROUPS> groups = new(dataPtr, 1);
                return new(groups[0].Groups.Sid);
            }
        }
    }

    [DllImport("Advapi32.dll", EntryPoint = "OpenProcessToken", SetLastError = true)]
    private static extern bool NativeOpenProcessToken(
        SafeHandle ProcessHandle,
        TokenAccessLevels DesiredAccess,
        out SafeAccessTokenHandle TokenHandle);

    public static SafeAccessTokenHandle OpenProcessToken(SafeHandle process, TokenAccessLevels access)
    {
        if (!NativeOpenProcessToken(process, access, out var token))
        {
            throw new Win32Exception();
        }

        return token;
    }
}

internal enum TokenInformationClass
{
    TokenUser = 1,
    TokenGroups,
    TokenPrivileges,
    TokenOwner,
    TokenPrimaryGroup,
    TokenDefaultDacl,
    TokenSource,
    TokenType,
    TokenImpersonationLevel,
    TokenStatistics,
    TokenRestrictedSids,
    TokenSessionId,
    TokenGroupsAndPrivileges,
    TokenSessionReference,
    TokenSandBoxInert,
    TokenAuditPolicy,
    TokenOrigin,
    TokenElevationType,
    TokenLinkedToken,
    TokenElevation,
    TokenHasRestrictions,
    TokenAccessInformation,
    TokenVirtualizationAllowed,
    TokenVirtualizationEnabled,
    TokenIntegrityLevel,
    TokenUIAccess,
    TokenMandatoryPolicy,
    TokenLogonSid,
    TokenIsAppContainer,
    TokenCapabilities,
    TokenAppContainerSid,
    TokenAppContainerNumber,
    TokenUserClaimAttributes,
    TokenDeviceClaimAttributes,
    TokenRestrictedUserClaimAttributes,
    TokenRestrictedDeviceClaimAttributes,
    TokenDeviceGroups,
    TokenRestrictedDeviceGroups,
    TokenSecurityAttributes,
    TokenIsRestricted,
    TokenProcessTrustLevel,
    TokenPrivateNameSpace,
    TokenSingletonAttributes,
    TokenBnoIsolation,
    TokenChildProcessFlags,
    TokenIsLessPrivilegedAppContainer,
    TokenIsSandboxed,
    TokenOriginatingProcessTrustLevel,
}
