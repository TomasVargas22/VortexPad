using VortexPad.Core.Models;

namespace VortexPad.AI;

public enum DriftCalibrationStatus
{
    Idle,
    Recording,
    Completed,
    Cancelled,
    /// <summary>A stick was moved during the measurement: the result would not represent the resting state.</summary>
    MovementDetected
}

/// <summary>
/// Records resting stick samples over a real time window (e.g. 2 s at 1000 Hz)
/// to measure drift and noise, instead of a handful of instantaneous readings.
///
/// Threads: <see cref="Start"/>, <see cref="Cancel"/> and <see cref="BuildResult"/> from the UI;
/// <see cref="AddSample"/> from a single producer (the polling loop), with no allocations.
/// </summary>
public sealed class DriftCalibrationRecorder
{
    private readonly double[] _lx;
    private readonly double[] _ly;
    private readonly double[] _rx;
    private readonly double[] _ry;
    private readonly double _movementThreshold;

    private int _count;
    private double _elapsedSeconds;
    private volatile int _status = (int)DriftCalibrationStatus.Idle;

    public double DurationSeconds { get; }

    /// <param name="durationSeconds">Measurement window.</param>
    /// <param name="maxRateHz">Maximum expected rate (sizes the buffers; past that, recording ends early).</param>
    /// <param name="movementThreshold">If a stick goes past this radius, the controller is not at rest.</param>
    public DriftCalibrationRecorder(double durationSeconds = 2.0, int maxRateHz = 2000, double movementThreshold = 0.35)
    {
        if (durationSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        DurationSeconds = durationSeconds;
        _movementThreshold = movementThreshold;

        int capacity = (int)Math.Ceiling(durationSeconds * Math.Max(60, maxRateHz));
        _lx = new double[capacity];
        _ly = new double[capacity];
        _rx = new double[capacity];
        _ry = new double[capacity];
    }

    public DriftCalibrationStatus Status => (DriftCalibrationStatus)_status;
    public bool IsRecording => Status == DriftCalibrationStatus.Recording;
    public int SampleCount => Volatile.Read(ref _count);
    public double Progress => Math.Clamp(Volatile.Read(ref _elapsedSeconds) / DurationSeconds, 0.0, 1.0);

    /// <summary>Starts a new measurement. Ignored if one is already running.</summary>
    public bool Start()
    {
        if (IsRecording) return false;
        Volatile.Write(ref _count, 0);
        Volatile.Write(ref _elapsedSeconds, 0.0);
        _status = (int)DriftCalibrationStatus.Recording;
        return true;
    }

    public void Cancel()
    {
        if (IsRecording) _status = (int)DriftCalibrationStatus.Cancelled;
    }

    /// <summary>Producer: adds one sample. Does nothing if no measurement is running.</summary>
    public void AddSample(PhysicalInputState state, double deltaSeconds)
    {
        if (_status != (int)DriftCalibrationStatus.Recording) return;

        double lr = Math.Sqrt((state.LX * state.LX) + (state.LY * state.LY));
        double rr = Math.Sqrt((state.RX * state.RX) + (state.RY * state.RY));
        if (lr > _movementThreshold || rr > _movementThreshold)
        {
            _status = (int)DriftCalibrationStatus.MovementDetected;
            return;
        }

        int i = _count;
        _lx[i] = state.LX;
        _ly[i] = state.LY;
        _rx[i] = state.RX;
        _ry[i] = state.RY;
        Volatile.Write(ref _count, i + 1);

        double elapsed = _elapsedSeconds + Math.Max(0.0, deltaSeconds);
        Volatile.Write(ref _elapsedSeconds, elapsed);

        if (elapsed >= DurationSeconds || i + 1 >= _lx.Length)
        {
            _status = (int)DriftCalibrationStatus.Completed;
        }
    }

    /// <summary>Computes the result once <see cref="Status"/> is <see cref="DriftCalibrationStatus.Completed"/>; null otherwise.</summary>
    public DriftCalibrationResult? BuildResult(SmartMapperAndCalibrator calibrator)
    {
        if (Status != DriftCalibrationStatus.Completed) return null;
        int n = SampleCount;
        return calibrator.CalibrateStickDrift(_lx.AsSpan(0, n), _ly.AsSpan(0, n), _rx.AsSpan(0, n), _ry.AsSpan(0, n));
    }
}
