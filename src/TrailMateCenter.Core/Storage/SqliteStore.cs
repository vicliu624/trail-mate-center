using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using TrailMateCenter.Models;

namespace TrailMateCenter.Storage;

public sealed class SqliteStore
{
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter()
        },
    };

    public SqliteStore(string dbPath)
    {
        _dbPath = dbPath;
        _connectionString = $"Data Source={_dbPath};Cache=Shared;Mode=ReadWriteCreate";
    }

    public static string GetDefaultPath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, "TrailMateCenter", "trailmate.db");
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=NORMAL;

            CREATE TABLE IF NOT EXISTS messages (
                id TEXT PRIMARY KEY,
                timestamp INTEGER NOT NULL,
                device_timestamp INTEGER,
                direction INTEGER NOT NULL,
                message_id INTEGER,
                from_id INTEGER,
                to_id INTEGER,
                from_text TEXT NOT NULL,
                to_text TEXT NOT NULL,
                channel_id INTEGER NOT NULL,
                channel TEXT NOT NULL,
                text TEXT NOT NULL,
                status INTEGER NOT NULL,
                error_message TEXT,
                rssi INTEGER,
                snr INTEGER,
                hop INTEGER,
                retry INTEGER,
                airtime_ms INTEGER,
                seq INTEGER NOT NULL,
                latitude REAL,
                longitude REAL,
                altitude REAL
            );

            CREATE INDEX IF NOT EXISTS idx_messages_timestamp ON messages(timestamp);

            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp INTEGER NOT NULL,
                event_type TEXT NOT NULL,
                payload TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events(timestamp);

            CREATE TABLE IF NOT EXISTS tactical_events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp INTEGER NOT NULL,
                severity INTEGER NOT NULL,
                kind INTEGER NOT NULL,
                title TEXT NOT NULL,
                detail TEXT NOT NULL,
                subject_id INTEGER,
                subject_label TEXT,
                latitude REAL,
                longitude REAL
            );

            CREATE INDEX IF NOT EXISTS idx_tactical_timestamp ON tactical_events(timestamp);

            CREATE TABLE IF NOT EXISTS positions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp INTEGER NOT NULL,
                source_id INTEGER NOT NULL,
                latitude REAL NOT NULL,
                longitude REAL NOT NULL,
                altitude REAL,
                accuracy REAL,
                source INTEGER NOT NULL,
                label TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_positions_timestamp ON positions(timestamp);

            CREATE TABLE IF NOT EXISTS logs (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp INTEGER NOT NULL,
                level INTEGER NOT NULL,
                message TEXT NOT NULL,
                raw_code TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_logs_timestamp ON logs(timestamp);

            CREATE TABLE IF NOT EXISTS node_infos (
                node_id INTEGER PRIMARY KEY,
                short_name TEXT,
                long_name TEXT,
                user_id TEXT,
                channel INTEGER,
                last_heard INTEGER NOT NULL,
                latitude REAL,
                longitude REAL,
                altitude REAL
            );

            CREATE TABLE IF NOT EXISTS mqtt_sources (
                id TEXT PRIMARY KEY,
                enabled INTEGER NOT NULL,
                name TEXT NOT NULL,
                host TEXT NOT NULL,
                port INTEGER NOT NULL,
                username TEXT,
                password TEXT,
                topic TEXT NOT NULL,
                use_tls INTEGER NOT NULL,
                client_id TEXT,
                clean_session INTEGER NOT NULL DEFAULT 0,
                subscribe_qos INTEGER NOT NULL DEFAULT 1,
                sort_order INTEGER NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_mqtt_sources_sort ON mqtt_sources(sort_order);

            CREATE TABLE IF NOT EXISTS mqtt_packets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                timestamp INTEGER NOT NULL,
                source_id TEXT NOT NULL,
                source_name TEXT NOT NULL,
                topic TEXT NOT NULL,
                qos INTEGER NOT NULL,
                retain INTEGER NOT NULL,
                payload BLOB NOT NULL,
                payload_size INTEGER NOT NULL,
                payload_sha256 TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_mqtt_packets_timestamp ON mqtt_packets(timestamp);
            CREATE INDEX IF NOT EXISTS idx_mqtt_packets_source_topic ON mqtt_packets(source_id, topic);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await EnsureColumnAsync(connection, "messages", "device_timestamp", "INTEGER", cancellationToken);
        await EnsureColumnAsync(connection, "mqtt_sources", "client_id", "TEXT", cancellationToken);
        await EnsureColumnAsync(connection, "mqtt_sources", "clean_session", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
        await EnsureColumnAsync(connection, "mqtt_sources", "subscribe_qos", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
    }

    public async Task UpsertMessageAsync(MessageEntry message, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO messages (
                    id, timestamp, device_timestamp, direction, message_id, from_id, to_id, from_text, to_text, channel_id, channel,
                    text, status, error_message, rssi, snr, hop, retry, airtime_ms, seq, latitude, longitude, altitude
                )
                VALUES (
                    $id, $timestamp, $device_timestamp, $direction, $message_id, $from_id, $to_id, $from_text, $to_text, $channel_id, $channel,
                    $text, $status, $error_message, $rssi, $snr, $hop, $retry, $airtime_ms, $seq, $latitude, $longitude, $altitude
                )
                ON CONFLICT(id) DO UPDATE SET
                    timestamp = excluded.timestamp,
                    device_timestamp = excluded.device_timestamp,
                    direction = excluded.direction,
                    message_id = excluded.message_id,
                    from_id = excluded.from_id,
                    to_id = excluded.to_id,
                    from_text = excluded.from_text,
                    to_text = excluded.to_text,
                    channel_id = excluded.channel_id,
                    channel = excluded.channel,
                    text = excluded.text,
                    status = excluded.status,
                    error_message = excluded.error_message,
                    rssi = excluded.rssi,
                    snr = excluded.snr,
                    hop = excluded.hop,
                    retry = excluded.retry,
                    airtime_ms = excluded.airtime_ms,
                    seq = excluded.seq,
                    latitude = excluded.latitude,
                    longitude = excluded.longitude,
                    altitude = excluded.altitude;
                """;

            cmd.Parameters.AddWithValue("$id", message.Id.ToString());
            cmd.Parameters.AddWithValue("$timestamp", message.Timestamp.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$device_timestamp", DbValue(message.DeviceTimestamp?.ToUnixTimeMilliseconds()));
            cmd.Parameters.AddWithValue("$direction", (int)message.Direction);
            cmd.Parameters.AddWithValue("$message_id", DbValue(message.MessageId));
            cmd.Parameters.AddWithValue("$from_id", DbValue(message.FromId));
            cmd.Parameters.AddWithValue("$to_id", DbValue(message.ToId));
            cmd.Parameters.AddWithValue("$from_text", message.From);
            cmd.Parameters.AddWithValue("$to_text", message.To);
            cmd.Parameters.AddWithValue("$channel_id", message.ChannelId);
            cmd.Parameters.AddWithValue("$channel", message.Channel);
            cmd.Parameters.AddWithValue("$text", message.Text);
            cmd.Parameters.AddWithValue("$status", (int)message.Status);
            cmd.Parameters.AddWithValue("$error_message", DbValue(message.ErrorMessage));
            cmd.Parameters.AddWithValue("$rssi", DbValue(message.Rssi));
            cmd.Parameters.AddWithValue("$snr", DbValue(message.Snr));
            cmd.Parameters.AddWithValue("$hop", DbValue(message.Hop));
            cmd.Parameters.AddWithValue("$retry", DbValue(message.Retry));
            cmd.Parameters.AddWithValue("$airtime_ms", DbValue(message.AirtimeMs));
            cmd.Parameters.AddWithValue("$seq", message.Seq);
            cmd.Parameters.AddWithValue("$latitude", DbValue(message.Latitude));
            cmd.Parameters.AddWithValue("$longitude", DbValue(message.Longitude));
            cmd.Parameters.AddWithValue("$altitude", DbValue(message.Altitude));

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task AddEventAsync(HostLinkEvent ev, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO events (timestamp, event_type, payload) VALUES ($timestamp, $event_type, $payload);";
            cmd.Parameters.AddWithValue("$timestamp", ev.Timestamp.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$event_type", ev.GetType().Name);
            cmd.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(ev, ev.GetType(), JsonOptions));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task AddTacticalEventAsync(TacticalEvent ev, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO tactical_events (
                    timestamp, severity, kind, title, detail, subject_id, subject_label, latitude, longitude
                ) VALUES (
                    $timestamp, $severity, $kind, $title, $detail, $subject_id, $subject_label, $latitude, $longitude
                );
                """;
            cmd.Parameters.AddWithValue("$timestamp", ev.Timestamp.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$severity", (int)ev.Severity);
            cmd.Parameters.AddWithValue("$kind", (int)ev.Kind);
            cmd.Parameters.AddWithValue("$title", ev.Title);
            cmd.Parameters.AddWithValue("$detail", ev.Detail);
            cmd.Parameters.AddWithValue("$subject_id", DbValue(ev.SubjectId));
            cmd.Parameters.AddWithValue("$subject_label", DbValue(ev.SubjectLabel));
            cmd.Parameters.AddWithValue("$latitude", DbValue(ev.Latitude));
            cmd.Parameters.AddWithValue("$longitude", DbValue(ev.Longitude));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task AddPositionAsync(PositionUpdate update, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO positions (
                    timestamp, source_id, latitude, longitude, altitude, accuracy, source, label
                ) VALUES (
                    $timestamp, $source_id, $latitude, $longitude, $altitude, $accuracy, $source, $label
                );
                """;
            cmd.Parameters.AddWithValue("$timestamp", update.Timestamp.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$source_id", update.SourceId);
            cmd.Parameters.AddWithValue("$latitude", update.Latitude);
            cmd.Parameters.AddWithValue("$longitude", update.Longitude);
            cmd.Parameters.AddWithValue("$altitude", DbValue(update.AltitudeMeters));
            cmd.Parameters.AddWithValue("$accuracy", DbValue(update.AccuracyMeters));
            cmd.Parameters.AddWithValue("$source", (int)update.Source);
            cmd.Parameters.AddWithValue("$label", DbValue(update.Label));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task AddLogAsync(HostLinkLogEntry entry, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO logs (timestamp, level, message, raw_code) VALUES ($timestamp, $level, $message, $raw_code);";
            cmd.Parameters.AddWithValue("$timestamp", entry.Timestamp.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$level", (int)entry.Level);
            cmd.Parameters.AddWithValue("$message", entry.Message);
            cmd.Parameters.AddWithValue("$raw_code", DbValue(entry.RawCode));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task AddMqttPacketAsync(MqttPacketRecord packet, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO mqtt_packets (
                    timestamp, source_id, source_name, topic, qos, retain, payload, payload_size, payload_sha256
                ) VALUES (
                    $timestamp, $source_id, $source_name, $topic, $qos, $retain, $payload, $payload_size, $payload_sha256
                );
                """;
            cmd.Parameters.AddWithValue("$timestamp", packet.Timestamp.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$source_id", packet.SourceId);
            cmd.Parameters.AddWithValue("$source_name", packet.SourceName);
            cmd.Parameters.AddWithValue("$topic", packet.Topic);
            cmd.Parameters.AddWithValue("$qos", Math.Clamp(packet.Qos, 0, 2));
            cmd.Parameters.AddWithValue("$retain", packet.Retain ? 1 : 0);
            cmd.Parameters.AddWithValue("$payload", packet.Payload);
            cmd.Parameters.AddWithValue("$payload_size", packet.Payload.Length);
            cmd.Parameters.AddWithValue("$payload_sha256", packet.PayloadSha256);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<MessageEntry>> LoadMessagesAsync(CancellationToken cancellationToken)
    {
        var results = new List<MessageEntry>();
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM messages ORDER BY timestamp ASC;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var message = new MessageEntry
                {
                    Id = Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("timestamp"))),
                    DeviceTimestamp = ReadNullableLong(reader, "device_timestamp") is { } deviceTs
                        ? DateTimeOffset.FromUnixTimeMilliseconds(deviceTs)
                        : null,
                    Direction = (MessageDirection)reader.GetInt32(reader.GetOrdinal("direction")),
                    MessageId = ReadNullableUInt(reader, "message_id"),
                    FromId = ReadNullableUInt(reader, "from_id"),
                    ToId = ReadNullableUInt(reader, "to_id"),
                    From = reader.GetString(reader.GetOrdinal("from_text")),
                    To = reader.GetString(reader.GetOrdinal("to_text")),
                    ChannelId = (byte)reader.GetInt32(reader.GetOrdinal("channel_id")),
                    Channel = reader.GetString(reader.GetOrdinal("channel")),
                    Text = reader.GetString(reader.GetOrdinal("text")),
                    Status = (MessageDeliveryStatus)reader.GetInt32(reader.GetOrdinal("status")),
                    ErrorMessage = ReadNullableString(reader, "error_message"),
                    Rssi = ReadNullableInt(reader, "rssi"),
                    Snr = ReadNullableInt(reader, "snr"),
                    Hop = ReadNullableInt(reader, "hop"),
                    Retry = ReadNullableInt(reader, "retry"),
                    AirtimeMs = ReadNullableInt(reader, "airtime_ms"),
                    Seq = (ushort)reader.GetInt32(reader.GetOrdinal("seq")),
                    Latitude = ReadNullableDouble(reader, "latitude"),
                    Longitude = ReadNullableDouble(reader, "longitude"),
                    Altitude = ReadNullableDouble(reader, "altitude"),
                };
                results.Add(message);
            }
        }
        finally
        {
            _mutex.Release();
        }

        return results;
    }

    public async Task UpsertNodeInfoAsync(NodeInfoUpdate info, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO node_infos (
                    node_id, short_name, long_name, user_id, channel, last_heard, latitude, longitude, altitude
                ) VALUES (
                    $node_id, $short_name, $long_name, $user_id, $channel, $last_heard, $latitude, $longitude, $altitude
                )
                ON CONFLICT(node_id) DO UPDATE SET
                    short_name = excluded.short_name,
                    long_name = excluded.long_name,
                    user_id = excluded.user_id,
                    channel = excluded.channel,
                    last_heard = excluded.last_heard,
                    latitude = excluded.latitude,
                    longitude = excluded.longitude,
                    altitude = excluded.altitude;
                """;
            cmd.Parameters.AddWithValue("$node_id", info.NodeId);
            cmd.Parameters.AddWithValue("$short_name", DbValue(info.ShortName));
            cmd.Parameters.AddWithValue("$long_name", DbValue(info.LongName));
            cmd.Parameters.AddWithValue("$user_id", DbValue(info.UserId));
            cmd.Parameters.AddWithValue("$channel", DbValue(info.Channel));
            cmd.Parameters.AddWithValue("$last_heard", info.LastHeard.ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$latitude", DbValue(info.Latitude));
            cmd.Parameters.AddWithValue("$longitude", DbValue(info.Longitude));
            cmd.Parameters.AddWithValue("$altitude", DbValue(info.AltitudeMeters));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    public async Task<IReadOnlyList<NodeInfoUpdate>> LoadNodeInfosAsync(CancellationToken cancellationToken)
    {
        var results = new List<NodeInfoUpdate>();
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM node_infos ORDER BY last_heard ASC;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var info = new NodeInfoUpdate(
                    (uint)reader.GetInt64(reader.GetOrdinal("node_id")),
                    ReadNullableString(reader, "short_name"),
                    ReadNullableString(reader, "long_name"),
                    ReadNullableString(reader, "user_id"),
                    ReadNullableByte(reader, "channel"),
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("last_heard"))),
                    ReadNullableDouble(reader, "latitude"),
                    ReadNullableDouble(reader, "longitude"),
                    ReadNullableDouble(reader, "altitude"));
                results.Add(info);
            }
        }
        finally
        {
            _mutex.Release();
        }

        return results;
    }

    public async Task<IReadOnlyList<HostLinkEvent>> LoadEventsAsync(CancellationToken cancellationToken)
    {
        var results = new List<HostLinkEvent>();
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT event_type, payload FROM events ORDER BY timestamp ASC;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var eventType = reader.GetString(0);
                var payload = reader.GetString(1);
                var ev = DeserializeEvent(eventType, payload);
                if (ev is not null)
                    results.Add(ev);
            }
        }
        finally
        {
            _mutex.Release();
        }

        return results;
    }

    public async Task<IReadOnlyList<TacticalEvent>> LoadTacticalEventsAsync(CancellationToken cancellationToken)
    {
        var results = new List<TacticalEvent>();
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM tactical_events ORDER BY timestamp ASC;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var ev = new TacticalEvent(
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("timestamp"))),
                    (TacticalSeverity)reader.GetInt32(reader.GetOrdinal("severity")),
                    (TacticalEventKind)reader.GetInt32(reader.GetOrdinal("kind")),
                    reader.GetString(reader.GetOrdinal("title")),
                    reader.GetString(reader.GetOrdinal("detail")),
                    ReadNullableUInt(reader, "subject_id"),
                    ReadNullableString(reader, "subject_label"),
                    ReadNullableDouble(reader, "latitude"),
                    ReadNullableDouble(reader, "longitude"));
                results.Add(ev);
            }
        }
        finally
        {
            _mutex.Release();
        }

        return results;
    }

    public async Task<IReadOnlyList<PositionUpdate>> LoadPositionsAsync(CancellationToken cancellationToken)
    {
        var results = new List<PositionUpdate>();
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM positions ORDER BY timestamp ASC;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var update = new PositionUpdate(
                    DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("timestamp"))),
                    (uint)reader.GetInt64(reader.GetOrdinal("source_id")),
                    reader.GetDouble(reader.GetOrdinal("latitude")),
                    reader.GetDouble(reader.GetOrdinal("longitude")),
                    ReadNullableDouble(reader, "altitude"),
                    ReadNullableDouble(reader, "accuracy"),
                    (PositionSource)reader.GetInt32(reader.GetOrdinal("source")),
                    ReadNullableString(reader, "label"));
                results.Add(update);
            }
        }
        finally
        {
            _mutex.Release();
        }

        return results;
    }

    public async Task<IReadOnlyList<HostLinkLogEntry>> LoadLogsAsync(CancellationToken cancellationToken)
    {
        var results = new List<HostLinkLogEntry>();
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM logs ORDER BY timestamp ASC;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(new HostLinkLogEntry
                {
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("timestamp"))),
                    Level = (Microsoft.Extensions.Logging.LogLevel)reader.GetInt32(reader.GetOrdinal("level")),
                    Message = reader.GetString(reader.GetOrdinal("message")),
                    RawCode = ReadNullableString(reader, "raw_code"),
                });
            }
        }
        finally
        {
            _mutex.Release();
        }

        return results;
    }

    public async Task<IReadOnlyList<MeshtasticMqttSourceSettings>> LoadMqttSourcesAsync(CancellationToken cancellationToken)
    {
        var results = new List<MeshtasticMqttSourceSettings>();
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT * FROM mqtt_sources ORDER BY sort_order ASC;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var source = new MeshtasticMqttSourceSettings
                {
                    Id = reader.GetString(reader.GetOrdinal("id")),
                    Enabled = reader.GetInt32(reader.GetOrdinal("enabled")) != 0,
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    Host = reader.GetString(reader.GetOrdinal("host")),
                    Port = reader.GetInt32(reader.GetOrdinal("port")),
                    Username = ReadNullableString(reader, "username") ?? string.Empty,
                    Password = ReadNullableString(reader, "password") ?? string.Empty,
                    Topic = reader.GetString(reader.GetOrdinal("topic")),
                    UseTls = reader.GetInt32(reader.GetOrdinal("use_tls")) != 0,
                    ClientId = ReadNullableString(reader, "client_id") ?? string.Empty,
                    CleanSession = reader.GetInt32(reader.GetOrdinal("clean_session")) != 0,
                    SubscribeQos = Math.Clamp(reader.GetInt32(reader.GetOrdinal("subscribe_qos")), 0, 2),
                };
                results.Add(source);
            }
        }
        finally
        {
            _mutex.Release();
        }

        return results;
    }

    public async Task SaveMqttSourcesAsync(IEnumerable<MeshtasticMqttSourceSettings> sources, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = "DELETE FROM mqtt_sources;";
                await clear.ExecuteNonQueryAsync(cancellationToken);
            }

            var sortOrder = 0;
            foreach (var source in sources)
            {
                await using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO mqtt_sources (
                        id, enabled, name, host, port, username, password, topic, use_tls, client_id, clean_session, subscribe_qos, sort_order
                    ) VALUES (
                        $id, $enabled, $name, $host, $port, $username, $password, $topic, $use_tls, $client_id, $clean_session, $subscribe_qos, $sort_order
                    );
                    """;
                insert.Parameters.AddWithValue("$id", source.Id);
                insert.Parameters.AddWithValue("$enabled", source.Enabled ? 1 : 0);
                insert.Parameters.AddWithValue("$name", source.Name ?? string.Empty);
                insert.Parameters.AddWithValue("$host", source.Host ?? string.Empty);
                insert.Parameters.AddWithValue("$port", source.Port);
                insert.Parameters.AddWithValue("$username", DbValue(source.Username));
                insert.Parameters.AddWithValue("$password", DbValue(source.Password));
                insert.Parameters.AddWithValue("$topic", source.Topic ?? string.Empty);
                insert.Parameters.AddWithValue("$use_tls", source.UseTls ? 1 : 0);
                insert.Parameters.AddWithValue("$client_id", DbValue(source.ClientId));
                insert.Parameters.AddWithValue("$clean_session", source.CleanSession ? 1 : 0);
                insert.Parameters.AddWithValue("$subscribe_qos", Math.Clamp(source.SubscribeQos, 0, 2));
                insert.Parameters.AddWithValue("$sort_order", sortOrder);
                await insert.ExecuteNonQueryAsync(cancellationToken);
                sortOrder++;
            }

            await transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private static object DbValue<T>(T? value)
    {
        return value is null ? DBNull.Value : value;
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var name = reader.GetString(reader.GetOrdinal("name"));
            if (string.Equals(name, column, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static long? ReadNullableLong(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static uint? ReadNullableUInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : (uint)reader.GetInt64(ordinal);
    }

    private static int? ReadNullableInt(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static byte? ReadNullableByte(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : (byte)reader.GetInt32(ordinal);
    }

    private static double? ReadNullableDouble(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
    }

    private static string? ReadNullableString(SqliteDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static HostLinkEvent? DeserializeEvent(string eventType, string payload)
    {
        return eventType switch
        {
            nameof(RxMessageEvent) => JsonSerializer.Deserialize<RxMessageEvent>(payload, JsonOptions),
            nameof(TxResultEvent) => JsonSerializer.Deserialize<TxResultEvent>(payload, JsonOptions),
            nameof(StatusEvent) => JsonSerializer.Deserialize<StatusEvent>(payload, JsonOptions),
            nameof(LogEvent) => JsonSerializer.Deserialize<LogEvent>(payload, JsonOptions),
            nameof(GpsEvent) => JsonSerializer.Deserialize<GpsEvent>(payload, JsonOptions),
            nameof(AppDataEvent) => JsonSerializer.Deserialize<AppDataEvent>(payload, JsonOptions),
            nameof(ConfigEvent) => JsonSerializer.Deserialize<ConfigEvent>(payload, JsonOptions),
            _ => null,
        };
    }
}
