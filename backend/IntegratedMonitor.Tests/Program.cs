using IntegratedMonitor.Core;

var cases = new (string Name, Action Run)[] {
    ("valid sample is stored", Stored),
    ("identical retry is idempotent", Duplicate),
    ("conflicting retry is rejected", Conflict),
    ("out-of-range and door alerts", Alerts),
    ("sensor fault requires null temperature", Fault),
    ("history retention is bounded", Bound),
    ("concurrent event retries create once", Concurrent),
    ("device identifiers are validated", Identifier)
};
int failures = 0;
foreach (var (name, run) in cases)
{
    try { run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine("FAIL " + name + ": " + ex.Message);
    }
}
Console.WriteLine($"{cases.Length - failures}/{cases.Length} tests passed");
return failures == 0 ? 0 : 1;

static TelemetryInput Input(string evt = "boot-a-1", double? temp = 22.0,
    bool door = false, bool ok = true, string device = "esp32-demo") =>
    new(device, evt, temp, door, ok, 100);
static void Check(bool value)
{
    if (!value) throw new Exception("Check failed");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
static void Stored()
{
    var store = new TelemetryStore();
    var result = store.Ingest(Input());
    Check(result.Created && !result.Reading.Alert &&
        store.GetDevices().Count == 1 && store.GetReadings("esp32-demo")!.Count == 1);
}
static void Duplicate()
{
    var store = new TelemetryStore();
    var first = store.Ingest(Input());
    var retry = store.Ingest(Input());
    Check(first.Created && !retry.Created && first.Reading == retry.Reading &&
        store.GetReadings("esp32-demo")!.Count == 1);
}
static void Conflict()
{
    var store = new TelemetryStore();
    store.Ingest(Input());
    Throws<ArgumentException>(() => store.Ingest(Input(temp: 24)));
}
static void Alerts()
{
    var store = new TelemetryStore();
    Check(store.Ingest(Input("e1", 30)).Reading.Alert);
    Check(store.Ingest(Input("e2", 22, true)).Reading.Alert);
    Check(!store.Ingest(Input("e3", 22)).Reading.Alert);
}
static void Fault()
{
    var store = new TelemetryStore();
    Check(store.Ingest(Input("e1", null, false, false)).Reading.Alert);
    Throws<ArgumentException>(() => store.Ingest(Input("e2", 20, false, false)));
}
static void Bound()
{
    var store = new TelemetryStore(maxReadingsPerDevice: 2);
    store.Ingest(Input("e1"));
    store.Ingest(Input("e2"));
    store.Ingest(Input("e3"));
    Check(store.GetReadings("esp32-demo")!.Count == 2);
    Check(store.GetReadings("esp32-demo")![0].EventId == "e2");
}
static void Concurrent()
{
    var store = new TelemetryStore();
    var results = new IngestResult[40];
    Parallel.For(0, results.Length, i => results[i] = store.Ingest(Input()));
    Check(results.Count(r => r.Created) == 1 &&
        store.GetReadings("esp32-demo")!.Count == 1);
}
static void Identifier()
{
    var store = new TelemetryStore();
    Throws<ArgumentException>(() => store.Ingest(Input(device: "../bad")));
}
