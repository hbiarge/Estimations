using System.Text.Json;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Npgsql;
using NpgsqlTypes;

namespace Acheve.Application.StateHolder;

// Keeps a copy of every case's state outside the process, so a restart doesn't forget the cases.
public interface ICaseStateStore
{
    Task Initialize(CancellationToken cancellationToken);

    Task Save(CaseSnapshot snapshot, CancellationToken cancellationToken);

    Task<IReadOnlyList<CaseSnapshot>> LoadAll(CancellationToken cancellationToken);

    // Deletes the cases that finished before the given time.
    Task DeleteFinishedBefore(DateTimeOffset cutoff, CancellationToken cancellationToken);
}

public sealed class PostgresCaseStateStore(NpgsqlDataSource dataSource) : ICaseStateStore
{
    private const string Table = Constants.Tables.CaseStates;

    public async Task Initialize(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
             CREATE TABLE IF NOT EXISTS {Table} (
                 ticket            text        NOT NULL,
                 client_id         text        NOT NULL,
                 state             integer     NOT NULL,
                 first_seen        timestamptz NOT NULL,
                 updated_at        timestamptz NOT NULL,
                 result            text        NULL,
                 failure_reason    text        NULL,
                 estimation_ticket text        NULL,
                 skipped_images    jsonb       NOT NULL,
                 PRIMARY KEY (ticket, client_id)
             )
             """);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task Save(CaseSnapshot snapshot, CancellationToken cancellationToken)
    {
        // Like the state in memory, the row only moves forward: a lower or equal state changes nothing.
        await using var command = dataSource.CreateCommand(
            $"""
             INSERT INTO {Table}
                 (ticket, client_id, state, first_seen, updated_at, result, failure_reason, estimation_ticket, skipped_images)
             VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
             ON CONFLICT (ticket, client_id) DO UPDATE SET
                 state = EXCLUDED.state,
                 updated_at = EXCLUDED.updated_at,
                 result = EXCLUDED.result,
                 failure_reason = EXCLUDED.failure_reason,
                 estimation_ticket = EXCLUDED.estimation_ticket,
                 skipped_images = EXCLUDED.skipped_images
             WHERE {Table}.state < EXCLUDED.state
             """);

        command.Parameters.Add(new NpgsqlParameter { Value = snapshot.Ticket });
        command.Parameters.Add(new NpgsqlParameter { Value = snapshot.ClientId });
        command.Parameters.Add(new NpgsqlParameter { Value = (int)snapshot.State });
        command.Parameters.Add(new NpgsqlParameter { Value = snapshot.FirstSeen.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter { Value = snapshot.UpdatedAt.ToUniversalTime() });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)snapshot.Result ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)snapshot.FailureReason ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter { Value = (object?)snapshot.EstimationTicket ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text });
        command.Parameters.Add(new NpgsqlParameter
        {
            Value = JsonSerializer.Serialize(snapshot.SkippedImages),
            NpgsqlDbType = NpgsqlDbType.Jsonb
        });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CaseSnapshot>> LoadAll(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"""
             SELECT ticket, client_id, state, first_seen, updated_at, result, failure_reason, estimation_ticket, skipped_images
             FROM {Table}
             """);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var cases = new List<CaseSnapshot>();

        while (await reader.ReadAsync(cancellationToken))
        {
            cases.Add(new CaseSnapshot
            {
                Ticket = reader.GetString(0),
                ClientId = reader.GetString(1),
                State = (EstimationStates)reader.GetInt32(2),
                FirstSeen = reader.GetFieldValue<DateTimeOffset>(3),
                UpdatedAt = reader.GetFieldValue<DateTimeOffset>(4),
                Result = reader.IsDBNull(5) ? null : reader.GetString(5),
                FailureReason = reader.IsDBNull(6) ? null : reader.GetString(6),
                EstimationTicket = reader.IsDBNull(7) ? null : reader.GetString(7),
                SkippedImages = JsonSerializer.Deserialize<SkippedImage[]>(reader.GetString(8)) ?? []
            });
        }

        return cases;
    }

    public async Task DeleteFinishedBefore(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            $"DELETE FROM {Table} WHERE state >= $1 AND updated_at < $2");

        command.Parameters.Add(new NpgsqlParameter { Value = (int)EstimationStates.ClientNotified });
        command.Parameters.Add(new NpgsqlParameter { Value = cutoff.ToUniversalTime() });

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
