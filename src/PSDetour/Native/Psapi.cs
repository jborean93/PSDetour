using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PSDetour.Native
{
    internal static class Psapi
    {
        [DllImport("Psapi.dll", EntryPoint = "EnumProcessModulesEx")]
        private static extern bool NativeEnumProcessModulesEx(
            SafeNativeHandle hProcess,
            IntPtr[] lphModule,
            int cb,
            out int lpcbNeeded,
            EnumProcessModulesFilterFlag dwFilterFlag);

        public static IntPtr[] EnumProcessModulesEx(SafeNativeHandle process, EnumProcessModulesFilterFlag filterFlag)
        {
            IntPtr[] modules = Array.Empty<IntPtr>();
            int needed = 0;
            while (true)
            {
                int size = modules.Length * IntPtr.Size;
                if (!NativeEnumProcessModulesEx(process, modules, size, out needed, filterFlag))
                {
                    throw new Win32Exception();
                }

                if (needed > size)
                {
                    modules = new IntPtr[needed / IntPtr.Size];
                }
                else
                {
                    return modules;
                }
            }
        }
    }

    internal enum EnumProcessModulesFilterFlag : uint
    {
        /// LIST_MODULES_DEFAULT
        Default = 0x00,
        /// LIST_MODULES_32BIT
        x86 = 0x01,
        /// LIST_MODULES_64BIT
        x64 = 0x02,
        /// LIST_MODULES_ALL
        All = 0x03,
    }
}
