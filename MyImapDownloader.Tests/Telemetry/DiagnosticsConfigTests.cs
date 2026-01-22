using System.Diagnostics;
using System.Threading.Tasks;

using AwesomeAssertions;

using MyImapDownloader.Telemetry;

using TUnit.Assertions;
using TUnit.Assertions.Extensions;
using TUnit.Core;

namespace MyImapDownloader.Tests.Telemetry;

public class DiagnosticsConfigTests
{
    [Test]
    public async Task ServiceName_IsExpectedValue()
    {
        var serviceName = DiagnosticsConfig.ServiceName;
        await Assert.That(serviceName).IsEqualTo("MyImapDownloader");
    }

    [Test]
    public async Task ActivitySource_HasCorrectName()
    {
        var source = DiagnosticsConfig.ActivitySource;

        source.Should().NotBeNull();
        await Assert.That(source.Name).IsEqualTo("MyImapDownloader");
    }

    [Test]
    public async Task Meter_HasCorrectName()
    {
        var meter = DiagnosticsConfig.Meter;

        meter.Should().NotBeNull();
        await Assert.That(meter.Name).IsEqualTo("MyImapDownloader");
    }

    [Test]
    public async Task ActivitySource_CanCreateActivity()
    {
        // Need a listener to actually create activities
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == DiagnosticsConfig.ServiceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = DiagnosticsConfig.ActivitySource.StartActivity("TestOperation");

        activity.Should().NotBeNull();
        await Assert.That(activity!.OperationName).IsEqualTo("TestOperation");
    }

    [Test]
    public async Task Meter_CanCreateCounter()
    {
        var counter = DiagnosticsConfig.Meter.CreateCounter<long>("test_counter");

        counter.Should().NotBeNull();
        counter.Name.Should().Be("test_counter");
    }

    [Test]
    public async Task Meter_CanCreateHistogram()
    {
        var histogram = DiagnosticsConfig.Meter.CreateHistogram<double>("test_histogram");

        histogram.Should().NotBeNull();
        histogram.Name.Should().Be("test_histogram");
    }

    [Test]
    public async Task ActivitySource_IsSingleton()
    {
        var source1 = DiagnosticsConfig.ActivitySource;
        var source2 = DiagnosticsConfig.ActivitySource;

        var areSame = ReferenceEquals(source1, source2);
        await Assert.That(areSame).IsTrue();
    }

    [Test]
    public async Task Meter_IsSingleton()
    {
        var meter1 = DiagnosticsConfig.Meter;
        var meter2 = DiagnosticsConfig.Meter;

        var areSame = ReferenceEquals(meter1, meter2);
        await Assert.That(areSame).IsTrue();
    }
}
