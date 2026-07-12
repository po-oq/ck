using CodeKnowledge.Core.Domain;
using CodeKnowledge.Core.Projects;
using CodeKnowledge.Infrastructure.Database;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Infrastructure.Stores;

public sealed class SqliteProjectStore(SqliteConnectionFactory factory) : IProjectStore
{
    public Project? FindById(string projectId)
        => Query("SELECT * FROM projects WHERE project_id = @key;", projectId);

    public Project? FindByRepositoryRoot(string repositoryRoot)
        => Query(
            "SELECT * FROM projects WHERE repository_root = @key COLLATE NOCASE;",
            repositoryRoot);

    public void Upsert(Project project)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects (project_id, display_name, repository_root, remote_url, created_at, updated_at)
            VALUES (@id, @name, @root, @remote, @created, @updated)
            ON CONFLICT (project_id) DO UPDATE SET
                display_name = excluded.display_name,
                repository_root = excluded.repository_root,
                remote_url = excluded.remote_url,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("@id", project.ProjectId);
        command.Parameters.AddWithValue("@name", project.DisplayName);
        command.Parameters.AddWithValue("@root", project.RepositoryRoot);
        command.Parameters.AddWithValue("@remote", (object?)project.RemoteUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("@created", project.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("@updated", project.UpdatedAt.ToString("O"));
        command.ExecuteNonQuery();
    }

    public int CountKnowledge(string projectId)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM knowledge WHERE project_id = @id;";
        command.Parameters.AddWithValue("@id", projectId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private Project? Query(string sql, string key)
    {
        using var connection = factory.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@key", key);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new Project(
            reader.GetString(reader.GetOrdinal("project_id")),
            reader.GetString(reader.GetOrdinal("display_name")),
            reader.GetString(reader.GetOrdinal("repository_root")),
            reader.IsDBNull(reader.GetOrdinal("remote_url"))
                ? null : reader.GetString(reader.GetOrdinal("remote_url")),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("updated_at"))));
    }
}
