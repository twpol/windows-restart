using System;
using System.Runtime.InteropServices;
using Microsoft.Windows.Sdk;

namespace Windows_Restart
{
    [Flags]
    enum ExecutionState : long
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        UserPresent = 0x00000004,
        AwaymodeRequired = 0x00000040,
    }

    static class Native
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        public static extern uint CallNtPowerInformation(POWER_INFORMATION_LEVEL informationLevel, byte[] inputBuffer, int inputBufferSize, out ExecutionState outputBuffer, int outputBufferSize);
    }
}
