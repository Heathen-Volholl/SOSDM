using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using LiteDB;

namespace SOSDM
{
    public sealed class DatabaseManager
    {
        private SqliteConnection _sqliteConnection;
        private LiteDatabase _liteDb;

        public async Task InitializeAsync()
        {
            _sqliteConnection = new SqliteConnection("Data Source=sosdm.db;Mode=ReadWriteCreate;");
            await _sqliteConnection.OpenAsync();
            await CreateTables();

            _liteDb = new LiteDatabase("sosdm_objects.db");
        }

        private async Task CreateTables()
        {
            var createDocuments = @"
                CREATE TABLE IF NOT EXISTS Documents (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Authors TEXT,
                    Abstract TEXT,
                    Content TEXT,
                    Source TEXT,
                    PublicationDate TEXT,
                    Keywords TEXT,
                    CitationCount INTEGER DEFAULT 0,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                )";

            // FTS includes Source
            var createFTS = @"
                CREATE VIRTUAL TABLE IF NOT EXISTS DocumentsFTS USING fts5(
                    Id UNINDEXED,
                    Title,
                    Authors,
                    Abstract,
                    Content,
                    Source,
                    Keywords,
                    content='Documents',
                    content_rowid='rowid'
                )";

            using (var cmd1 = new SqliteCommand(createDocuments, _sqliteConnection))
                await cmd1.ExecuteNonQueryAsync();

            // Rebuild FTS if older schema is present (missing Source)
            if (!await FtsHasColumn("Source"))
            {
                using (var drop = new SqliteCommand("DROP TABLE IF EXISTS DocumentsFTS", _sqliteConnection))
                    await drop.ExecuteNonQueryAsync();
                using (var create = new SqliteCommand(createFTS, _sqliteConnection))
                    await create.ExecuteNonQueryAsync();

                var refill = @"
                    INSERT INTO DocumentsFTS (Id, Title, Authors, Abstract, Content, Source, Keywords)
                    SELECT Id, Title, Authors, Abstract, Content, Source, Keywords FROM Documents";
                using var refillCmd = new SqliteCommand(refill, _sqliteConnection);
                await refillCmd.ExecuteNonQueryAsync();
            }
            else
            {
                using var cmd2 = new SqliteCommand(createFTS, _sqliteConnection);
                await cmd2.ExecuteNonQueryAsync();
            }
        }

