using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VortexPad.Core.Engine;

public readonly record struct PollingTelemetry(
    double ActualRateHz,
    double LastTickDurationMicroseconds,
    double AverageJitterMilliseconds,
    long TotalFramesProcessed);

/// <summary>
/// High-precision 1000 Hz (1 ms) polling loop using Windows High-Resolution Waitable Timer
/// (CREATE_WAITABLE_TIMER_HIGH_RESOLUTION) + winmm timeBeginPeriod(1) + Stopwatch spin-wait hybrid.
/// Eliminates the ~15.6 ms Thread.Sleep(1) bottleneck present in legacy XOutput.
/// </summary>
public sealed class HighPrecisionPollingLoop : IDisposable
{
    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    private const uint TIMER_ALL_ACCESS = 0x001F0003;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWaitableTimerExW(
        IntPtr lpTimerAttributes,
        string? lpTimerName,
        uint dwFlags,
        uint dwDesiredAccess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetWaitableTimer(
        IntPtr hTimer,
        ref long pDueTime,
        int lPeriod,
        IntPtr pfnCompletionRoutine,
        IntPtr lpArgToCompletionRoutine,
        bool fResume);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint uPeriod);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint uPeriod);

    private readonly Action<double> _onTick;
    private Thread? _workerThread;
    private volatile bool _running;
    private int _targetHz = 1000;

    public PollingTelemetry LatestTelemetry { get; private set; }
    public event Action<PollingTelemetry>? TelemetryUpdated;

    public bool IsRunning => _running;

    public int TargetHz
    {
        get => _targetHz;
        set => _targetHz = Math.Clamp(value, 60, 2000);
    }

    public HighPrecisionPollingLoop(Action<double> onTick)
    {
        _onTick = onTick ?? throw new ArgumentNullException(nameof(onTick));
    }

    public void Start(int targetHz = 1000)
    {
        if (_running) return;
        TargetHz = targetHz;
        _running = true;

        _workerThread = new Thread(RunLoop)
        {
            Name = "VortexPad 1000Hz Polling Engine",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _workerThread.Start();
    }

    public void Stop()
    {
        _running = false;
        _workerThread?.Join(500);
        _workerThread = null;
    }

    private void RunLoop()
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        IntPtr waitableTimer = IntPtr.Zero;

        if (isWindows)
        {
            _ = timeBeginPeriod(1);
            waitableTimer = CreateWaitableTimerExW(
                IntPtr.Zero,
                null,
                CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
                TIMER_ALL_ACCESS);
        }

        var stopwatch = Stopwatch.StartNew();
        double tickFreq = Stopwatch.Frequency;
        long lastTicks = stopwatch.ElapsedTicks;
        long nextTargetTicks = lastTicks;

        long framesInWindow = 0;
        long totalFrames = 0;
        double windowElapsedSec = 0;
        double jitterSumMs = 0;

        try
        {
            while (_running)
            {
                int hz = _targetHz;
                double targetIntervalSec = 1.0 / hz;
                long intervalTicks = (long)(targetIntervalSec * tickFreq);
                nextTargetTicks += intervalTicks;

                long nowTicks = stopwatch.ElapsedTicks;
                long remainingTicks = nextTargetTicks - nowTicks;

                if (remainingTicks > 0)
                {
                    double remainingSeconds = remainingTicks / tickFreq;

                    // If more than 0.65 ms remains and we have a high-res waitable timer, sleep using OS waitable timer
                    if (waitableTimer != IntPtr.Zero && remainingSeconds > 0.00065)
                    {
                        // 100-nanosecond intervals, negative for relative time, leave 250us for final spin
                        long dueTime100Ns = -(long)((remainingSeconds - 0.00025) * 10_000_000.0);
                        if (dueTime100Ns < -1000)
                        {
                            SetWaitableTimer(waitableTimer, ref dueTime100Ns, 0, IntPtr.Zero, IntPtr.Zero, false);
                            WaitForSingleObject(waitableTimer, 2);
                        }
                    }

                    // Final sub-millisecond spin-wait for ultra-low jitter
                    while (stopwatch.ElapsedTicks < nextTargetTicks && _running)
                    {
                        Thread.SpinWait(16);
                    }
                }
                else if (remainingTicks < -intervalTicks * 4)
                {
                    // Resync if thread was delayed
                    nextTargetTicks = stopwatch.ElapsedTicks;
                }

                long tickStart = stopwatch.ElapsedTicks;
                double deltaSec = (tickStart - lastTicks) / tickFreq;
                lastTicks = tickStart;

                try
                {
                    _onTick(deltaSec);
                }
                catch (Exception ex)
                {
                    // Keep realtime loop alive even if a callback throws, but leave a trace (at most every 5 s)
                    VortexPad.Core.Diagnostics.VortexLog.ErrorThrottled(
                        "loop.tick", TimeSpan.FromSeconds(5), "Bucle1000Hz", "Excepción en el tick del bucle de polling.", ex);
                }

                long tickEnd = stopwatch.ElapsedTicks;
                double workDurationUs = ((tickEnd - tickStart) / tickFreq) * 1_000_000.0;
                double jitterMs = Math.Abs((deltaSec - targetIntervalSec) * 1000.0);

                totalFrames++;
                framesInWindow++;
                windowElapsedSec += deltaSec;
                jitterSumMs += jitterMs;

                if (windowElapsedSec >= 0.25)
                {
                    double actualHz = framesInWindow / Math.Max(0.001, windowElapsedSec);
                    double avgJitter = jitterSumMs / Math.Max(1, framesInWindow);

                    LatestTelemetry = new PollingTelemetry(
                        Math.Round(actualHz, 1),
                        Math.Round(workDurationUs, 1),
                        Math.Round(avgJitter, 3),
                        totalFrames);

                    TelemetryUpdated?.Invoke(LatestTelemetry);

                    framesInWindow = 0;
                    windowElapsedSec = 0;
                    jitterSumMs = 0;
                }
            }
        }
        finally
        {
            if (waitableTimer != IntPtr.Zero)
            {
                CloseHandle(waitableTimer);
            }
            if (isWindows)
            {
                _ = timeEndPeriod(1);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
