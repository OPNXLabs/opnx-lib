using Npgsql;
using OPNX.Lib.Common.Logging;
using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.Enums;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Data;
using System.Data.Common;

namespace OPNX.Lib.Data.ORM.Services
{
    public class PostgreSQLDataBaseService(string connectionString, IEntityStore entityStore)
        : BaseDataBaseService(connectionString, entityStore)
    {
        #region Public Methods                
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
                    LogManager.Error(ex);

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
                    LogManager.Error(ex);

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
                    LogManager.Error(ex);

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
        #endregion

        #region Privte / Protected Methods
        protected override DbConnection CreateConnection(string connectionString)
        {
            return new NpgsqlConnection(connectionString);
        }

        protected override string GetSqlQueryCommand<T>(DataBaseQueryType queryType, T entity, ref List<KeyValuePair<string, object>> paramList)
        {
            string tableName = QuotePgIdent(typeof(T).Name);

            switch (queryType)
            {
                case DataBaseQueryType.Insert:
                    {
                        var props = typeof(T).GetProperties()
                            .Where(p =>
                                p.CanWrite &&
                                !string.Equals(p.Name, "ID", StringComparison.OrdinalIgnoreCase) &&
                                p.IsDefined(typeof(DataColumnAttribute), inherit: true))
                            .ToList();

                        if (props.Count == 0)
                            return $"INSERT INTO {tableName} DEFAULT VALUES RETURNING {QuotePgIdent("ID")};";

                        string columns = string.Join(",", props.Select(p => QuotePgIdent(p.Name)));
                        string values = string.Join(",", props.Select(p => $"@{p.Name}"));

                        foreach (var p in props)
                            AddParamIfMissing(paramList, p, entity);

                        // PostgreSQL: ID 반환
                        return $"INSERT INTO {tableName}({columns}) VALUES({values}) RETURNING {QuotePgIdent("ID")};";
                    }

                case DataBaseQueryType.Update:
                    {
                        var props = typeof(T).GetProperties()
                            .Where(p =>
                                p.CanWrite &&
                                !string.Equals(p.Name, "ID", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(p.Name, "InsertTime", StringComparison.OrdinalIgnoreCase) &&
                                p.IsDefined(typeof(DataColumnAttribute), inherit: true))
                            .ToList();

                        object? idValue = typeof(T).GetProperty("ID")?.GetValue(entity);
                        paramList.Add(new KeyValuePair<string, object>("@ID", idValue ?? DBNull.Value));

                        if (props.Count == 0)
                            return $"UPDATE {tableName} SET {QuotePgIdent("ID")}={QuotePgIdent("ID")} WHERE {QuotePgIdent("ID")}=@ID;";

                        string updates = string.Join(",", props.Select(p => $"{QuotePgIdent(p.Name)}=@{p.Name}"));
                        string wheres = $"{QuotePgIdent("ID")}=@ID";

                        foreach (var p in props)
                            AddParamIfMissing(paramList, p, entity);

                        return $"UPDATE {tableName} SET {updates} WHERE {wheres};";
                    }

                case DataBaseQueryType.Delete:
                    {
                        object? idValue = typeof(T).GetProperty("ID")?.GetValue(entity);
                        paramList.Add(new KeyValuePair<string, object>("@ID", idValue ?? DBNull.Value));

                        return $"DELETE FROM {tableName} WHERE {QuotePgIdent("ID")}=@ID;";
                    }

                default:
                    return string.Empty;
            }

            // ----------------- helpers -----------------

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
                var attr = property.GetCustomAttributes(typeof(DataColumnAttribute), false)
                    .Cast<DataColumnAttribute>()
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
