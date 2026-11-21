using ChubbClaims.Messaging.Producers;
using ChubbClaims.Messaging.Publishers;
using ChubbClaims.Messaging.Settings;
using ChubbClaims.Repository.Interfaces;
using ChubbClaims.Repository.Outbox;
using ChubbClaims.Tests.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;
using static ChubbClaims.Tests.Domain.ClaimTestData;

namespace ChubbClaims.Tests.Messaging;

public class OutboxPublisherTests
{
    private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IEventProducer _producer = Substitute.For<IEventProducer>();

    private OutboxPublisher Publisher() => new(
        _outbox, _unitOfWork, _producer, new KafkaSettings { PublishBatchSize = 10 }, new FixedTimeProvider(Now),
        NullLogger<OutboxPublisher>.Instance);

    private static OutboxMessage Message(string type) => new(Guid.NewGuid(), type, Guid.NewGuid(), "{}", null, Now);

    [Fact]
    public async Task Publishes_in_order_and_marks_each_message()
    {
        var batch = new[] { Message("ClaimSubmitted"), Message("ClaimPickedUp") };
        _outbox.GetUnpublishedAsync(10, Arg.Any<CancellationToken>()).Returns(batch);

        var published = await Publisher().PublishPendingAsync(CancellationToken.None);

        published.ShouldBe(2);
        batch.ShouldAllBe(m => m.ProcessedAt == Now);
        Received.InOrder(() =>
        {
            _producer.PublishAsync(batch[0], Arg.Any<CancellationToken>());
            _producer.PublishAsync(batch[1], Arg.Any<CancellationToken>());
        });
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stops_at_the_first_failure_so_later_events_are_not_published_out_of_order()
    {
        var batch = new[] { Message("ClaimSubmitted"), Message("ClaimPickedUp"), Message("ClaimApproved") };
        _outbox.GetUnpublishedAsync(10, Arg.Any<CancellationToken>()).Returns(batch);
        _producer.PublishAsync(batch[1], Arg.Any<CancellationToken>()).ThrowsAsync(new Exception("broker unavailable"));

        var published = await Publisher().PublishPendingAsync(CancellationToken.None);

        published.ShouldBe(1);
        batch[0].ProcessedAt.ShouldBe(Now);
        (batch[1].ProcessedAt, batch[1].Attempts, batch[1].LastError).ShouldBe((null, 1, "broker unavailable"));
        (batch[2].ProcessedAt, batch[2].Attempts).ShouldBe((null, 0));
        await _producer.DidNotReceive().PublishAsync(batch[2], Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>()); // the progress so far is kept
    }

    [Fact]
    public async Task Nothing_to_publish_does_not_touch_the_database()
    {
        _outbox.GetUnpublishedAsync(10, Arg.Any<CancellationToken>()).Returns([]);

        (await Publisher().PublishPendingAsync(CancellationToken.None)).ShouldBe(0);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
