using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
        
        public async Task<List<Document>> SearchDocuments(string query, int limit = 10)
        {
            var sql = @"
                SELECT d.* FROM Documents d
                JOIN DocumentsFTS fts ON d.Id = fts.Id
                WHERE DocumentsFTS MATCH @query
                ORDER BY rank
                LIMIT @limit";
            
            using var cmd = new SqliteCommand(sql, _sqliteConnection);
            cmd.Parameters.AddWithValue("@query", query);
            cmd.Parameters.AddWithValue("@limit", limit);
            
            var documents = new List<Document>();
            using var reader = await cmd.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                documents.Add(new Document
                {
                    Id = reader.GetString("Id"),
                    Title = reader.GetString("Title"),
                    Authors = reader.IsDBNull("Authors") ? null : reader.GetString("Authors"),
                    Abstract = reader.IsDBNull("Abstract") ? null : reader.GetString("Abstract"),
                    Content = reader.GetString("Content"),
                    Source = reader.IsDBNull("Source") ? null : reader.GetString("Source"),
                    PublicationDate = reader.IsDBNull("PublicationDate") ? null : reader.GetString("PublicationDate")
                });
            }
            
            return documents;
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
            cmd.Parameters.AddWithValue("@content", document.Content);
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
            ftsCmd.Parameters.AddWithValue("@title", document.Title);
            ftsCmd.Parameters.AddWithValue("@authors", document.Authors ?? "");
            ftsCmd.Parameters.AddWithValue("@abstract", document.Abstract ?? "");
            ftsCmd.Parameters.AddWithValue("@content", document.Content);
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

