namespace RangeVote2.Data
{
    public class BallotClosureService : IHostedService, IDisposable
    {
        private Timer? _timer;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BallotClosureService> _logger;

        public BallotClosureService(IServiceProvider serviceProvider, ILogger<BallotClosureService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Ballot Closure Service is starting.");

            // Run every hour
            _timer = new Timer(CheckAndCloseBallots, null, TimeSpan.Zero, TimeSpan.FromHours(1));

            return Task.CompletedTask;
        }

        private async void CheckAndCloseBallots(object? state)
        {
            _logger.LogInformation("Checking for ballots to auto-close...");

            try
            {
                using (var scope = _serviceProvider.CreateScope())
                {
                    var repo = scope.ServiceProvider.GetRequiredService<IRangeVoteRepository>();

                    var ballots = await repo.GetBallotsToAutoCloseAsync(DateTime.UtcNow);

                    _logger.LogInformation($"Found {ballots.Count} ballots to close.");

                    foreach (var ballot in ballots)
                    {
                        await repo.CloseBallotAsync(ballot.Id);
                        _logger.LogInformation($"Auto-closed ballot: {ballot.Name} (ID: {ballot.Id})");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while auto-closing ballots.");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Ballot Closure Service is stopping.");

            _timer?.Change(Timeout.Infinite, 0);

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}
