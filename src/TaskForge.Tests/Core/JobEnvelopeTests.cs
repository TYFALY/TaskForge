using FluentAssertions;
using TaskForge.Core;
using Xunit;

namespace TaskForge.Tests.Core;

public class JobEnvelopeTests
{
    [Fact]
    public void Create_WithValidInput_CreatesJobEnvelope()
    {
        // Arrange
        var queueName = "test-queue";
        var payload = "{\"data\":\"test\"}";

        // Act
        var job = JobEnvelope.Create(queueName, payload);

        // Assert
        job.Id.Should().NotBeEmpty();
        job.QueueName.Should().Be(queueName);
        job.Payload.Should().Be(payload);
        job.Status.Should().Be(JobStatus.Queued);
        job.CurrentRetry.Should().Be(0);
        job.MaxRetries.Should().Be(3);
        job.EnqueuedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Create_WithCustomMaxRetries_UsesSpecifiedValue()
    {
        var job = JobEnvelope.Create("queue", "{}", maxRetries: 5);
        job.MaxRetries.Should().Be(5);
    }

    [Fact]
    public void Create_WithNamespaceAndTenant_SetsPropertiesCorrectly()
    {
        var job = JobEnvelope.Create(
            queueName: "q",
            payload: "{}",
            @namespace: "production",
            tenantId: "tenant-123"
        );

        job.Namespace.Should().Be("production");
        job.TenantId.Should().Be("tenant-123");
    }

    [Theory]
    [InlineData(JobType.Webhook)]
    [InlineData(JobType.Scheduled)]
    [InlineData(JobType.Default)]
    public void Create_WithDifferentJobTypes_SetsTypeCorrectly(JobType jobType)
    {
        var job = JobEnvelope.Create("q", "{}", jobType: jobType);
        job.JobType.Should().Be(jobType);
    }

    [Fact]
    public void WithRetryCount_ReturnsNewInstanceWithIncrementedRetry()
    {
        var original = JobEnvelope.Create("q", "{}");
        var updated = original with { CurrentRetry = 1 };

        updated.Id.Should().Be(original.Id);
        updated.CurrentRetry.Should().Be(1);
        original.CurrentRetry.Should().Be(0);
    }

    [Fact]
    public void JobStatus_HasExpectedValues()
    {
        Enum.GetValues<JobStatus>().Should().Contain(JobStatus.Queued);
        Enum.GetValues<JobStatus>().Should().Contain(JobStatus.Processing);
        Enum.GetValues<JobStatus>().Should().Contain(JobStatus.Completed);
        Enum.GetValues<JobStatus>().Should().Contain(JobStatus.Failed);
        Enum.GetValues<JobStatus>().Should().Contain(JobStatus.DeadLettered);
    }
}