        private async Task<bool> FtsHasColumn(string col)
        {
            using var cmd = new SqliteCommand("PRAGMA table_info(DocumentsFTS);", _sqliteConnection);
            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var name = r.GetString(1);
                if (string.Equals(name, col, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public async Task StoreDocument(Document document)
        {
            var sql = @"
                INSERT OR REPLACE INTO Documents 
                (Id, Title, Authors, Abstract, Content, Source, PublicationDate, Keywords, CitationCount)
                VALUES (@id, @title, @authors, @abstract, @content, @source, @date, @keywords, @cites)";
            using (var cmd = new SqliteCommand(sql, _sqliteConnection))
            {
                cmd.Parameters.AddWithValue("@id", document.Id);
                cmd.Parameters.AddWithValue("@title", document.Title ?? "");
                cmd.Parameters.AddWithValue("@authors", (object?)document.Authors ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@abstract", (object?)document.Abstract ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@content", (object?)document.Content ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@source", (object?)document.Source ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@date", (object?)document.PublicationDate ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@keywords", (object?)document.Keywords ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cites", document.CitationCount);
                await cmd.ExecuteNonQueryAsync();
            }

            var ftsSQL = @"
                INSERT OR REPLACE INTO DocumentsFTS 
                (Id, Title, Authors, Abstract, Content, Source, Keywords)
                VALUES (@id, @title, @authors, @abstract, @content, @source, @keywords)";
            using (var ftsCmd = new SqliteCommand(ftsSQL, _sqliteConnection))
            {
                ftsCmd.Parameters.AddWithValue("@id", document.Id);
                ftsCmd.Parameters.AddWithValue("@title", document.Title ?? "");
                ftsCmd.Parameters.AddWithValue("@authors", document.Authors ?? "");
                ftsCmd.Parameters.AddWithValue("@abstract", document.Abstract ?? "");
                ftsCmd.Parameters.AddWithValue("@content", document.Content ?? "");
                ftsCmd.Parameters.AddWithValue("@source", document.Source ?? "");
                ftsCmd.Parameters.AddWithValue("@keywords", document.Keywords ?? "");
                await ftsCmd.ExecuteNonQueryAsync();
            }

            if (document.Embedding != null)
            {
                var embeddings = _liteDb.GetCollection<DocumentEmbedding>("embeddings");
                embeddings.Upsert(new DocumentEmbedding
                {
                    Id = document.Id,          // ensure primary key in LiteDB
                    DocumentId = document.Id,
                    Vector = document.Embedding
                });
            }
        }

        public async Task<List<Document>> SearchDocuments(string query, int limit = 10)
        {
            var documents = new List<Document>();
            if (string.IsNullOrWhiteSpace(query)) return documents;

            // FTS first
            var ftsQuery = ToFtsMatch(query);
            var sqlFts = @"
                SELECT d.* 
                FROM DocumentsFTS fts
                JOIN Documents d ON d.Id = fts.Id
                WHERE fts MATCH @query
                ORDER BY bm25(fts) ASC
                LIMIT @limit";
            try
            {
                using var cmd = new SqliteCommand(sqlFts, _sqliteConnection);
                cmd.Parameters.AddWithValue("@query", ftsQuery);
                cmd.Parameters.AddWithValue("@limit", limit);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    documents.Add(MapDocument(reader));
            }
            catch (SqliteException)
            {
                // fall back below
            }

            // Fallback LIKE if looks like a filename/path or FTS returned nothing
            if (documents.Count == 0 && (LooksLikeFilename(query) || LooksLikePath(query)))
            {
                var like = "%" + EscapeLike(Path.GetFileName(query)) + "%";
                var sqlLike = @"
                    SELECT * FROM Documents
                    WHERE (Source LIKE @like ESCAPE '\' OR Title LIKE @like ESCAPE '\')
                    ORDER BY CreatedAt DESC
                    LIMIT @limit";
                using var cmd = new SqliteCommand(sqlLike, _sqliteConnection);
                cmd.Parameters.AddWithValue("@like", like);
                cmd.Parameters.AddWithValue("@limit", limit);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    documents.Add(MapDocument(reader));
            }

            return documents;
        }

        private static bool LooksLikeFilename(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var rx = new Regex(@"\.(pdf|txt|xml|docx?|pptx?|xlsx?)$", RegexOptions.IgnoreCase);
            return rx.IsMatch(text.Trim());
        }

        private static bool LooksLikePath(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return text.Contains("\\") || text.Contains("/") || text.Contains(":");
        }

        private static string ToFtsMatch(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            var hasSpecial = Regex.IsMatch(raw, @"[^\w\s]");
            if (hasSpecial)
            {
                var quoted = raw.Replace("\"", "\"\"");
                return $"\"{quoted}\"";
            }
            return raw;
        }

        private static string EscapeLike(string s)
        {
            return s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        }

        private static Document MapDocument(SqliteDataReader reader)
        {
            string GetStringOrNull(string col)
            {
                var i = reader.GetOrdinal(col);
                return reader.IsDBNull(i) ? null : reader.GetString(i);
            }

            return new Document
            {
                Id = reader.GetString(reader.GetOrdinal("Id")),
                Title = reader.GetString(reader.GetOrdinal("Title")),
                Authors = GetStringOrNull("Authors"),
                Abstract = GetStringOrNull("Abstract"),
                Content = GetStringOrNull("Content"),
                Source = GetStringOrNull("Source"),
                PublicationDate = GetStringOrNull("PublicationDate"),
                Keywords = GetStringOrNull("Keywords"),
                CitationCount = reader.TryGetOrdinal("CitationCount", out var ci) && !reader.IsDBNull(ci)
                                ? Convert.ToInt32(reader.GetValue(ci)) : 0
            };
        }
		public async Task<DatabaseStatistics> GetStatistics()
{
    // Count docs in SQLite
    long docCount = 0;
    using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("SELECT COUNT(*) FROM Documents", _sqliteConnection))
    {
        var val = await cmd.ExecuteScalarAsync();
        docCount = (val is long l) ? l : Convert.ToInt64(val ?? 0);
    }

    // Count embeddings in LiteDB
    var embeddings = _liteDb.GetCollection<DocumentEmbedding>("embeddings");
    var embCount = embeddings.Count();

    return new DatabaseStatistics
    {
        DocumentCount = (int)docCount,
        EmbeddingCount = embCount
    };
}

    }

    internal static class SqliteReaderExtensions
    {
        public static bool TryGetOrdinal(this SqliteDataReader r, string name, out int ordinal)
        {
            try { ordinal = r.GetOrdinal(name); return true; }
            catch { ordinal = -1; return false; }
        }
    }
}
