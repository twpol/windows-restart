using System;
using System.Runtime.InteropServices;

namespace Windows_Restart
{
    [Flags]
    enum ExecutionState : long
    {
        None = 0,
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        UserPresent = 0x00000004,
        AwaymodeRequired = 0x00000040,
        Continuous = 0x80000000,
    }

    enum PowerInformationLevel : int
    {
        SystemExecutionState = 16,
    }

    struct LastInputInfo
    {
        public int Size;
        public uint Time;

        public LastInputInfo(uint time)
        {
            Size = Marshal.SizeOf(typeof(LastInputInfo));
            Time = time;
        }
    }

    static class Native
    {
        [DllImport("kernel32", SetLastError = true)]
        public static extern uint GetTickCount();

        [DllImport("user32", SetLastError = true)]
        public static extern uint GetLastInputInfo(ref LastInputInfo lastInputInfo);

        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint CallNtPowerInformation(PowerInformationLevel informationLevel, byte[] inputBuffer, int inputBufferSize, out ExecutionState outputBuffer, int outputBufferSize);
    }
}
