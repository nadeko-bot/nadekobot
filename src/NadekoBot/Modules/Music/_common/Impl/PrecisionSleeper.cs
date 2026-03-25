using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NadekoBot.Modules.Music;

internal abstract class PrecisionSleeper : IDisposable
{
    public abstract void SleepUntil(long targetStopwatchTicks);

    public virtual void Dispose()
    {
    }

    public static PrecisionSleeper Create()
    {
        if (OperatingSystem.IsLinux())
            return new LinuxSleeper();
        if (OperatingSystem.IsMacOS())
            return new MacOsSleeper();
        if (OperatingSystem.IsWindows())
            return new WindowsSleeper();
        return new FallbackSleeper();
    }

    // .NET on Linux hardcodes Stopwatch.Frequency = 1e9 and uses clock_gettime(CLOCK_MONOTONIC),
    // so Stopwatch ticks are CLOCK_MONOTONIC nanoseconds (see Stopwatch.Unix.cs in dotnet/runtime).
    private sealed class LinuxSleeper : PrecisionSleeper
    {
        private const int CLOCK_MONOTONIC = 1;
        private const int TIMER_ABSTIME = 1;
        private const int EINTR = 4;

        [StructLayout(LayoutKind.Sequential)]
        private struct Timespec
        {
            public long tv_sec;
            public long tv_nsec;
        }

        [DllImport("libc", EntryPoint = "clock_nanosleep", SetLastError = false)]
        private static extern int clock_nanosleep(int clockId, int flags, in Timespec request, IntPtr remain);

        public override void SleepUntil(long targetStopwatchTicks)
        {
            if (Stopwatch.GetTimestamp() >= targetStopwatchTicks)
                return;

            var ts = new Timespec
            {
                tv_sec = targetStopwatchTicks / 1_000_000_000L,
                tv_nsec = targetStopwatchTicks % 1_000_000_000L
            };

            while (clock_nanosleep(CLOCK_MONOTONIC, TIMER_ABSTIME, in ts, IntPtr.Zero) == EINTR)
            {
            }
        }
    }

    // .NET Stopwatch on macOS returns nanoseconds via clock_gettime_nsec_np(CLOCK_UPTIME_RAW).
    // mach_wait_until takes Mach absolute time ticks. On current Apple hardware numer/denom = 1/1,
    // but the conversion via mach_timebase_info is required by spec for correctness.
    private sealed class MacOsSleeper : PrecisionSleeper
    {
        [DllImport("libSystem.B.dylib")]
        private static extern int mach_wait_until(ulong deadline);

        [DllImport("libSystem.B.dylib")]
        private static extern int mach_timebase_info(out MachTimebaseInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct MachTimebaseInfo
        {
            public uint Numer;
            public uint Denom;
        }

        private readonly MachTimebaseInfo _timebase;

        public MacOsSleeper()
        {
            mach_timebase_info(out _timebase);
        }

        public override void SleepUntil(long targetStopwatchTicks)
        {
            if (Stopwatch.GetTimestamp() >= targetStopwatchTicks)
                return;

            // ns -> Mach absolute time ticks
            var machTicks = (ulong)targetStopwatchTicks * _timebase.Denom / _timebase.Numer;
            mach_wait_until(machTicks);
        }
    }

    // Uses SetWaitableTimer + WaitForSingleObject. CREATE_WAITABLE_TIMER_HIGH_RESOLUTION (Win10 1803+)
    // gives sub-ms precision; without it, timeBeginPeriod(1) provides ~1ms precision.
    private sealed class WindowsSleeper : PrecisionSleeper
    {
        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        private const uint TIMER_ALL_ACCESS = 0x1F0003;
        private const uint INFINITE = 0xFFFFFFFF;

        [DllImport("kernel32", EntryPoint = "CreateWaitableTimerExW", SetLastError = true)]
        private static extern IntPtr CreateWaitableTimerExW(
            IntPtr lpTimerAttributes, IntPtr lpTimerName, uint dwFlags, uint dwDesiredAccess);

        [DllImport("kernel32", SetLastError = true)]
        private static extern bool SetWaitableTimer(
            IntPtr hTimer, in long lpDueTime, int lPeriod,
            IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, bool fResume);

        [DllImport("kernel32")]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32")]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("winmm")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm")]
        private static extern uint timeEndPeriod(uint uPeriod);

        private readonly IntPtr _hTimer;
        private readonly bool _isHighRes;

        public WindowsSleeper()
        {
            // try high-resolution timer first (Win10 1803+)
            _hTimer = CreateWaitableTimerExW(
                IntPtr.Zero, IntPtr.Zero,
                CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                TIMER_ALL_ACCESS);

            if (_hTimer != IntPtr.Zero)
            {
                _isHighRes = true;
                return;
            }

            // fall back to standard waitable timer + 1ms scheduler resolution
            _hTimer = CreateWaitableTimerExW(
                IntPtr.Zero, IntPtr.Zero,
                0,
                TIMER_ALL_ACCESS);

            _isHighRes = false;
            timeBeginPeriod(1);
        }

        public override void SleepUntil(long targetStopwatchTicks)
        {
            var now = Stopwatch.GetTimestamp();
            if (now >= targetStopwatchTicks)
                return;

            // convert remaining Stopwatch ticks to 100ns units (negative = relative due time)
            var remainingTicks = targetStopwatchTicks - now;
            var dueTime = -(long)(remainingTicks * 10_000_000.0 / Stopwatch.Frequency);
            if (dueTime >= 0)
                return;

            SetWaitableTimer(_hTimer, in dueTime, 0, IntPtr.Zero, IntPtr.Zero, false);
            WaitForSingleObject(_hTimer, INFINITE);
        }

        public override void Dispose()
        {
            if (_hTimer != IntPtr.Zero)
                CloseHandle(_hTimer);

            if (!_isHighRes)
                timeEndPeriod(1);
        }
    }

    private sealed class FallbackSleeper : PrecisionSleeper
    {
        public override void SleepUntil(long targetStopwatchTicks)
        {
            while (true)
            {
                var remainingMs = (targetStopwatchTicks - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
                if (remainingMs <= 0)
                    return;

                if (remainingMs > 3.0)
                    Thread.Sleep((int)(remainingMs - 2.0));
                else
                    Thread.SpinWait(100);
            }
        }
    }
}
