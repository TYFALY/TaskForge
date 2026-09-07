using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using TaskForge.Api.Services;
using TaskForge.Core;
using System.Text.Json;
using Xunit;

namespace TaskForge.Tests.Idempotency;

public class IdempotencyServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IDatabase> _dbMock;
    private readonly Mock<ILogger<IdempotencyService>> _loggerMock;

    public IdempotencyServiceTests()
    {
        _redisMock = new Mock<IConnectionMultiplexer>();
        _dbMock = new Mock<IDatabase>();
        _loggerMock = new Mock<ILogger<IdempotencyService>>();
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
    }

    [Fact]
    public async Task GetOrCachedJobIdAsync_WhenKeyExists_ReturnsCachedId()
    {
        // Arrange
        var key = "idem-123";
        var cachedId = Guid.NewGuid().ToString();
        _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(cachedId));

        var service = new IdempotencyService(_redisMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetOrCachedJobIdAsync(key, JobType.Webhook, () => Task.FromResult(Guid.NewGuid()));

        // Assert
        result.Should().Be(Guid.Parse(cachedId));
    }

    [Fact]
    public async Task GetOrCachedJobIdAsync_WhenKeyNotExists_CachesAndReturnsNewId()
    {
        // Arrange
        var key = "idem-new";
        var newId = Guid.NewGuid();
        
        // Setup to return Null first (key doesn't exist), then return the newId on second call (race condition result)
        var callCount = 0;
        _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(() => callCount++ == 0 ? RedisValue.Null : new RedisValue(newId.ToString()));
        _dbMock.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var service = new IdempotencyService(_redisMock.Object, _loggerMock.Object);

        // Act
        var result = await service.GetOrCachedJobIdAsync(key, JobType.Webhook, () => Task.FromResult(newId));

        // Assert
        result.Should().Be(newId);
        _dbMock.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString().Contains(key)),
            It.Is<RedisValue>(v => v.ToString() == newId.ToString()),
            TimeSpan.FromHours(24),
            When.NotExists), Times.Once);
    }

    [Fact]
    public async Task GetOrCachedJobIdAsync_WhenProviderThrows_PropagatesException()
    {
        // Arrange
        var key = "idem-error";
        _dbMock.Setup(d => d.StringGetAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var service = new IdempotencyService(_redisMock.Object, _loggerMock.Object);

        // Act
        var action = () => service.GetOrCachedJobIdAsync(key, JobType.Webhook, () => throw new InvalidOperationException("Boom"));

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>();
    }
}
