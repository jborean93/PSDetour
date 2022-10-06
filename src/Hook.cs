using PSDetour.Native;
using System;
using System.ComponentModel;
using System.Text;

namespace PSDetour
{
    public static class Hook
    {
        public static void Main(int pid, string targetDll, string method, IntPtr argument)
        {
            IntPtr lib = Kernel32.LoadLibraryW(targetDll);
            try
            {
                IntPtr initAddr = Kernel32.GetProcAddress(lib, method);
                const ProcessAccessRights procRights = ProcessAccessRights.CreateThread |
                    ProcessAccessRights.QueryInformation |
                    ProcessAccessRights.VMOperation |
                    ProcessAccessRights.VMRead |
                    ProcessAccessRights.VMWrite;
                using var process = Kernel32.OpenProcess(pid, procRights, false);

                IntPtr remoteModAddr = IntPtr.Zero;
                byte[] paramBytes = Encoding.Unicode.GetBytes(targetDll);
                RemoteLibraryFunction(process, "Kernel32.dll", "LoadLibraryW", paramBytes);
                try
                {
                    foreach (IntPtr mod in Psapi.EnumProcessModulesEx(process, EnumProcessModulesFilterFlag.All))
                    {
                        if (mod == IntPtr.Zero)
                        {
                            continue;
                        }

                        string moduleName;
                        try
                        {
                            moduleName = Kernel32.GetModuleFileNameW(mod);
                        }
                        catch (Win32Exception)
                        {
                            continue;
                        }

                        if (string.Equals(moduleName, targetDll, StringComparison.OrdinalIgnoreCase))
                        {
                            remoteModAddr = mod;
                            break;
                        }
                    }

                    int initOffset = (int)(initAddr.ToInt64() - lib.ToInt64());
                    IntPtr remoteAddr = IntPtr.Add(remoteModAddr, initOffset);
                    RemoteLibraryFunction(process, initAddr, null, test: argument);

                    // TODO: Create anon pipe to talk to CLR that the remote thread starts
                }
                finally
                {
                    if (remoteModAddr != IntPtr.Zero)
                    {
                        RemoteLibraryFunction(process, "Kernel32.dll", "FreeLibrary", null, test: remoteModAddr);
                    }
                }
            }
            finally
            {
                Kernel32.FreeLibrary(lib);
            }
        }

        private static void RemoteLibraryFunction(SafeNativeHandle process, string moduleName, string name,
            byte[]? parameter, IntPtr? test = null)
        {
            IntPtr funcAddr = Kernel32.GetProcAddress(Kernel32.GetModuleHandleW(moduleName), name);
            RemoteLibraryFunction(process, funcAddr, parameter, test: test);
        }

        private static void RemoteLibraryFunction(SafeNativeHandle process, IntPtr func, byte[]? parameter,
            IntPtr? test = null)
        {
            IntPtr paramAddr = IntPtr.Zero;

            try
            {
                if (test != null)
                {
                    paramAddr = (IntPtr)test;
                }
                else if (parameter != null)
                {
                    paramAddr = Kernel32.VirtualAllocEx(process,
                        IntPtr.Zero,
                        parameter.Length,
                        MemoryAllocationType.Reserve | MemoryAllocationType.Commit,
                        MemoryProtection.ExecuteReadWrite);

                    Kernel32.WriteProcessMemory(process, paramAddr, parameter);
                }

                using SafeNativeHandle thread = Kernel32.CreateRemoteThread(process, 0, func, paramAddr,
                    ThreadCreationFlags.None, out var _);
                Kernel32.WaitForSingleObject(thread, Kernel32.INFINITE);
            }
            finally
            {
                if (paramAddr != IntPtr.Zero && test == null)
                {
                    Kernel32.VirtualFreeEx(process, paramAddr, 0, MemoryFreeType.Release);
                }
            }
        }
    }
}
