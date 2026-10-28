using Microsoft.Data.Sqlite;

namespace VaultGuard.Common.Data;

/// <summary>
/// 轻量 SQLite 数据访问封装。系统一与系统二各使用一个独立的数据库文件，
/// 通过 <see cref="Database"/> 实例隔离，互不影响。
/// </summary>
public sealed class Database
{
    /// <summary>数据库文件的绝对路径。</summary>
    public string DbPath { get; }

    /// <summary>连接字符串。</summary>
    public string ConnectionString { get; }

    public Database(string dbPath)
    {
        DbPath = Path.GetFullPath(dbPath);
        var dir = Path.GetDirectoryName(DbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        ConnectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    /// <summary>打开一个连接（调用方负责释放）。</summary>
    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        using var pragma = conn.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        pragma.ExecuteNonQuery();
        return conn;
    }

    /// <summary>执行建表脚本（幂等）。</summary>
    public void EnsureSchema(string ddl)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = ddl;
        cmd.ExecuteNonQuery();
    }

    /// <summary>执行非查询语句，返回受影响行数。</summary>
    public int Execute(string sql, params SqlArg[] args)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, args);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>执行查询并把每一行映射为字典。</summary>
    public List<Dictionary<string, object?>> Query(string sql, params SqlArg[] args)
    {
        var rows = new List<Dictionary<string, object?>>();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, args);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>查询单行，无结果时返回 null。</summary>
    public Dictionary<string, object?>? QueryOne(string sql, params SqlArg[] args)
        => Query(sql, args).FirstOrDefault();

    /// <summary>执行插入并返回自增主键。</summary>
    public long Insert(string sql, params SqlArg[] args)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql + "; SELECT last_insert_rowid();";
        Bind(cmd, args);
        var result = cmd.ExecuteScalar();
        return Convert.ToInt64(result);
    }

    /// <summary>执行标量查询。</summary>
    public object? Scalar(string sql, params SqlArg[] args)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, args);
        return cmd.ExecuteScalar();
    }

    private static void Bind(SqliteCommand cmd, SqlArg[] args)
    {
        foreach (var arg in args)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = arg.Name;
            p.Value = arg.Value ?? DBNull.Value;
            cmd.Parameters.Add(p);
        }
    }
}

/// <summary>SQL 参数：名称 + 值。名称可带或省略 '@'。</summary>
public readonly struct SqlArg
{
    public string Name { get; }
    public object? Value { get; }

    public SqlArg(string name, object? value)
    {
        Name = name.StartsWith('@') ? name : "@" + name;
        Value = value;
    }

    /// <summary>便捷构造。</summary>
    public static SqlArg Of(string name, object? value) => new(name, value);
}
