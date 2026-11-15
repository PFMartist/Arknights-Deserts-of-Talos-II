Set-StrictMode -Version Latest
function Get-RhodesProcessContext([int]$ProcessId) {
    if ($ProcessId -le 0) { throw 'ProcessId must be positive.' }
    if (-not ('RhodesLauncherProcessContext' -as [type])) {
        # Same read-only Win32 query audited in audit/build_audit/ProcessContextReadOnly.cs.
        # No privilege adjustment, impersonation, process launch or Steam API call.
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

// Read-only process context inspection. No privilege adjustment, impersonation,
// Steam API calls or process launch is performed.
public sealed class RhodesLauncherProcessContextInfo
{
    public int ProcessId;
    public string ImagePath;
    public uint SessionId;
    public string UserName;
    public string UserSid;
    public int IntegrityRid;
    public string Error;
    public int Win32Error;
}
public static class RhodesLauncherProcessContext
{
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("advapi32.dll", SetLastError=true)]
    static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError=true)]
    static extern bool GetTokenInformation(IntPtr token, int tokenClass, IntPtr data, uint length, out uint required);
    [DllImport("advapi32.dll")]
    static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
    [DllImport("advapi32.dll")]
    static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    static IntPtr TokenBuffer(IntPtr token, int tokenClass)
    {
        uint required;
        GetTokenInformation(token, tokenClass, IntPtr.Zero, 0, out required);
        if (required == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        IntPtr buffer = Marshal.AllocHGlobal((int)required);
        if (!GetTokenInformation(token, tokenClass, buffer, required, out required))
        {
            int error = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(buffer);
            throw new System.ComponentModel.Win32Exception(error);
        }
        return buffer;
    }
    public static RhodesLauncherProcessContextInfo Inspect(int processId)
    {
        RhodesLauncherProcessContextInfo result = new RhodesLauncherProcessContextInfo();
        result.ProcessId = processId;
        IntPtr process = IntPtr.Zero, token = IntPtr.Zero;
        try
        {
            process = OpenProcess(0x1000, false, processId); // QUERY_LIMITED_INFORMATION
            if (process == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            StringBuilder path = new StringBuilder(32768);
            uint pathLength = (uint)path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref pathLength))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            result.ImagePath = path.ToString();
            uint session;
            if (!ProcessIdToSessionId((uint)processId, out session))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            result.SessionId = session;
            if (!OpenProcessToken(process, 8, out token)) // TOKEN_QUERY
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            IntPtr user = TokenBuffer(token, 1); // TokenUser
            try
            {
                SecurityIdentifier sid = new SecurityIdentifier(Marshal.ReadIntPtr(user));
                result.UserSid = sid.Value;
                try { result.UserName = sid.Translate(typeof(NTAccount)).Value; }
                catch (IdentityNotMappedException) { result.UserName = sid.Value; }
            }
            finally { Marshal.FreeHGlobal(user); }
            IntPtr mandatory = TokenBuffer(token, 25); // TokenIntegrityLevel
            try
            {
                IntPtr sid = Marshal.ReadIntPtr(mandatory);
                byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                if (count == 0) throw new InvalidOperationException("Integrity SID has no subauthority.");
                result.IntegrityRid = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            }
            finally { Marshal.FreeHGlobal(mandatory); }
        }
        catch (System.ComponentModel.Win32Exception e) { result.Error = e.Message; result.Win32Error = e.NativeErrorCode; }
        catch (Exception e) { result.Error = e.GetType().Name + ": " + e.Message; }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
        return result;
    }
}

'@
    }
    $context=[RhodesLauncherProcessContext]::Inspect($ProcessId)
    if ($context.Error) { throw "Cannot inspect process $ProcessId token: $($context.Error) (Win32 $($context.Win32Error)). Launch is blocked." }
    $label=switch ($context.IntegrityRid) {
        {$_ -ge 0x4000} {'System';break}
        {$_ -ge 0x3000} {'High';break}
        {$_ -gt 0x2000} {'MediumPlus';break}
        {$_ -eq 0x2000} {'Medium';break}
        {$_ -ge 0x1000} {'Low';break}
        default {'Untrusted'}
    }
    return [pscustomobject]@{ProcessId=$context.ProcessId;ImagePath=$context.ImagePath;SessionId=$context.SessionId;UserName=$context.UserName;UserSid=$context.UserSid;IntegrityRid=$context.IntegrityRid;IntegrityHex=('0x{0:x4}' -f $context.IntegrityRid);IntegrityLabel=$label}
}
Export-ModuleMember -Function Get-RhodesProcessContext
