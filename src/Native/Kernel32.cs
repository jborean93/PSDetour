using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace PSDetour.Native
{
    internal static class Kernel32
    {
        public const int INFINITE = -1;

        [DllImport("Kernel32.dll")]
        public static extern bool CloseHandle(
            IntPtr hObject);

        [DllImport("Kernel32.dll", EntryPoint = "CreateRemoteThread", SetLastError = true)]
        private static extern SafeNativeHandle NativeCreateRemoteThread(
            SafeNativeHandle hProcess,
            IntPtr lpThreadAttributes,
            UIntPtr dwStackSize,
            IntPtr lpStartAddress,
            IntPtr lpParameter,
            ThreadCreationFlags dwCreationFlags,
            out int lpThreadId);

        public static SafeNativeHandle CreateRemoteThread(SafeNativeHandle process, int stackSize,
            IntPtr startAddress, IntPtr parameter, ThreadCreationFlags creationFlags, out int threadId)
        {
            SafeNativeHandle thread = NativeCreateRemoteThread(process, IntPtr.Zero, (UIntPtr)stackSize, startAddress,
                parameter, creationFlags, out threadId);
            if (thread.DangerousGetHandle() == IntPtr.Zero)
            {
                throw new Win32Exception();
            }

            return thread;
        }

        [DllImport("Kernel32.dll", EntryPoint = "GetExitCodeThread", SetLastError = true)]
        private static extern bool NativeGetExitCodeThread(
            SafeNativeHandle hThread,
            out int lpExitCode);

        public static int GetExitCodeThread(SafeNativeHandle thread)
        {
            if (!NativeGetExitCodeThread(thread, out var exitCode))
            {
                throw new Win32Exception();
            }

            return exitCode;
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleFileNameW", SetLastError = true)]
        private static extern int NativeGetModuleHandleW(
            IntPtr hModule,
            StringBuilder lpFileName,
            int nSize);

        public static string GetModuleFileNameW(IntPtr module)
        {
            StringBuilder buffer = new StringBuilder(256);
            int length = NativeGetModuleHandleW(module, buffer, buffer.Capacity);
            if (length == 0)
            {
                throw new Win32Exception();
            }

            return buffer.ToString(0, length);
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW", SetLastError = true)]
        private static extern IntPtr NativeGetModuleHandleW(
            string lpModuleName);

        public static IntPtr GetModuleHandleW(string moduleName)
        {
            IntPtr handle = NativeGetModuleHandleW(moduleName);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception();
            }

            return handle;
        }

        [DllImport("Kernel32.dll", CharSet = CharSet.Ansi, EntryPoint = "GetProcAddress", SetLastError = true)]
        private static extern IntPtr NativeGetProcAddress(
            IntPtr module,
            string lpProcName);

        public static IntPtr GetProcAddress(IntPtr module, string name)
        {
            IntPtr addr = NativeGetProcAddress(module, name);
            if (addr == IntPtr.Zero)
            {
                throw new Win32Exception();
            }

            return addr;
        }

        [DllImport("Kernel32.dll")]
        public static extern bool FreeLibrary(
            IntPtr hLibModule);

        [DllImport("Kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryW", SetLastError = true)]
        private static extern IntPtr NativeLoadLibraryW(
            string lpLibFileName);

        public static IntPtr LoadLibraryW(string fileName)
        {
            IntPtr lib = NativeLoadLibraryW(fileName);
            if (lib == IntPtr.Zero)
            {
                throw new Win32Exception();
            }

            return lib;
        }

        [DllImport("Kernel32.dll", EntryPoint = "OpenProcess", SetLastError = true)]
        private static extern SafeNativeHandle NativeOpenProcess(
            ProcessAccessRights dwDesiredAccess,
            bool bInheritHandle,
            Int32 dwProcessId);

        public static SafeNativeHandle OpenProcess(int processId, ProcessAccessRights access, bool inherit)
        {
            SafeNativeHandle handle = NativeOpenProcess(access, inherit, processId);
            if (handle.DangerousGetHandle() == IntPtr.Zero)
                throw new Win32Exception();

            return handle;
        }

        [DllImport("Kernel32.dll", EntryPoint = "VirtualAllocEx", SetLastError = true)]
        private static extern IntPtr NativeVirtualAllocEx(
            SafeNativeHandle hProcess,
            IntPtr lpAddress,
            UIntPtr dwSize,
            MemoryAllocationType flAllocationType,
            MemoryProtection flProtect);

        public static IntPtr VirtualAllocEx(SafeNativeHandle process, IntPtr address, int size,
            MemoryAllocationType allocationType, MemoryProtection protection)
        {
            IntPtr mem = NativeVirtualAllocEx(process, address, (UIntPtr)size, allocationType, protection);
            if (mem == IntPtr.Zero)
            {
                throw new Win32Exception();
            }

            return mem;
        }

        [DllImport("Kernel32.dll", EntryPoint = "VirtualFreeEx", SetLastError = true)]
        private static extern bool NativeVirtualFreeEx(
            SafeNativeHandle hProcess,
            IntPtr lPAddress,
            UIntPtr dwSize,
            MemoryFreeType dwFreeType);

        public static void VirtualFreeEx(SafeNativeHandle process, IntPtr address, int size, MemoryFreeType freeType)
        {
            if (!NativeVirtualFreeEx(process, address, (UIntPtr)size, freeType))
            {
                throw new Win32Exception();
            }
        }

        [DllImport("Kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
        private static extern int NativeWaitForSingleObject(
            SafeHandle hHandle,
            int dwMilliseconds);

        public static int WaitForSingleObject(SafeHandle handle, int milliseconds)
        {
            int res = NativeWaitForSingleObject(handle, milliseconds);
            if (res == 0x00000102)
            {
                throw new TimeoutException();
            }
            else if (res == -1)
            {
                throw new Win32Exception();
            }

            return res;
        }

        [DllImport("Kernel32.dll", EntryPoint = "WriteProcessMemory", SetLastError = true)]
        private unsafe static extern bool NativeWriteProcessMemory(
            SafeNativeHandle hProcess,
            IntPtr lpBaseAddress,
            byte* lpBuffer,
            UIntPtr nSize,
            out UIntPtr lpNumberOfBytesWritten);

        public static int WriteProcessMemory(SafeNativeHandle process, IntPtr address, byte[] data)
        {
            UIntPtr written;
            unsafe
            {
                fixed (byte* dataPtr = data)
                {
                    if (!NativeWriteProcessMemory(process, address, dataPtr, (UIntPtr)data.Length, out written))
                    {
                        throw new Win32Exception();
                    }
                }
            }

            return (int)written;
        }
    }

    [Flags]
    internal enum MemoryAllocationType : uint
    {
        None = 0x00000000,
        /// MEM_COMMIT
        Commit = 0x00001000,
        /// MEM_RESERVE
        Reserve = 0x00002000,
        /// MEM_RESET
        Reset = 0x00080000,
        /// MEM_RESET_UNDO
        ResetUndo = 0x10000000,
        /// MEM_LARGE_PAGES
        LargePages = 0x20000000,
        /// MEM_PHYSICAL
        Physical = 0x00400000,
        /// MEM_TOP_DOWN
        TopDown = 0x00100000,
    }

    [Flags]
    internal enum MemoryFreeType : uint
    {
        None = 0x00000000,
        /// MEM_COALESCE_PLACEHOLDERS
        CoalescePlaceholders = 0x00000001,
        /// MEM_PRESERVE_PLACEHOLDER
        PreservePlaceholder = 0x00000002,
        /// MEM_DECOMMIT
        Decommit = 0x00004000,
        /// MEM_RELEASE
        Release = 0x00008000,
    }

    [Flags]
    internal enum MemoryProtection : uint
    {
        None = 0x00000000,
        /// PAGE_NOACCESS
        NoAccess = 0x00000001,
        /// PAGE_READONLY
        ReadOnly = 0x00000002,
        /// PAGE_READWRITE
        ReadWrite = 0x00000004,
        /// PAGE_WRITECOPY
        WriteCopy = 0x00000008,
        /// PAGE_EXECUTE
        Execute = 0x00000010,
        /// PAGE_EXECUTE_READ
        ExecuteRead = 0x00000020,
        /// PAGE_EXECUTE_READWRITE
        ExecuteReadWrite = 0x00000040,
        /// PAGE_EXECUTE_WRITECOPY
        ExecuteWriteCopy = 0x00000080,
        /// PAGE_TARGETS_INVALID
        TargetsInvalid = 0x40000000,
        /// PAGE_TARGETS_NO_UPDATE
        TargetsNoUpdate = 0x40000000,
    }

    [Flags]
    internal enum ProcessAccessRights
    {
        /// <summary>
        /// PROCESS_TERMINATE - Required to terminate a process.
        /// </summary>
        Terminate = 0x00000001,
        /// <summary>
        /// PROCESS_CREATE_THREAD - Required to create a thread.
        /// </summary>
        CreateThread = 0x00000002,
        /// <summary>
        /// PROCESS_VM_OPERATION - Required to perform an operation on the address space of a process.
        /// </summary>
        VMOperation = 0x00000008,
        /// <summary>
        /// PROCESS_VM_READ - Required to read memory in a process.
        /// </summary>
        VMRead = 0x00000010,
        /// <summary>
        /// PROCESS_VM_WRITE - Required to write to memory in a process.
        /// </summary>
        VMWrite = 0x00000020,
        /// <summary>
        /// PROCESS_DUP_HANDLE - Required to duplicate a handle.
        /// </summary>
        DupHandle = 0x00000040,
        /// <summary>
        /// PROCESS_CREATE_PROCESS - Required to create a process.
        /// </summary>
        CreateProcess = 0x00000080,
        /// <summary>
        /// PROCESS_SET_QUOTA - Required to set memory limits.
        /// </summary>
        SetQuota = 0x00000100,
        /// <summary>
        /// PROCESS_SET_INFORMATION - Required to set certain information about a process.
        /// </summary>
        SetInformation = 0x00000200,
        /// <summary>
        /// PROCESS_QUERY_INFORMATION - Required to retrieve certain information about a process.
        /// </summary>
        QueryInformation = 0x00000400,
        /// <summary>
        /// PROCESS_SUSPEND_RESUME - Required to suspend or resume a process.
        /// </summary>
        SuspendResume = 0x00000800,
        /// <summary>
        /// PROCESS_QUERY_LIMITED_INFORMATION - Required to retrieved certain limited information about a process.
        /// </summary>
        QueryLimitedInformation = 0x00001000,

        /// <summary>
        /// DELETE - Required to delete the object.
        /// </summary>
        Delete = 0x00010000,
        /// <summary>
        /// READ_CONTROL - Required to read information in the security descriptor.
        /// </summary>
        ReadControl = 0x00020000,
        /// <summary>
        /// WRITE_DAC - Required to modify the DACL in the security descriptor for the object.
        /// </summary>
        WriteDAC = 0x00040000,
        /// <summary>
        /// WRITE_OWNER - Required to change the owner in the security descriptor for the object.
        /// </summary>
        WriteOwner = 0x00080000,
        /// <summary>
        /// SYNCHRONIZE - Enables a thread to wait until the object is in the signaled state.
        /// </summary>
        Synchronize = 0x00100000,
        /// <summary>
        /// ACCESS_SYSTEM_SECURITY - Required to read/modify the SACL in the security descriptor for the object.
        /// </summary>
        AccessSystemSecurity = 0x01000000,

        /// <summary>
        /// STANDARD_RIGHTS_ALL
        /// </summary>
        StandardRightsAll = Delete | ReadControl | WriteDAC | WriteOwner | Synchronize,
        /// <summary>
        /// STANDARD_RIGHTS_EXECUTE
        /// </summary>
        StandardRightsExecute = ReadControl,
        /// <summary>
        /// STANDARD_RIGHTS_READ
        /// </summary>
        StandardRightsRead = ReadControl,
        /// <summary>
        /// STANDARD_RIGHTS_REQUIRED
        /// </summary>
        StandardRightsRequired = Delete | ReadControl | WriteDAC | WriteOwner,
        /// <summary>
        /// STANDARD_RIGHTS_WRITE
        /// </summary>
        StandardRightsWrite = ReadControl,

        /// <summary>
        /// GENERIC_ALL
        /// </summary>
        GenericAll = 0x10000000,
        /// <summary>
        /// GENERIC_EXECUTE
        /// </summary>
        GenericExecute = 0x20000000,
        /// <summary>
        /// GENERIC_WRITE
        /// </summary>
        GenericWrite = 0x40000000,
        /// <summary>
        /// GENERIC_READ
        /// </summary>
        GenericRead = -2147483648,

        /// <summary>
        /// PROCESS_ALL_ACCESS - All possible access rights for a process object.
        /// </summary>
        AllAccess = StandardRightsRequired | Synchronize | 0x1FFF,
    }

    [Flags]
    internal enum ThreadCreationFlags : uint
    {
        None = 0,
        /// CREATE_SUSPENDED
        CreateSuspended = 0x00000004,
        /// STACK_SIZE_PARAM_IS_A_RESERVATION
        StackSizeParamIsAReservation = 0x00010000,
    }

    internal class SafeNativeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeNativeHandle() : base(true) { }
        public SafeNativeHandle(IntPtr handle) : this(handle, true) { }

        public SafeNativeHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle()
        {
            return Kernel32.CloseHandle(handle);
        }
    }
}
