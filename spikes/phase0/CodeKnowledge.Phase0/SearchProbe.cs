using System.Text;
using Microsoft.Data.Sqlite;

namespace CodeKnowledge.Phase0;

internal enum SearchRoute { Fts, Like }

internal static class SearchProbe
{
    public static SearchRoute SelectRoute(string raw)
    {
        var value = Normalize(raw);
        return value.EnumerateRunes().Count() >= 3 ? SearchRoute.Fts : SearchRoute.Like;
    }

    public static IReadOnlyCollection<long> Search(
        SqliteConnection connection,
        IEnumerable<string> rawTerms)
    {
        var ids = new HashSet<long>();
        foreach (var term in rawTerms.Select(Normalize).Where(static x => x.Length > 0))
        {
            using var command = connection.CreateCommand();
            if (SelectRoute(term) is SearchRoute.Fts)
            {
                command.CommandText =
                    "SELECT CAST(id AS INTEGER) FROM knowledge_fts WHERE knowledge_fts MATCH $term";
                command.Parameters.AddWithValue("$term", $"\"{term.Replace("\"", "\"\"")}\"");
            }
            else
            {
                command.CommandText =
                    "SELECT id FROM knowledge_records WHERE title LIKE $term ESCAPE '\\'";
                var escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                command.Parameters.AddWithValue("$term", $"%{escaped}%");
            }

            using var reader = command.ExecuteReader();
            while (reader.Read()) ids.Add(reader.GetInt64(0));
        }
        return ids;
    }

    private static string Normalize(string value) =>
        value.Normalize(NormalizationForm.FormKC).Trim();
}
