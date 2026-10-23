using System.Diagnostics;
using System.Runtime.InteropServices;

// Windows Job Object로 자식 프로세스를 묶습니다. API 프로세스 종료로 Job 핸들이 닫히면 연결된 Unity 프로세스도 종료되도록 설정합니다.
namespace Smart_Training_Server.Helper
{
    public class ProcessJobHelper
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        // Windows API를 호출해 프로세스를 묶을 Job Object 핸들을 만듭니다. IntPtr은 운영체제 자원 핸들을 담습니다.
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        // Job Object의 종료 제한 등 설정을 운영체제에 전달하는 네이티브 함수입니다.
        private static extern bool SetInformationJobObject(
            IntPtr hJob,
            int JobObjectInfoClass,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength
        );

        [DllImport("kernel32.dll", SetLastError = true)]
        // 지정 프로세스를 Job Object에 소속시키는 Windows 네이티브 함수입니다.
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        // 초기화에 실패하면 이미 생성한 Job 핸들을 해제합니다.
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        private static IntPtr jobHandle;

        // 첫 사용 시 한 번 Job을 만듭니다. 0x2000은 Job 핸들 종료 시 소속 프로세스를 종료하는 제한 플래그입니다.
        static ProcessJobHelper()
        {
            jobHandle = CreateJobObject(IntPtr.Zero, null);
            if (jobHandle == IntPtr.Zero)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = 0x2000
            };
            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = info
            };
            IntPtr extendedInfoPtr = IntPtr.Zero;
            try
            {
                extendedInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf(extendedInfo));
                Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);
                if (!SetInformationJobObject(jobHandle, 9, extendedInfoPtr, (uint)Marshal.SizeOf(extendedInfo)))
                {
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            catch
            {
                // 설정·메모리 준비에 실패한 경우 Job 핸들도 해제하고 원래 예외를 전달합니다.
                CloseHandle(jobHandle);
                jobHandle = IntPtr.Zero;
                throw;
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }

        // 새로 실행한 Unity 프로세스를 공통 Job Object에 넣습니다. 실패 시 Windows 오류를 예외로 전달합니다.
        public static void AddProcess(Process process)
        {
            if (!AssignProcessToJobObject(jobHandle, process.Handle))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        // Windows 기본 제한 구조체입니다. 타입과 필드 순서는 운영체제가 요구하는 메모리 배치에 맞춥니다.
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public int LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public int ActiveProcessLimit;
            public long Affinity;
            public int PriorityClass;
            public int SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        // Windows 확장 Job 정보가 요구하는 입출력 카운터 구조체입니다.
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        // Windows API에 전달할 기본 제한·입출력·메모리 제한 정보를 묶는 구조체입니다.
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
