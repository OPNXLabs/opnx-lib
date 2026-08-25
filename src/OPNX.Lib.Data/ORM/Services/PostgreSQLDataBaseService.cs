using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Enums;
using OPNX.Lib.Data.ORM.Generators;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Data;
using System.Data.Common;

namespace OPNX.Lib.Data.ORM.Services
{
    public class PostgreSQLDataBaseService : BaseDataBaseService
    {
        public PostgreSQLDataBaseService(string connectionString, IEntityStore entityStore, ILogger? logger = null) : base(connectionString, entityStore, logger) => _logger = logger ?? NullLogger.Instance;
        public PostgreSQLDataBaseService(string connectionString, bool useEntityStore, ILogger? logger = null) : base(connectionString, useEntityStore, logger) => _logger = logger ?? NullLogger.Instance;

        private readonly ILogger _logger;

        #region Public Methods                
        public override string GetTableIdentifier(Type entityType)
        {
            string tableName = DatabaseNaming.GetTableName(entityType).ToLowerInvariant();
            return $"\"{tableName.Replace("\"", "\"\"")}\"";
        }

        public override int ExecuteNonQuery(string sqlQuery, List<KeyValuePair<string, object>> paramList)
        {
            DbConnection? dbConnection = OpenDataBase();
            if (dbConnection == null)
                throw new InvalidOperationException("Failed to open the PostgreSQL database connection.");

            try
            {
                using NpgsqlCommand sqlCmd = new(sqlQuery, (NpgsqlConnection)dbConnection);
                sqlCmd.CommandTimeout = CommandTimeout;

                if (CurrentTransaction is NpgsqlTransaction myTx)
                    sqlCmd.Transaction = myTx;

                if (paramList != null)
                {
                    foreach (KeyValuePair<string, object> param in paramList)
                    {
                        sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                    }
                }
                return sqlCmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                throw;
            }
            finally
            {
                CloseDataBase(dbConnection);
            }
        }

        public override async Task<int> ExecuteNonQueryAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default)
        {
            DbConnection? dbConnection = await OpenDataBaseAsync(cancellationToken).ConfigureAwait(false);
            if (dbConnection == null)
                throw new InvalidOperationException("Failed to open the PostgreSQL database connection.");

            try
            {
                await using NpgsqlCommand sqlCmd = new(sqlQuery, (NpgsqlConnection)dbConnection) { CommandTimeout = CommandTimeout };
                if (CurrentTransaction is NpgsqlTransaction transaction)
                    sqlCmd.Transaction = transaction;
                foreach (KeyValuePair<string, object> param in paramList)
                    sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                return await sqlCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                throw;
            }
            finally
            {
                await CloseDataBaseAsync(dbConnection).ConfigureAwait(false);
            }
        }

        public override DataTable? ExecuteReader(string sqlQuery, List<KeyValuePair<string, object>> paramList)
        {
            DbConnection? dbConnection = OpenDataBase();
            if (dbConnection == null)
                throw new InvalidOperationException("Failed to open the PostgreSQL database connection.");
            DataTable? result = null; // 기본값은 null로 설정

            try
            {
                using NpgsqlCommand sqlCmd = new(sqlQuery, (NpgsqlConnection)dbConnection);
                sqlCmd.CommandTimeout = CommandTimeout;

                if (CurrentTransaction is NpgsqlTransaction myTx)
                    sqlCmd.Transaction = myTx;

                if (paramList != null)
                {
                    foreach (KeyValuePair<string, object> param in paramList)
                    {
                        sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                    }
                }

                using NpgsqlDataReader reader = sqlCmd.ExecuteReader();
                result = new DataTable();
                result.Load(reader);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                throw;
            }
            finally
            {
                CloseDataBase(dbConnection);
            }

            return result;
        }

        public override async Task<DataTable?> ExecuteReaderAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default)
        {
            DbConnection? dbConnection = await OpenDataBaseAsync(cancellationToken).ConfigureAwait(false);
            if (dbConnection == null)
                throw new InvalidOperationException("Failed to open the PostgreSQL database connection.");

            try
            {
                await using NpgsqlCommand sqlCmd = new(sqlQuery, (NpgsqlConnection)dbConnection) { CommandTimeout = CommandTimeout };
                if (CurrentTransaction is NpgsqlTransaction transaction)
                    sqlCmd.Transaction = transaction;
                foreach (KeyValuePair<string, object> param in paramList)
                    sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                await using NpgsqlDataReader reader = await sqlCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                return await ReadDataTableAsync(reader, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                throw;
            }
            finally
            {
                await CloseDataBaseAsync(dbConnection).ConfigureAwait(false);
            }
        }

        public override object? ExecuteScalar(string sqlQuery, List<KeyValuePair<string, object>> paramList)
        {
            DbConnection? dbConnection = OpenDataBase();
            if (dbConnection == null)
                throw new InvalidOperationException("Failed to open the PostgreSQL database connection.");

            try
            {
                using NpgsqlCommand sqlCmd = new(sqlQuery, (NpgsqlConnection)dbConnection);
                sqlCmd.CommandTimeout = CommandTimeout;

                if (CurrentTransaction is NpgsqlTransaction myTx)
                    sqlCmd.Transaction = myTx;

                if (paramList != null)
                {
                    foreach (KeyValuePair<string, object> param in paramList)
                    {
                        sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                    }
                }

                return sqlCmd.ExecuteScalar();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                throw;
            }
            finally
            {
                CloseDataBase(dbConnection);
            }
        }

        public override async Task<object?> ExecuteScalarAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default)
        {
            DbConnection? dbConnection = await OpenDataBaseAsync(cancellationToken).ConfigureAwait(false);
            if (dbConnection == null)
                throw new InvalidOperationException("Failed to open the PostgreSQL database connection.");

            try
            {
                await using NpgsqlCommand sqlCmd = new(sqlQuery, (NpgsqlConnection)dbConnection) { CommandTimeout = CommandTimeout };
                if (CurrentTransaction is NpgsqlTransaction transaction)
                    sqlCmd.Transaction = transaction;
                foreach (KeyValuePair<string, object> param in paramList)
                    sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                return await sqlCmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Message}", ex.Message);
                throw;
            }
            finally
            {
                await CloseDataBaseAsync(dbConnection).ConfigureAwait(false);
            }
        }
        #endregion

        #region Properties
        public override DatabaseType DBType => DatabaseType.PostgreSQL;
        public override IEntitySqlGenerator SqlGenerator { get; } = new PostgreSqlEntitySqlGenerator();
        #endregion

        #region Privte / Protected Methods
        protected override DbConnection CreateConnection(string connectionString)
        {
            return new NpgsqlConnection(connectionString);
        }

        protected override async Task<int> ExecuteBulkInsertChunkAsync<T>(IReadOnlyList<T> entities, int offset, int count, IReadOnlyList<System.Reflection.PropertyInfo> properties, CancellationToken cancellationToken)
        {
            string tableName = GetTableIdentifier(typeof(T));
            string columns = string.Join(",", properties.Select(property => $"\"{DatabaseNaming.GetColumnName(property).Replace("\"", "\"\"")}\""));
            List<KeyValuePair<string, object>> parameters = new(count * properties.Count);
            string[] rows = new string[count];

            for (int row = 0; row < count; row++)
            {
                T entity = entities[offset + row];
                ArgumentNullException.ThrowIfNull(entity);
                string[] values = new string[properties.Count];
                for (int column = 0; column < properties.Count; column++)
                {
                    var property = properties[column];
                    string parameterName = $"@p{row}_{column}";
                    values[column] = parameterName;
                    parameters.Add(new(parameterName, NormalizeBulkInsertValue(property, property.GetValue(entity))));
                }
                rows[row] = $"({string.Join(",", values)})";
            }

            string sql = $"INSERT INTO {tableName}({columns}) VALUES {string.Join(",", rows)};";
            return await ExecuteNonQueryAsync(sql, parameters, cancellationToken).ConfigureAwait(false);
        }

        #endregion
    }
}


