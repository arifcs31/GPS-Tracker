using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Telemax.Dev.DbMigrationTool.Interfaces;

namespace Telemax.Dev.DbMigrationTool;

/// <summary>
/// Implements application logic.
/// </summary>
public class App
{
    /// <summary>
    /// Logger.
    /// </summary>
    private readonly ILogger<App> _logger;

    /// <summary>
    /// Old DB data reader.
    /// </summary>
    private readonly IOldDataReader _oldDataReader;

    /// <summary>
    /// Old tracker records cache.
    /// </summary>
    private readonly IOldTrackerRecordsCache _oldTrackerRecordsCache;


    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="logger">Logger.</param>
    /// <param name="oldDataReader">Old DB data reader.</param>
    /// <param name="oldTrackerRecordsCache">Old tracker records cache.</param>
    public App(ILogger<App> logger, IOldDataReader oldDataReader, IOldTrackerRecordsCache oldTrackerRecordsCache)
    {
        _logger = logger;
        _oldDataReader = oldDataReader;
        _oldTrackerRecordsCache = oldTrackerRecordsCache;
    }


    /// <summary>
    /// Implements app execution logic.
    /// </summary>
    /// <returns></returns>
    public async Task ExecuteAsync(CancellationToken ct)
    {
        var minDate = DateTime.UtcNow - TimeSpan.FromDays(183);

        // Write old tracker records to cache.
        await _oldTrackerRecordsCache.CreateAsync(
            _oldDataReader
                .EnumerateTrackerRecordsAsync(ct)
                .Where(e => e.TrackerDate > minDate)
                .Take(10000000),
            ct
        );
    }
}