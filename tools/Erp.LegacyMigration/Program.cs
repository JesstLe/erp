using Erp.LegacyMigration;

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

return await LegacyMigrationCli.RunAsync(args, Console.Out, cancellation.Token);
