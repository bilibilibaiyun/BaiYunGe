using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BaiYunGe.Core.Inference;

/// <summary>
/// 把子进程（llama-server）挂到 Windows Job Object，设置 KILL_ON_JOB_CLOSE，
/// 主程序无论正常/异常退出，子进程都会被系统一并终止，不残留。
/// </summary>
public sealed class ChildProcessJob : IDisposable
{
    private IntPtr _jobHandle;

    public ChildProcessJob()
    {
        _jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);
        if (_jobHandle == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed.");
        }

        var info = new NativeMethods.JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new NativeMethods.JobObjectBasicLimitInformation
            {
                LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        var length = Marshal.SizeOf<NativeMethods.JobObjectExtendedLimitInformation>();
        var pointer = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, pointer, false);
            if (!NativeMethods.SetInformationJobObject(
                    _jobHandle,
                    NativeMethods.JobObjectInfoClass.ExtendedLimitInformation,
                    pointer,
                    (uint)length))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    public void AddProcess(Process process)
    {
        if (_jobHandle == IntPtr.Zero || process.Handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.AssignProcessToJobObject(_jobHandle, process.Handle);
    }

    public void Dispose()
    {
        if (_jobHandle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
    }

    private static class NativeMethods
    {
        public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        public struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nuint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        public enum JobObjectInfoClass
        {
            ExtendedLimitInformation = 9
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr CreateJobObject(IntPtr attributes, string? name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            IntPtr job,
            JobObjectInfoClass infoClass,
            IntPtr info,
            uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
