using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;

namespace Simple1C.Impl.Sql.SqlAccess
{
    internal abstract class AbstractSqlDatabase
    {
        private readonly int commandTimeout;
        public string ConnectionString { get; private set; }

        protected AbstractSqlDatabase(string connectionString, int commandTimeout = 100500)
        {
            this.commandTimeout = commandTimeout;
            ConnectionString = connectionString;
        }

        public int ExecuteInt(string commandText)
        {
            return ExecuteScalar<int>(commandText);
        }

        public long ExecuteLong(string commandText)
        {
            return ExecuteScalar<long>(commandText);
        }

        public bool ExecuteBool(string commandText)
        {
            return ExecuteScalar<bool>(commandText);
        }

        public decimal ExecuteDecimal(string commandText)
        {
            return ExecuteScalar<decimal>(commandText);
        }

        public string ExecuteString(string commandText)
        {
            return ExecuteScalar<string>(commandText);
        }

        protected TResult ExecuteScalar<TResult>(string commandText, params object[] args)
        {
            return ExecuteWithResult(commandText, args,
                c => (TResult) Convert.ChangeType(c.ExecuteScalar(), typeof(TResult)));
        }

        public bool Exists(string sql, params object[] args)
        {
            return ExecuteWithResult(sql, args, c =>
            {
                using (var reader = c.ExecuteReader())
                    return reader.Read();
            });
        }

        public void TruncateTable(string tableName)
        {
            ExecuteNonQuery(string.Format("truncate table {0}", tableName));
        }

        public void DropTable(string tableName)
        {
            ExecuteNonQuery(string.Format("drop table {0}", tableName));
        }

        public IEnumerable<T> ExecuteEnumerable<T>(string commandText, object[] args, Func<DbDataReader, T> map)
        {
            return ExecuteWithResult(commandText, args, c =>
            {
                using (var reader = c.ExecuteReader())
                    return ReadAll(reader, map).ToArray();
            });
        }

        private static IEnumerable<T> ReadAll<T>(DbDataReader reader, Func<DbDataReader, T> map)
        {
            while (reader.Read())
                yield return map(reader);
        }

        public void CreateTable(string tableName, params DataColumn[] columns)
        {
            var sqlBuilder = new StringBuilder();
            sqlBuilder.Append("CREATE TABLE ");
            sqlBuilder.Append(tableName);
            sqlBuilder.AppendLine();
            sqlBuilder.Append("(");
            for (var i = 0; i < columns.Length; i++)
            {
                sqlBuilder.AppendLine();
                sqlBuilder.Append('\t');
                var column = columns[i];
                sqlBuilder.Append(column.ColumnName);
                sqlBuilder.Append(' ');
                sqlBuilder.Append(GetSqlType(column));
                sqlBuilder.Append(column.AllowDBNull ? " NULL" : " NOT NULL");
                if (i != columns.Length - 1)
                    sqlBuilder.Append(',');
                sqlBuilder.AppendLine();
            }
            sqlBuilder.Append(")");
            ExecuteNonQuery(sqlBuilder.ToString());
        }

        public void ExecuteNonQuery(string commandText, params object[] parameters)
        {
            Execute(commandText, parameters, c => c.ExecuteNonQuery());
        }

        public TResult ExecuteWithResult<TResult>(string commandText, object[] parameters,
            Func<DbCommand, TResult> useCommand)
        {
            var result = default(TResult);
            Execute(commandText, parameters, c => result = useCommand(c));
            return result;
        }

        public void Execute(string commandText, object[] parameters, Action<DbCommand> useCommand)
        {
            var namedParameters = new Dictionary<string, object>();
            for (var i = 0; i < parameters.Length; i++)
                namedParameters.Add("@p" + i, parameters[i]);
            Execute(commandText, namedParameters, useCommand);
        }

        public void Execute(string commandText, Dictionary<string, object> parameters, Action<DbCommand> useCommand)
        {
            using (var connection = CreateConnection())
            {
                connection.ConnectionString = ConnectionString;
                connection.Open();
                using (var command = CreateCommand())
                {
                    if (parameters != null)
                        foreach (var parameter in parameters)
                            AddParameter(command, parameter.Key, parameter.Value);
                    command.CommandText = commandText;
                    command.CommandTimeout = commandTimeout;
                    command.Connection = connection;
                    useCommand(command);
                }
            }
        }

        protected abstract DbConnection CreateConnection();
        protected abstract DbCommand CreateCommand();
        protected abstract string GetSqlType(DataColumn column);
        protected abstract void AddParameter(DbCommand command, string name, object value);
    }
}