using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using OPNX.Lib.Data.ORM.Datas;
using OPNX.Lib.Data.ORM.Datas.Attributes;
using OPNX.Lib.Data.ORM.Enums;
using OPNX.Lib.Data.ORM.Interfaces;
using System.Data;
using System.Data.Common;

namespace OPNX.Lib.Data.ORM.Services
{
    public class MySQLDataBaseService(string connectionString, IEntityStore entityStore, ILogger? logger = null)
        : BaseDataBaseService(connectionString, entityStore, logger)
    {
        private readonly ILogger _logger = logger ?? NullLogger.Instance;

        #region Public Methods                
        public override string GetTableIdentifier(Type entityType) => $"`{DatabaseNaming.GetTableName(entityType).Replace("`", "``")}`";

        public override int ExecuteNonQuery(string sqlQuery, List<KeyValuePair<string, object>> paramList)
        {
            DbConnection? dbConnection = OpenDataBase();
            if (dbConnection != null)
            {
                try
                {
                    using MySqlCommand sqlCmd = new(sqlQuery, (MySqlConnection)dbConnection);
                    sqlCmd.CommandTimeout = CommandTimeout;

                    if (CurrentTransaction is MySqlTransaction myTx)
                        sqlCmd.Transaction = myTx;

                    if (paramList != null)
                    {
                        foreach (KeyValuePair<string, object> param in paramList)
                        {
                            sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                        }
                    }
                    return sqlCmd.ExecuteNonQuery();


                    //using (MySqlCommand sqlCmd = new(sqlQuery, (MySqlConnection)dbConnection))
                    //{
                    //    sqlCmd.CommandTimeout = CommandTimeout;

                    //    if (paramList != null)
                    //    {
                    //        foreach (KeyValuePair<string, object> param in paramList)
                    //        {
                    //            sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                    //        }
                    //    }
                    //    return sqlCmd.ExecuteNonQuery();
                    //}
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
                await using MySqlCommand sqlCmd = new(sqlQuery, (MySqlConnection)dbConnection) { CommandTimeout = CommandTimeout };
                if (CurrentTransaction is MySqlTransaction transaction)
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
                    using MySqlCommand sqlCmd = new(sqlQuery, (MySqlConnection)dbConnection);
                    sqlCmd.CommandTimeout = CommandTimeout;

                    if (CurrentTransaction is MySqlTransaction myTx)
                        sqlCmd.Transaction = myTx;

                    if (paramList != null)
                    {
                        foreach (KeyValuePair<string, object> param in paramList)
                        {
                            sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                        }
                    }

                    using MySqlDataReader reader = sqlCmd.ExecuteReader();
                    result = new DataTable();
                    result.Load(reader);



                    //using (MySqlCommand sqlCmd = new(sqlQuery, (MySqlConnection)dbConnection))
                    //{
                    //    sqlCmd.CommandTimeout = CommandTimeout;

                    //    if (paramList != null)
                    //    {
                    //        foreach (KeyValuePair<string, object> param in paramList)
                    //        {
                    //            sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                    //        }
                    //    }
                    //    using (MySqlDataReader reader = sqlCmd.ExecuteReader())
                    //    {
                    //        result = new DataTable();
                    //        result.Load(reader);
                    //    }
                    //}
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
                await using MySqlCommand sqlCmd = new(sqlQuery, (MySqlConnection)dbConnection) { CommandTimeout = CommandTimeout };
                if (CurrentTransaction is MySqlTransaction transaction)
                    sqlCmd.Transaction = transaction;
                foreach (KeyValuePair<string, object> param in paramList)
                    sqlCmd.Parameters.AddWithValue(param.Key, param.Value);
                await using MySqlDataReader reader = await sqlCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
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
                    using MySqlCommand sqlCmd = new(sqlQuery, (MySqlConnection)dbConnection);
                    sqlCmd.CommandTimeout = CommandTimeout;

                    if (CurrentTransaction is MySqlTransaction myTx)
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
                await using MySqlCommand sqlCmd = new(sqlQuery, (MySqlConnection)dbConnection) { CommandTimeout = CommandTimeout };
                if (CurrentTransaction is MySqlTransaction transaction)
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
        public override DatabaseType DBType => DatabaseType.MySQL;
        #endregion

        #region Private / Protected Methods       
        protected override DbConnection CreateConnection(string connectionString)
        {
            return new MySqlConnection(connectionString);
        }

        protected override string GetSqlQueryCommand<T>(DatabaseQueryType queryType, T entity, ref List<KeyValuePair<string, object>> paramList)
        {
            string tableName = GetTableIdentifier(typeof(T));
            System.Reflection.PropertyInfo idProperty = typeof(T).GetProperty(nameof(IEntity.ID))!;
            string idColumnName = QuoteMySqlIdent(DatabaseNaming.GetColumnName(idProperty));
            EntityColumnAttribute idAttribute = GetColumnAttribute(idProperty);

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
                            return $"INSERT INTO {tableName}() VALUES(); SELECT LAST_INSERT_ID();";

                        string columns = string.Join(",", props.Select(p => QuoteMySqlIdent(DatabaseNaming.GetColumnName(p))));
                        string values = string.Join(",", props.Select(p => $"@{p.Name}"));

                        foreach (var p in props)
                            AddParamIfMissing(paramList, p, entity);

                        // MySQL: AUTO_INCREMENT PK 반환
                        return idAttribute.IsIdentity
                            ? $"INSERT INTO {tableName}({columns}) VALUES({values}); SELECT LAST_INSERT_ID();"
                            : $"INSERT INTO {tableName}({columns}) VALUES({values}); SELECT @{idProperty.Name};";
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

                        // WHERE ID=@ID
                        object? idValue = typeof(T).GetProperty("ID")?.GetValue(entity);
                        paramList.Add(new KeyValuePair<string, object>("@ID", idValue ?? DBNull.Value));

                        if (props.Count == 0)
                            return $"UPDATE {tableName} SET {idColumnName}={idColumnName} WHERE {idColumnName}=@ID;";

                        string updates = string.Join(",", props.Select(p => $"{QuoteMySqlIdent(DatabaseNaming.GetColumnName(p))}=@{p.Name}"));
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
                // ForeignKey 규칙
                var attr = property.GetCustomAttributes(typeof(EntityColumnAttribute), false)
                    .Cast<EntityColumnAttribute>()
                    .FirstOrDefault();

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

            static string QuoteMySqlIdent(string ident)
            {
                // MySQL identifier quoting: `ident`
                return "`" + ident.Replace("`", "``") + "`";
            }
        }

        //protected override string GetSqlQueryCommand<T>(DataBaseQueryTypes queryType, T entity, ref List<KeyValuePair<string, object>> paramList)
        //{
        //    string result = string.Empty;
        //    string tableName = typeof(T).Name;
        //    string columns = string.Empty;
        //    string values = string.Empty;

        //    switch (queryType)
        //    {
        //        case DataBaseQueryTypes.Insert:
        //            {
        //                PropertyInfo[] propertyInfos = typeof(T).GetProperties();

        //                foreach (PropertyInfo propertyInfo in propertyInfos)
        //                {
        //                    if (!propertyInfo.CanWrite)
        //                        continue;

        //                    if (propertyInfo.Name.ToUpper() == "ID")
        //                        continue;

        //                    if (propertyInfo.IsDefined(typeof(EntityIgnore), true))
        //                        continue;

        //                    object propValue = propertyInfo.GetValue(entity, null);
        //                    if (propValue != null)
        //                    {
        //                        try
        //                        {
        //                            switch (Type.GetTypeCode(propValue.GetType()))
        //                            {
        //                                case TypeCode.String:
        //                                    {
        //                                        if (string.IsNullOrEmpty((string)propValue))
        //                                        {
        //                                            propValue = DBNull.Value;
        //                                        }
        //                                    }
        //                                    break;
        //                                case TypeCode.Int32:
        //                                    {
        //                                        if ((int)propValue < 0)
        //                                        {
        //                                            propValue = DBNull.Value;
        //                                        }
        //                                    }
        //                                    break;
        //                                case TypeCode.DateTime:
        //                                    {
        //                                        if ((DateTime)propValue <= DateTime.MinValue)
        //                                        {
        //                                            propValue = DBNull.Value;
        //                                        }
        //                                    }
        //                                    break;
        //                            }
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            _logger.LogError(ex, "{Message}", ex.Message);
        //                        }

        //                        columns = string.IsNullOrEmpty(columns) ? propertyInfo.Name : string.Format("{0},{1}", columns, propertyInfo.Name);
        //                        string parameter = string.Format("@{0}", propertyInfo.Name);
        //                        values = string.IsNullOrEmpty(values) ? parameter : string.Format("{0},{1}", values, parameter);

        //                        paramList.Add(new KeyValuePair<string, object>(parameter, propValue));
        //                    }
        //                }

        //                if (!string.IsNullOrEmpty(columns) && !string.IsNullOrEmpty(values))
        //                {
        //                    result = string.Format("INSERT INTO {0}({1}) VALUES({2})", tableName, columns, values);
        //                }
        //            }
        //            break;
        //        case DataBaseQueryTypes.Update:
        //            {
        //                string updates = string.Empty;
        //                string wheres = string.Format("ID={0}", entity.ID);

        //                PropertyInfo[] propertyInfos = typeof(T).GetProperties();

        //                foreach (PropertyInfo propertyInfo in propertyInfos)
        //                {
        //                    if (!propertyInfo.CanWrite)
        //                        continue;

        //                    if (propertyInfo.IsDefined(typeof(EntityIgnore), true))
        //                        continue;

        //                    object propValue = null;

        //                    switch (propertyInfo.Name)
        //                    {
        //                        case "ID":
        //                        case "InsertTime":
        //                            continue;
        //                        case "UpdateTime":
        //                            break;
        //                        default:
        //                            {
        //                                propValue = propertyInfo.GetValue(entity, null);

        //                                //if (dbDataColumn.IsForeignKey)
        //                                //{
        //                                //    int value = Convert.ToInt32(propValue);
        //                                //    if (value <= 0)
        //                                //    {
        //                                //        propValue = null;
        //                                //    }
        //                                //}
        //                            }
        //                            break;
        //                    }

        //                    string parameter = string.Format("@{0}", propertyInfo.Name);
        //                    updates = string.IsNullOrEmpty(updates) ? string.Format("{0}={1}", propertyInfo.Name, parameter) : updates + string.Format(" ,{0}={1}", propertyInfo.Name, parameter);

        //                    paramList.Add(new KeyValuePair<string, object>(parameter, propValue));

        //                }

        //                if (!string.IsNullOrEmpty(updates))
        //                {
        //                    result = string.Format("UPDATE {0} SET {1} WHERE {2}", tableName, updates, wheres);
        //                }                        
        //            }
        //            break;
        //        case DataBaseQueryTypes.Delete:
        //            {
        //                result = string.Format("DELETE FROM {0} WHERE ID={1}", typeof(T).Name, entity.ID);
        //            }
        //            break;
        //    }

        //    return result;
        //}
        #endregion
    }
}


