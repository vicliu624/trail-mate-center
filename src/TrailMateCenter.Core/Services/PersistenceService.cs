using Microsoft.Extensions.Logging;
using TrailMateCenter.Models;
using TrailMateCenter.Storage;

namespace TrailMateCenter.Services;

public sealed class PersistenceService
{
    private const int StartupEventHistoryLimit = 1200;
    private const int StartupLogHistoryLimit = 300;

    private readonly SqliteStore _store;
    private readonly SessionStore _sessionStore;
    private readonly LogStore _logStore;
    private readonly ILogger<PersistenceService> _logger;
    private bool _suppressWrites;
    private bool _started;

    public PersistenceService(
        SqliteStore store,
        SessionStore sessionStore,
        LogStore logStore,
        ILogger<PersistenceService> logger)
    {
        _store = store;
        _sessionStore = sessionStore;
        _logStore = logStore;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _store.InitializeAsync(cancellationToken);

        _suppressWrites = true;
        try
        {
            var messages = await _store.LoadMessagesAsync(cancellationToken);
            foreach (var message in messages)
                _sessionStore.AddMessage(message);

            var events = await _store.LoadEventsAsync(cancellationToken, takeLatest: StartupEventHistoryLimit);
            foreach (var ev in events)
                _sessionStore.AddEvent(ev);

            var tactical = await _store.LoadTacticalEventsAsync(cancellationToken);
            foreach (var ev in tactical)
                _sessionStore.AddTacticalEvent(ev);

            var positions = await _store.LoadPositionsAsync(cancellationToken);
            foreach (var position in positions)
                _sessionStore.AddPositionUpdate(position);

            var logs = await _store.LoadLogsAsync(cancellationToken, takeLatest: StartupLogHistoryLimit);
            foreach (var log in logs)
                _logStore.Add(log);

            var nodeInfos = await _store.LoadNodeInfosAsync(cancellationToken);
            foreach (var info in nodeInfos)
                _sessionStore.AddOrUpdateNodeInfo(info);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load persisted data");
        }
        finally
        {
            _suppressWrites = false;
        }
    }

    public void Start()
    {
        if (_started)
            return;
        _started = true;

        _sessionStore.MessageAdded += OnMessageAdded;
        _sessionStore.MessageUpdated += OnMessageUpdated;
        _sessionStore.EventAdded += OnEventAdded;
        _sessionStore.TacticalEventAdded += OnTacticalEventAdded;
        _sessionStore.PositionUpdated += OnPositionUpdated;
        _sessionStore.NodeInfoUpdated += OnNodeInfoUpdated;
        _logStore.EntryAdded += OnLogAdded;
    }

    private void OnMessageAdded(object? sender, MessageEntry message)
    {
        if (_suppressWrites)
            return;
        _ = SaveAsync(ct => _store.UpsertMessageAsync(message, ct));
    }

    private void OnMessageUpdated(object? sender, MessageEntry message)
    {
        if (_suppressWrites)
            return;
        _ = SaveAsync(ct => _store.UpsertMessageAsync(message, ct));
    }

    private void OnEventAdded(object? sender, HostLinkEvent ev)
    {
        if (_suppressWrites)
            return;
        _ = SaveAsync(ct => _store.AddEventAsync(ev, ct));
    }

    private void OnTacticalEventAdded(object? sender, TacticalEvent ev)
    {
        if (_suppressWrites)
            return;
        _ = SaveAsync(ct => _store.AddTacticalEventAsync(ev, ct));
    }

    private void OnPositionUpdated(object? sender, PositionUpdate update)
    {
        if (_suppressWrites)
            return;
        _ = SaveAsync(ct => _store.AddPositionAsync(update, ct));
    }

    private void OnLogAdded(object? sender, HostLinkLogEntry entry)
    {
        if (_suppressWrites)
            return;
        _ = SaveAsync(ct => _store.AddLogAsync(entry, ct));
    }

    private void OnNodeInfoUpdated(object? sender, NodeInfoUpdate info)
    {
        if (_suppressWrites)
            return;
        _ = SaveAsync(ct => _store.UpsertNodeInfoAsync(info, ct));
    }

    private async Task SaveAsync(Func<CancellationToken, Task> action)
    {
        try
        {
            await action(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist data");
        }
    }
}
