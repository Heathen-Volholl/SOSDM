using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text;
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

            // Create tables (and rebuild FTS if older schema is present)
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

            // NOTE: FTS includes Source (fix #1)
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

            // If an older FTS exists without Source, rebuild it
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
                // PRAGMA table_info columns: cid|name|type|notnull|dflt_value|pk
                var name = r.GetString(1);
                if (string.Equals(name, col, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Main search. Uses FTS5 (now indexing Source), with safe quoting.
        /// Falls back to LIKE on Source/Title when the query looks like a filename or FTS returns nothing.
        /// </summary>
        public async Task<List<Document>> SearchDocuments(string query, int limit = 10)
        {
            var documents = new List<Document>();
            if (string.IsNullOrWhiteSpace(query)) return documents;

            // 1) Try FTS with safe quoting
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
                // swallow and fall back to LIKE below
            }

            // 2) Fallback: if looks like a filename OR FTS found nothing, try LIKE on Source/Title
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

        /// <summary>
        /// Convert raw text to a safe FTS5 MATCH expression:
        /// quote the whole string if it has any non-word characters.
        /// </summary>
        private static string ToFtsMatch(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return raw ?? "";
            var hasSpecial = Regex.IsMatch(raw, @"[^\w\s]");
            if (hasSpecial)
            {
                var quoted = raw.Replace("\"", "\"\""); // escape quotes
                return $"\"{quoted}\"";
            }
            return raw;
        }

        private static string EscapeLike(string s)
        {
            // Escape %, _, and backslash for LIKE ... ESCAPE '\'
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
                Keywords = GetStringOrNull("Keywords")
            };
        }

        public async Task StoreDocument(Document document)
        {
            // Store in SQLite
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

            // Update FTS index (includes Source)  <-- fix #1
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
