using Microsoft.Extensions.Options;
using Moq;
using t5f25sdprojectone_projectsplus.Services.ComsService;
using Xunit;

namespace t5f25sdprojectone_projectsplus.Tests
{
    public class CommQueueWorkerTests
    {
        // Tests/CommQueueWorkerTests.cs (xUnit + Moq)
        [Fact]
        public async Task Worker_MovesToDeadLetter_AfterMaxAttempts()
        {
            var repo = new Mock<INotificationRepository>();
            var center = new Mock<NotificationCenter>(/* dependencies can be null or mocks */);
            var audit = new Mock<ICommAuditStore>();
            var logger = new Mock<ILogger<CommQueueWorker>>();
            var options = Options.Create(new CommQueueWorkerOptions { MaxBatchSize = 1, MaxAttempts = 2, PollDelay = TimeSpan.FromMilliseconds(10), UseJitter = false });

            var note = new NotificationDto { NotificationId = Guid.NewGuid(), Recipient = "x", Channel = ChannelType.Email };
            repo.Setup(r => r.DequeuePendingAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(new List<NotificationDto> { note });

            // First call: failed attempt 1
            center.SetupSequence(c => c.SendNowAsync(It.IsAny<NotificationDto>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new SendResultDto { NotificationId = note.NotificationId, Status = SendStatus.Failed, Attempt = 1 })
                  .ReturnsAsync(new SendResultDto { NotificationId = note.NotificationId, Status = SendStatus.Failed, Attempt = 2 });

            var worker = new CommQueueWorker(logger.Object, repo.Object, center.Object, audit.Object, options);
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

            await worker.StartAsync(cts.Token);
            await Task.Delay(200, cts.Token); // allow loop to run a couple iterations
            await worker.StopAsync(CancellationToken.None);

            // Verify MoveToDeadLetterAsync called once when attempts >= MaxAttempts
            repo.Verify(r => r.MoveToDeadLetterAsync(note.NotificationId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            audit.Verify(a => a.RecordAsync(It.IsAny<CommunicationAuditEntryDto>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

    }
}
