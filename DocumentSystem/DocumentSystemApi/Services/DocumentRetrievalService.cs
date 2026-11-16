using DocumentSystemApi.Dtos;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using DocumentSystemApi.Data;


namespace DocumentSystemApi.Services
{
    public class DocumentRetrievalService
    {
        private readonly DocumentDbContext _context;

        //Limit the amount of document text sent to the AI
        private const int DefaultChunkLimit = 5;
        private const int MaxChunkLimit = 10;

        //Common words that usually don't help with searching
        private static readonly HashSet<string> StopWords =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "a", "an", "the", "and", "or",
                "is", "are", "was", "were", "be",
                "to", "of", "in", "on", "at",
                "for", "from", "with", "by",
                "what", "which", "who", "when",
                "where", "why", "how", "does",
                "do", "did", "can", "could",
                "would", "should", "about",
                "this", "that", "these", "those",
                "it", "its", "my", "me", "i",
                "you", "your", "please", "tell"
            };

        //Constructor: receive the database context
        public DocumentRetrievalService(
            DocumentDbContext context)
        {
            _context = context;
        }


        //Find the most relevant chunks for a question
        public async Task<List<RetrievedChunkDto>>
            RetrieveAsync(
                Guid documentId,
                Guid userId,
                string question,
                int limit = DefaultChunkLimit)
        {
            //Reject invalid document or user identifiers
            if (documentId == Guid.Empty ||
                userId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Valid document and user IDs are required.");
            }

            //Reject empty questions
            if (string.IsNullOrWhiteSpace(question))
            {
                throw new ArgumentException(
                    "Question cannot be empty.");
            }

            //Keep the requested result limit within safe bounds
            limit = Math.Clamp(limit, 1, MaxChunkLimit);

            //Verify that the document belongs to this user
            var document = await _context.Documents
                .Where(d =>
                    d.Id == documentId &&
                    d.UserId == userId &&
                    !d.IsArchived)
                .Select(d => new
                {
                    d.Id,
                    d.Status
                })
                .FirstOrDefaultAsync();

            //Don't expose another user's document
            if (document == null)
                throw new KeyNotFoundException(
                    "Document not found.");

            //Only search documents that have been processed
            if (document.Status != "Ready")
                throw new InvalidOperationException(
                    "Document must be Ready before searching.");

            //Extract meaningful keywords from the question
            var keywords = ExtractKeywords(question);

            //Return no matches when no searchable keywords exist
            if (keywords.Count == 0)
                return new List<RetrievedChunkDto>();

            //Load chunks belonging only to the selected document
            var chunks = await _context.DocumentChunks
                .Where(c => c.DocumentId == documentId)
                .OrderBy(c => c.ChunkIndex)
                .Select(c => new RetrievedChunkDto
                {
                    ChunkId = c.Id,
                    ChunkIndex = c.ChunkIndex,
                    Content = c.Content,
                    PageNumber = c.PageNumber,
                    SectionTitle = c.SectionTitle
                })
                .ToListAsync();


            //Calculate relevance scores for each document chunk
            foreach (var chunk in chunks)
            {
                //Calculate keyword relevance normally
                chunk.Score = CalculateScore(
                    chunk.Content,
                    chunk.SectionTitle,
                    keywords);

                //Reduce the priority of chunks containing Word artifacts
                //Keep them available if no better evidence exists
                if (ContainsWordNavigationArtifacts(chunk.Content))
                {
                    chunk.Score = Math.Max(
                        0,
                        chunk.Score - 10);
                }
            }


            //Select chunks that have positive relevance scores
            var rankedChunks = chunks
                .Where(c => c.Score > 0)
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.ChunkIndex)
                .ToList();

            //Prefer evidence without Word formatting artifacts
            var cleanChunks = rankedChunks
                .Where(c => !ContainsWordNavigationArtifacts(c.Content))
                .Take(limit)
                .ToList();

            //Use clean chunks when relevant evidence exists
            if (cleanChunks.Count > 0)
            {
                return cleanChunks;
            }

            //Fallback to the ranked results if no clean evidence exists
            return rankedChunks
                .Take(limit)
                .ToList();

        }


        //Extract searchable keywords from the question
        private static List<string> ExtractKeywords(
            string question)
        {
            //Find words containing letters or numbers
            var words = Regex.Matches(
                question.ToLowerInvariant(),
                @"[\p{L}\p{N}]+");

            //Remove stop words and duplicate keywords
            return words
                .Select(m => m.Value)
                .Where(word =>
                    word.Length >= 2 &&
                    !StopWords.Contains(word))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }


        //Calculate how relevant a chunk is to the question
        private static int CalculateScore(
            string content,
            string? sectionTitle,
            List<string> keywords)
        {
            int score = 0;

            //Use lowercase text for case-insensitive matching
            string normalizedContent =
                content.ToLowerInvariant();

            string normalizedTitle =
                (sectionTitle ?? "").ToLowerInvariant();

            //Check each keyword against the chunk
            foreach (string keyword in keywords)
            {
                //Count complete-word matches in the content
                int contentMatches = Regex.Matches(
                    normalizedContent,
                    $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(keyword)}(?![\p{{L}}\p{{N}}])"
                ).Count;

                //Give one point per content match
                score += contentMatches;

                //Give extra weight to matching section headings
                if (normalizedTitle.Contains(
                    keyword, StringComparison.Ordinal))
                {
                    score += 3;
                }
            }

            return score;
        }


        //Check whether a chunk contains Word navigation artifacts
        private static bool ContainsWordNavigationArtifacts(
            string content)
        {
            //Detect Word table-of-contents references
            bool hasTocReference = Regex.IsMatch(
                content,
                @"_Toc\d+",
                RegexOptions.IgnoreCase);

            //Detect Word page-reference instructions
            bool hasPageReference = Regex.IsMatch(
                content,
                @"\bPAGEREF\b",
                RegexOptions.IgnoreCase);

            //Identify chunks containing these extraction artifacts
            return hasTocReference || hasPageReference;
        }

    }
}
