using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.Enums;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Data;
using System.Data.Common;

namespace OPNX.Lib.Data.ORM.Services
{
    public class PostgreSQLDataBaseService(string connectionString, IEntityStore entityStore, ILogger? logger = null)
        : BaseDataBaseService(connectionString, entityStore, logger)
    {
        private readonly ILogger _logger = logger ?? NullLogger.Instance;

        #region Public Methods                
        public override string GetTableIdentifier(Type entityType) => $"\"{DatabaseNaming.GetTableName(entityType).Replace("\"", "\"\"")}\"";

        public override int ExecuteNonQuery(string sqlQuery, List<KeyValuePair<string, object>> paramList)
        {
            DbConnection? dbConnection = OpenDataBase();
            if (dbConnection != null)
            {
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

                    if (CurrentTransaction != null)
                        throw;
                }
                finally
                {
                    CloseDataBase(dbConnection);
                }
            }
            return int.MinValue;
        }

        public override async Task<int> ExecuteNonQueryAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default)
        {
            DbConnection? dbConnection = await OpenDataBaseAsync(cancellationToken).ConfigureAwait(false);
            if (dbConnection == null)
                return int.MinValue;

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
                if (CurrentTransaction != null)
                    throw;
                return int.MinValue;
            }
            finally
            {
                await CloseDataBaseAsync(dbConnection).ConfigureAwait(false);
            }
        }

        public override DataTable? ExecuteReader(string sqlQuery, List<KeyValuePair<string, object>> paramList)
        {
            DbConnection? dbConnection = OpenDataBase();
            DataTable? result = null; // 기본값은 null로 설정

            if (dbConnection != null)
            {
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

                    if (CurrentTransaction != null)
                        throw;
                }
                finally
                {
                    CloseDataBase(dbConnection);
                }
            }

            return result;
        }

        public override async Task<DataTable?> ExecuteReaderAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default)
        {
            DbConnection? dbConnection = await OpenDataBaseAsync(cancellationToken).ConfigureAwait(false);
            if (dbConnection == null)
                return null;

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
                if (CurrentTransaction != null)
                    throw;
                return null;
            }
            finally
            {
                await CloseDataBaseAsync(dbConnection).ConfigureAwait(false);
            }
        }

        public override object? ExecuteScalar(string sqlQuery, List<KeyValuePair<string, object>> paramList)
        {
            DbConnection? dbConnection = OpenDataBase();

            if (dbConnection != null)
            {
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

                    if (CurrentTransaction != null)
                        throw;
                }
                finally
                {
                    CloseDataBase(dbConnection);
                }
            }

            return null;
        }

        public override async Task<object?> ExecuteScalarAsync(string sqlQuery, List<KeyValuePair<string, object>> paramList, CancellationToken cancellationToken = default)
        {
            DbConnection? dbConnection = await OpenDataBaseAsync(cancellationToken).ConfigureAwait(false);
            if (dbConnection == null)
                return null;

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
                if (CurrentTransaction != null)
                    throw;
                return null;
            }
            finally
            {
                await CloseDataBaseAsync(dbConnection).ConfigureAwait(false);
            }
        }
        #endregion

        #region Properties
        public override DatabaseType DBType => DatabaseType.PostgreSQL;
        #endregion

        #region Privte / Protected Methods
        protected override DbConnection CreateConnection(string connectionString)
        {
            return new NpgsqlConnection(connectionString);
        }

        protected override string GetSqlQueryCommand<T>(DatabaseQueryType queryType, T entity, ref List<KeyValuePair<string, object>> paramList)
        {
            string tableName = GetTableIdentifier(typeof(T));
            System.Reflection.PropertyInfo idProperty = typeof(T).GetProperty(nameof(IEntity.ID))!;
            string idColumnName = QuotePgIdent(DatabaseNaming.GetColumnName(idProperty));

            switch (queryType)
            {
                case DatabaseQueryType.Insert:
                    {
                        var props = typeof(T).GetProperties()
                            .Where(p =>
                                p.CanWrite &&
                                p.IsDefined(typeof(EntityColumnAttribute), inherit: true) &&
                                !GetColumnAttribute(p).IsIdentity &&
                                !GetColumnAttribute(p).IsReadOnly)
                            .ToList();

                        if (props.Count == 0)
                            return $"INSERT INTO {tableName} DEFAULT VALUES RETURNING {idColumnName};";

                        string columns = string.Join(",", props.Select(p => QuotePgIdent(DatabaseNaming.GetColumnName(p))));
                        string values = string.Join(",", props.Select(p => $"@{p.Name}"));

                        foreach (var p in props)
                            AddParamIfMissing(paramList, p, entity);

                        // PostgreSQL: ID 반환
                        return $"INSERT INTO {tableName}({columns}) VALUES({values}) RETURNING {idColumnName};";
                    }

                case DatabaseQueryType.Update:
                    {
                        var props = typeof(T).GetProperties()
                            .Where(p =>
                                p.CanWrite &&
                                p.IsDefined(typeof(EntityColumnAttribute), inherit: true) &&
                                !GetColumnAttribute(p).IsPrimaryKey &&
                                !GetColumnAttribute(p).IsIdentity &&
                                !GetColumnAttribute(p).IsReadOnly &&
                                !string.Equals(p.Name, "InsertTime", StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        object? idValue = typeof(T).GetProperty("ID")?.GetValue(entity);
                        paramList.Add(new KeyValuePair<string, object>("@ID", idValue ?? DBNull.Value));

                        if (props.Count == 0)
                            return $"UPDATE {tableName} SET {idColumnName}={idColumnName} WHERE {idColumnName}=@ID;";

                        string updates = string.Join(",", props.Select(p => $"{QuotePgIdent(DatabaseNaming.GetColumnName(p))}=@{p.Name}"));
                        string wheres = $"{idColumnName}=@ID";

                        foreach (var p in props)
                            AddParamIfMissing(paramList, p, entity);

                        return $"UPDATE {tableName} SET {updates} WHERE {wheres};";
                    }

                case DatabaseQueryType.Delete:
                    {
                        object? idValue = typeof(T).GetProperty("ID")?.GetValue(entity);
                        paramList.Add(new KeyValuePair<string, object>("@ID", idValue ?? DBNull.Value));

                        return $"DELETE FROM {tableName} WHERE {idColumnName}=@ID;";
                    }

                default:
                    return string.Empty;
            }

            // ----------------- helpers -----------------

            static EntityColumnAttribute GetColumnAttribute(System.Reflection.PropertyInfo property) => property.GetCustomAttributes(typeof(EntityColumnAttribute), true).Cast<EntityColumnAttribute>().First();

            static void AddParamIfMissing<TEnt>(List<KeyValuePair<string, object>> list, System.Reflection.PropertyInfo property, TEnt entity)
            {
                string paramName = $"@{property.Name}";
                if (list.Any(x => x.Key == paramName))
                    return;

                object? value = property.GetValue(entity);
                value = NormalizeValue(property, value);

                list.Add(new KeyValuePair<string, object>(paramName, value));
            }

            static object NormalizeValue(System.Reflection.PropertyInfo property, object? value)
            {
                var attr = property.GetCustomAttributes(typeof(EntityColumnAttribute), false)
                    .Cast<EntityColumnAttribute>()
                    .FirstOrDefault();

                // FK 규칙
                if (attr?.ForeignType != null && value is int fk && fk <= 0)
                    return DBNull.Value;

                // 공통 Null 규칙
                if (value == null)
                    return DBNull.Value;

                if (value is string s && string.IsNullOrEmpty(s))
                    return DBNull.Value;

                if (value is int i && i < 0)
                    return DBNull.Value;

                if (value is DateTime dt && dt <= DateTime.MinValue)
                    return DBNull.Value;

                if (value is Guid g && g == Guid.Empty)
                    return DBNull.Value;

                return value;
            }

            static string QuotePgIdent(string ident)
            {
                // PostgreSQL identifier quoting: "Ident"
                // 내부 "는 ""로 이스케이프
                return "\"" + ident.Replace("\"", "\"\"") + "\"";
            }
        }

        #endregion
    }
}


