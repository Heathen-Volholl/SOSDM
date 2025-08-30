using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using LiteDB;

namespace SOSDM
{
    // DatabaseManager
    public class DatabaseManager
    {
        private SqliteConnection _sqliteConnection;
        private LiteDatabase _liteDb;

        public async Task InitializeAsync()
        {
            // Initialize SQLite for metadata and full-text search
            var connectionString = "Data Source=sosdm.db";
            _sqliteConnection = new SqliteConnection(connectionString);
            await _sqliteConnection.OpenAsync();

            // Create tables
            await CreateTables();

            // Initialize LiteDB for objects and embeddings
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

            var createFTS = @"
                CREATE VIRTUAL TABLE IF NOT EXISTS DocumentsFTS USING fts5(
                    Id UNINDEXED,
                    Title,
                    Authors,
                    Abstract,
                    Content,
                    Keywords,
                    content='Documents',
                    content_rowid='rowid'
                )";

            using var cmd1 = new SqliteCommand(createDocuments, _sqliteConnection);
            using var cmd2 = new SqliteCommand(createFTS, _sqliteConnection);

            await cmd1.ExecuteNonQueryAsync();
            await cmd2.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Main search. Uses FTS5, but safely quotes the MATCH string if it has special chars.
        /// Falls back to LIKE on Source/Title when the query looks like a filename or FTS returns nothing.
        /// </summary>
        public async Task<List<Document>> SearchDocuments(string query, int limit = 10)
        {
            var documents = new List<Document>();

            // 1) Try FTS with safe quoting
            var ftsQuery = ToFtsMatch(query);
            var sqlFts = @"
                SELECT d.* 
                FROM Documents d
                JOIN DocumentsFTS fts ON d.Id = fts.Id
                WHERE DocumentsFTS MATCH @query
                ORDER BY bm25(DocumentsFTS) ASC
                LIMIT @limit";

            try
            {
                using var cmd = new SqliteCommand(sqlFts, _sqliteConnection);
                cmd.Parameters.AddWithValue("@query", ftsQuery);
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    documents.Add(MapDocument(reader));
                }
            }
            catch (SqliteException)
            {
                // swallow and fall back to LIKE below
            }

            // 2) Fallback: if looks like a filename OR FTS found nothing, try LIKE on Source/Title
            if (documents.Count == 0 && (LooksLikeFilename(query) || LooksLikePath(query)))
            {
                var like = "%" + Path.GetFileName(query) + "%";
                var sqlLike = @"
                    SELECT * FROM Documents
                    WHERE (Source LIKE @like OR Title LIKE @like)
                    ORDER BY CreatedAt DESC
                    LIMIT @limit";

                using var cmd = new SqliteCommand(sqlLike, _sqliteConnection);
                cmd.Parameters.AddWithValue("@like", like);
                cmd.Parameters.AddWithValue("@limit", limit);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    documents.Add(MapDocument(reader));
                }
            }

            return documents;
        }

        private static bool LooksLikeFilename(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            // common doc extensions (extend as needed)
            var rx = new Regex(@"\.(pdf|txt|xml|docx?|pptx?|xlsx?)$", RegexOptions.IgnoreCase);
            return rx.IsMatch(text.Trim());
        }

        private static bool LooksLikePath(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            // crude check for path-like strings
            return text.Contains("\\") || text.Contains("/") || text.Contains(":");
        }

        /// <summary>
        /// Safely convert raw user text to an FTS5 MATCH expression:
        /// - If it contains any non-word operators/punctuation, wrap in double quotes.
        /// - Escape embedded quotes by doubling them.
        /// </summary>
        private static string ToFtsMatch(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";

            // Any characters that might break MATCH syntax -> quote the whole thing.
            // Keep this conservative: quote when we see anything other than word chars/space.
            var hasSpecial = Regex.IsMatch(raw, @"[^\w\s]");
            if (hasSpecial)
            {
                var quoted = raw.Replace("\"", "\"\""); // escape double-quotes for SQLite
                return $"\"{quoted}\"";
            }
            return raw;
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
                Keywords = GetStringOrNull("Keywords")
            };
        }

        public async Task StoreDocument(Document document)
        {
            // Store in SQLite
            var sql = @"
                INSERT OR REPLACE INTO Documents 
                (Id, Title, Authors, Abstract, Content, Source, PublicationDate, Keywords)
                VALUES (@id, @title, @authors, @abstract, @content, @source, @date, @keywords)";

            using var cmd = new SqliteCommand(sql, _sqliteConnection);
            cmd.Parameters.AddWithValue("@id", document.Id);
            cmd.Parameters.AddWithValue("@title", document.Title);
            cmd.Parameters.AddWithValue("@authors", document.Authors ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@abstract", document.Abstract ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@content", document.Content ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@source", document.Source ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@date", document.PublicationDate ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@keywords", document.Keywords ?? (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync();

            // Update FTS index
            var ftsSQL = @"
                INSERT OR REPLACE INTO DocumentsFTS 
                (Id, Title, Authors, Abstract, Content, Keywords)
                VALUES (@id, @title, @authors, @abstract, @content, @keywords)";

            using var ftsCmd = new SqliteCommand(ftsSQL, _sqliteConnection);
            ftsCmd.Parameters.AddWithValue("@id", document.Id);
            ftsCmd.Parameters.AddWithValue("@title", document.Title ?? "");
            ftsCmd.Parameters.AddWithValue("@authors", document.Authors ?? "");
            ftsCmd.Parameters.AddWithValue("@abstract", document.Abstract ?? "");
            ftsCmd.Parameters.AddWithValue("@content", document.Content ?? "");
            ftsCmd.Parameters.AddWithValue("@keywords", document.Keywords ?? "");

            await ftsCmd.ExecuteNonQueryAsync();

            // Store embedding in LiteDB if present
            if (document.Embedding != null)
            {
                var embeddings = _liteDb.GetCollection<DocumentEmbedding>("embeddings");
                embeddings.Upsert(new DocumentEmbedding
                {
                    DocumentId = document.Id,
                    Vector = document.Embedding
                });
            }
        }

        public async Task<DatabaseStatistics> GetStatistics()
        {
            var countSQL = "SELECT COUNT(*) FROM Documents";
            using var cmd = new SqliteCommand(countSQL, _sqliteConnection);
            var docCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            var embeddings = _liteDb.GetCollection<DocumentEmbedding>("embeddings");
            var embCount = embeddings.Count();

            return new DatabaseStatistics
            {
                DocumentCount = docCount,
                EmbeddingCount = embCount
            };
        }
    }
}
