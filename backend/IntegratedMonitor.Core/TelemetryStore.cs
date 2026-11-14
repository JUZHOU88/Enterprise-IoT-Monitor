using System.Text.RegularExpressions;

namespace IntegratedMonitor.Core;

public sealed record TelemetryInput(string DeviceId, string EventId, double? TemperatureC,
    bool DoorOpen, bool SensorOk, long UptimeMs);
public sealed record TelemetryReading(string DeviceId, string EventId, double? TemperatureC,
    bool DoorOpen, bool SensorOk, long UptimeMs, DateTimeOffset ReceivedAtUtc, bool Alert);
public sealed record DeviceSummary(string DeviceId, double? TemperatureC, bool DoorOpen,
    bool SensorOk, bool Alert, DateTimeOffset LastSeenUtc, int StoredReadings);
public sealed record IngestResult(TelemetryReading Reading, bool Created);

/// <summary>
/// Thread-safe, process-local store with bounded histories; not durable storage.
/// </summary>
public sealed class TelemetryStore
{
    private sealed class DeviceHistory
    {
        public readonly LinkedList<TelemetryReading> Readings = new();
        public readonly HashSet<string> EventIds = new(StringComparer.Ordinal);
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, DeviceHistory> _devices = new(StringComparer.Ordinal);
    private readonly int _maxDevices, _maxReadingsPerDevice;
    private readonly double _minimum, _maximum;
    private static readonly Regex Identifier =
        new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public TelemetryStore(int maxDevices = 100, int maxReadingsPerDevice = 200,
        double minimum = 18, double maximum = 28)
    {
        if (maxDevices < 1 || maxReadingsPerDevice < 1 ||
            !double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum >= maximum)
            throw new ArgumentException("Invalid store settings.");
        _maxDevices = maxDevices;
        _maxReadingsPerDevice = maxReadingsPerDevice;
        _minimum = minimum;
        _maximum = maximum;
    }

    public IngestResult Ingest(TelemetryInput input, DateTimeOffset? receivedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!Identifier.IsMatch(input.DeviceId ?? "") || !Identifier.IsMatch(input.EventId ?? ""))
            throw new ArgumentException("DeviceId and EventId must contain 1-64 safe characters.");
        if (input.UptimeMs < 0 || input.UptimeMs > 4_294_967_295L)
            throw new ArgumentException("UptimeMs is outside the supported range.");
        if (input.SensorOk && (!input.TemperatureC.HasValue ||
            !double.IsFinite(input.TemperatureC.Value) ||
            input.TemperatureC.Value < -55 || input.TemperatureC.Value > 125))
            throw new ArgumentException("Valid sensor readings require a temperature from -55 to 125 C.");
        if (!input.SensorOk && input.TemperatureC.HasValue)
            throw new ArgumentException("Fault readings must have a null temperature.");

        lock (_gate)
        {
            if (!_devices.TryGetValue(input.DeviceId, out var history))
            {
                if (_devices.Count >= _maxDevices)
                    throw new InvalidOperationException("Device limit reached.");
                history = new DeviceHistory();
                _devices.Add(input.DeviceId, history);
            }

            if (history.EventIds.Contains(input.EventId))
            {
                var prior = history.Readings.First(r => r.EventId == input.EventId);
                if (prior.TemperatureC != input.TemperatureC ||
                    prior.DoorOpen != input.DoorOpen || prior.SensorOk != input.SensorOk ||
                    prior.UptimeMs != input.UptimeMs)
                    throw new ArgumentException("An existing event ID cannot be reused with different data.");
                return new IngestResult(prior, Created: false);
            }

            bool alert = !input.SensorOk || input.DoorOpen ||
                input.TemperatureC < _minimum || input.TemperatureC > _maximum;
            var reading = new TelemetryReading(input.DeviceId, input.EventId, input.TemperatureC,
                input.DoorOpen, input.SensorOk, input.UptimeMs,
                receivedAtUtc ?? DateTimeOffset.UtcNow, alert);
            history.EventIds.Add(input.EventId);
            history.Readings.AddLast(reading);
            if (history.Readings.Count > _maxReadingsPerDevice)
            {
                history.EventIds.Remove(history.Readings.First!.Value.EventId);
                history.Readings.RemoveFirst();
            }
            return new IngestResult(reading, Created: true);
        }
    }

    public IReadOnlyList<DeviceSummary> GetDevices()
    {
        lock (_gate)
            return _devices.Select(pair =>
            {
                var latest = pair.Value.Readings.Last!.Value;
                return new DeviceSummary(pair.Key, latest.TemperatureC, latest.DoorOpen,
                    latest.SensorOk, latest.Alert, latest.ReceivedAtUtc, pair.Value.Readings.Count);
            }).OrderBy(d => d.DeviceId, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<TelemetryReading>? GetReadings(string deviceId, int limit = 50)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        lock (_gate)
        {
            if (!_devices.TryGetValue(deviceId, out var history)) return null;
            return history.Readings.Reverse().Take(limit).Reverse().ToArray();
        }
    }
}
