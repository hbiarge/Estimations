using System.Text.Json;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Npgsql;
using NpgsqlTypes;

namespace Acheve.Application.StateHolder
{
    // Keeps a copy of every case's state outside the process, so a restart doesn't forget the cases.
    public interface ICaseStateStore
    {
        public Task Initialize(CancellationToken cancellationToken);

        public Task Save(CaseSnapshot snapshot, CancellationToken cancellationToken);

        public Task<IReadOnlyList<CaseSnapshot>> LoadAll(CancellationToken cancellationToken);

        // One case, or null when this customer has no case with this ticket.
        public Task<CaseSnapshot?> Get(string ticket, string customerId, CancellationToken cancellationToken);

        // Deletes the cases that finished before the given time.
        public Task DeleteFinishedBefore(DateTimeOffset cutoff, CancellationToken cancellationToken);
    }

    public sealed class PostgresCaseStateStore(NpgsqlDataSource dataSource) : ICaseStateStore
    {
        private const string Table = Constants.Tables.CaseStates;

        public async Task Initialize(CancellationToken cancellationToken)
        {
            // Databases created before the client became a customer have a client_id column: rename it in place.
            await using var rename = dataSource.CreateCommand(
                $"""
             DO $$
             BEGIN
                 IF EXISTS (SELECT 1 FROM information_schema.columns
                            WHERE table_name = '{Table}' AND column_name = 'client_id') THEN
                     ALTER TABLE {Table} RENAME COLUMN client_id TO customer_id;
                 END IF;
             END $$
             """);

            await rename.ExecuteNonQueryAsync(cancellationToken);

            await using var command = dataSource.CreateCommand(
                $"""
             CREATE TABLE IF NOT EXISTS {Table} (
                 ticket            text        NOT NULL,
                 customer_id       text        NOT NULL,
                 state             integer     NOT NULL,
                 first_seen        timestamptz NOT NULL,
                 updated_at        timestamptz NOT NULL,
                 result            text        NULL,
                 failure_reason    text        NULL,
                 estimation_ticket text        NULL,
                 skipped_images    jsonb       NOT NULL,
                 PRIMARY KEY (ticket, customer_id)
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
                 (ticket, customer_id, state, first_seen, updated_at, result, failure_reason, estimation_ticket, skipped_images)
             VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
             ON CONFLICT (ticket, customer_id) DO UPDATE SET
                 state = EXCLUDED.state,
                 updated_at = EXCLUDED.updated_at,
                 result = EXCLUDED.result,
                 failure_reason = EXCLUDED.failure_reason,
                 estimation_ticket = EXCLUDED.estimation_ticket,
                 skipped_images = EXCLUDED.skipped_images
             WHERE {Table}.state < EXCLUDED.state
             """);

            command.Parameters.Add(new NpgsqlParameter { Value = snapshot.Ticket });
            command.Parameters.Add(new NpgsqlParameter { Value = snapshot.CustomerId });
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

        private const string Columns =
            "ticket, customer_id, state, first_seen, updated_at, result, failure_reason, estimation_ticket, skipped_images";

        public async Task<IReadOnlyList<CaseSnapshot>> LoadAll(CancellationToken cancellationToken)
        {
            await using var command = dataSource.CreateCommand($"SELECT {Columns} FROM {Table}");

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var cases = new List<CaseSnapshot>();

            while (await reader.ReadAsync(cancellationToken))
            {
                cases.Add(Read(reader));
            }

            return cases;
        }

        public async Task<CaseSnapshot?> Get(string ticket, string customerId, CancellationToken cancellationToken)
        {
            // The primary key is (ticket, customer_id): the customer id is part of the lookup, so a customer
            // can't read another customer's case by knowing its ticket.
            await using var command = dataSource.CreateCommand(
                $"SELECT {Columns} FROM {Table} WHERE ticket = $1 AND customer_id = $2");

            command.Parameters.Add(new NpgsqlParameter { Value = ticket });
            command.Parameters.Add(new NpgsqlParameter { Value = customerId });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
        }

        private static CaseSnapshot Read(NpgsqlDataReader reader) => new()
        {
            Ticket = reader.GetString(0),
            CustomerId = reader.GetString(1),
            State = (EstimationStates)reader.GetInt32(2),
            FirstSeen = reader.GetFieldValue<DateTimeOffset>(3),
            UpdatedAt = reader.GetFieldValue<DateTimeOffset>(4),
            Result = reader.IsDBNull(5) ? null : reader.GetString(5),
            FailureReason = reader.IsDBNull(6) ? null : reader.GetString(6),
            EstimationTicket = reader.IsDBNull(7) ? null : reader.GetString(7),
            SkippedImages = JsonSerializer.Deserialize<SkippedImage[]>(reader.GetString(8)) ?? []
        };

        public async Task DeleteFinishedBefore(DateTimeOffset cutoff, CancellationToken cancellationToken)
        {
            await using var command = dataSource.CreateCommand(
                $"DELETE FROM {Table} WHERE state >= $1 AND updated_at < $2");

            command.Parameters.Add(new NpgsqlParameter { Value = (int)EstimationStates.CustomerNotified });
            command.Parameters.Add(new NpgsqlParameter { Value = cutoff.ToUniversalTime() });

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
