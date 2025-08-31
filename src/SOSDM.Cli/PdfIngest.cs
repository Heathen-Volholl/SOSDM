using System.IO;
using System.Text;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace SOSDM
{
    public static class PdfIngest
    {
        public static async Task<Document> LoadDocumentAsync(string path)
        {
            string text = await Task.Run(() =>
            {
                using var pdf = PdfDocument.Open(path);
                var sb = new StringBuilder();
                foreach (var page in pdf.GetPages())
                    sb.AppendLine(page.Text);
                return sb.ToString();
            });

            var fi = new FileInfo(path);
            return new Document
            {
                Id = fi.FullName,                          // stable PK
                Title = Path.GetFileNameWithoutExtension(path),
                Authors = "",
                Abstract = "",
                Content = text ?? "",
                Source = Path.GetFileName(path),           // enables filename FTS/LIKE
                PublicationDate = fi.LastWriteTimeUtc.ToString("o"),
                Keywords = "",
                CitationCount = 0
            };
        }
    }
}
