using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SOSDM
{
    internal class Program
    {
        static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("SOSDM v1.0");
                Console.WriteLine("Usage:");
                Console.WriteLine("  sosdm ingest <folder>");
                Console.WriteLine("  sosdm query <text or \"phrase\">");
                Console.WriteLine("  sosdm status");
                return 1;
            }

            var db = new DatabaseManager();
            await db.InitializeAsync();

            switch (args[0].ToLowerInvariant())
            {
                case "ingest":
                {
                    var folder = args.Length > 1 ? args[1] : ".";
                    int added = 0;
                    foreach (var file in Directory.EnumerateFiles(folder, "*.pdf", SearchOption.AllDirectories))
                    {
                        var doc = await PdfIngest.LoadDocumentAsync(file);
                        await db.StoreDocument(doc);
                        if (++added % 25 == 0) Console.WriteLine($"Ingested {added}...");
                    }
                    Console.WriteLine($"Ingest complete. {added} documents.");
                    return 0;
                }

                case "query":
                {
                    var q = string.Join(' ', args.Skip(1));
                    var hits = await db.SearchDocuments(q, 10);
                    foreach (var h in hits)
                        Console.WriteLine($"{h.Source} | {h.Title} | {h.Id}");
                    Console.WriteLine($"Total: {hits.Count}");
                    return 0;
                }

                case "status":
                {
                    var stats = await db.GetStatistics();
                    Console.WriteLine($"Docs: {stats.DocumentCount}  Embeddings: {stats.EmbeddingCount}");
                    return 0;
                }
            }

            Console.WriteLine("Unknown command.");
            return 2;
        }
    }
}
