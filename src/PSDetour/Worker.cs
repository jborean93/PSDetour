using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.IO.Pipes;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;

namespace PSDetour
{
    public class RemoteWorker
    {
        public static int Main(IntPtr arg, int sizeBytes)
        {
            UTF8Encoding utf8Encoding = new UTF8Encoding();
            using AnonymousPipeClientStream pipeIn = new AnonymousPipeClientStream(PipeDirection.In, new SafePipeHandle(arg, false));
            using StreamReader pipeReader = new StreamReader(pipeIn, utf8Encoding);
            string clientPipeAddr = pipeReader.ReadLine() ?? "nothing";

            IntPtr rawClientOutPtr = new IntPtr(Int64.Parse(clientPipeAddr));
            using AnonymousPipeClientStream pipeOut = new AnonymousPipeClientStream(PipeDirection.Out, new SafePipeHandle(rawClientOutPtr, false));
            using StreamWriter sw = new StreamWriter(pipeOut, utf8Encoding);
            sw.AutoFlush = true;
            sw.WriteLine("connected");

            string scriptToRun = pipeReader.ReadLine() ?? "unknown command";
            using Runspace rs = RunspaceFactory.CreateRunspace();
            rs.Open();
            using PowerShell ps = PowerShell.Create();
            ps.Runspace = rs;
            ps.AddScript(scriptToRun);
            string cmdOut = ps.Invoke<string>()[0].ToString();

            sw.WriteLine(cmdOut);

            return 0;
        }
    }
}
