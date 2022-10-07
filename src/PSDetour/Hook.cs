using PSDetour.Native;
using System;
using System.IO;
using System.ComponentModel;
using System.IO.Pipes;
using System.Text;

namespace PSDetour
{
    public static class Hook
    {
        public static void Main(int pid, string cmd)
        {
            string assemblyPath = typeof(Hook).Assembly.Location;
            string targetDll = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(assemblyPath) ?? "", "PSDetourNative.dll"));

            IntPtr lib = Kernel32.LoadLibraryW(targetDll);
            try
            {
                IntPtr initAddr = Kernel32.GetProcAddress(lib, "inject");
                const ProcessAccessRights procRights = ProcessAccessRights.CreateThread |
                    ProcessAccessRights.DupHandle |
                    ProcessAccessRights.QueryInformation |
                    ProcessAccessRights.VMOperation |
                    ProcessAccessRights.VMRead |
                    ProcessAccessRights.VMWrite;
                using var process = Kernel32.OpenProcess(pid, procRights, false);

                IntPtr remoteModAddr = IntPtr.Zero;
                byte[] paramBytes = Encoding.Unicode.GetBytes(targetDll);
                RemoteLibraryFunction(process, "Kernel32.dll", "LoadLibraryW", paramBytes).Dispose();
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
                    if (remoteModAddr == IntPtr.Zero)
                    {
                        throw new Exception($"Failed to load dll in remote process {pid}");
                    }

                    int initOffset = (int)(initAddr.ToInt64() - lib.ToInt64());
                    IntPtr remoteAddr = IntPtr.Add(remoteModAddr, initOffset);

                    UTF8Encoding utf8Encoding = new UTF8Encoding();
                    using AnonymousPipeServerStream pipeOut = new AnonymousPipeServerStream(PipeDirection.Out,
                        HandleInheritability.None);
                    using StreamWriter pipeWriter = new StreamWriter(pipeOut, utf8Encoding);
                    pipeWriter.AutoFlush = true;
                    using SafeDuplicateHandle clientPipeOut = Kernel32.DuplicateHandle(
                        Kernel32.GetCurrentProcess(),
                        pipeOut.ClientSafePipeHandle,
                        process,
                        0,
                        false,
                        DuplicateHandleOptions.SameAccess,
                        true);
                    pipeOut.DisposeLocalCopyOfClientHandle();

                    using AnonymousPipeServerStream pipeIn = new AnonymousPipeServerStream(PipeDirection.In,
                        HandleInheritability.None);
                    using StreamReader pipeReader = new StreamReader(pipeIn, utf8Encoding);
                    using SafeDuplicateHandle clientPipeIn = Kernel32.DuplicateHandle(
                        Kernel32.GetCurrentProcess(),
                        pipeIn.ClientSafePipeHandle,
                        process,
                        0,
                        false,
                        DuplicateHandleOptions.SameAccess,
                        true);
                    pipeIn.DisposeLocalCopyOfClientHandle();

                    using SafeNativeHandle remoteThread = RemoteLibraryFunction(process, initAddr, null,
                        test: clientPipeOut.DangerousGetHandle(), wait: false);
                    Console.WriteLine("Thread exit");

                    pipeWriter.WriteLine(clientPipeIn.DangerousGetHandle().ToInt64().ToString());

                    string confirmation = pipeReader.ReadLine() ?? "confirmation";
                    if (confirmation != "connected")
                    {
                        throw new Exception("Invalid confirmation");
                    }

                    pipeWriter.WriteLine(cmd);

                    string result = pipeReader.ReadLine() ?? "no result";
                    Console.WriteLine($"Received: {result}");

                    Kernel32.WaitForSingleObject(remoteThread, Kernel32.INFINITE);

                    Console.WriteLine("done");
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

        private static SafeNativeHandle RemoteLibraryFunction(SafeNativeHandle process, string moduleName, string name,
            byte[]? parameter, IntPtr? test = null)
        {
            IntPtr funcAddr = Kernel32.GetProcAddress(Kernel32.GetModuleHandleW(moduleName), name);
            return RemoteLibraryFunction(process, funcAddr, parameter, test: test);
        }

        private static SafeNativeHandle RemoteLibraryFunction(SafeNativeHandle process, IntPtr func,
            byte[]? parameter, IntPtr? test = null, bool wait = true)
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

                SafeNativeHandle thread = Kernel32.CreateRemoteThread(process, 0, func, paramAddr,
                    ThreadCreationFlags.None, out var _);
                if (wait)
                {
                    Kernel32.WaitForSingleObject(thread, Kernel32.INFINITE);
                }

                return thread;
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
