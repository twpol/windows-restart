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

    static class Native
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint CallNtPowerInformation(PowerInformationLevel informationLevel, byte[] inputBuffer, int inputBufferSize, out ExecutionState outputBuffer, int outputBufferSize);
    }
}
