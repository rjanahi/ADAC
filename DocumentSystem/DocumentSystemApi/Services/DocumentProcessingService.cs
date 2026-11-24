
using DocumentSystemApi.Data;
using DocumentSystemApi.Models;
using Microsoft.EntityFrameworkCore;

namespace DocumentSystemApi.Services
{
    public class DocumentProcessingService
    {
        private readonly DocumentDbContext _context;
        private readonly DocumentExtractionService _extractionService;

        //Maximum number of characters in each chunk
        private const int ChunkSize = 1000;

        //Constructor: receive services through dependency injection
        public DocumentProcessingService(
            DocumentDbContext context,
            DocumentExtractionService extractionService)
        {
            _context = context;
            _extractionService = extractionService;
        }

        //Process an uploaded document and save its text as chunks
        public async Task<int> ProcessDocumentAsync(
            Guid documentId,
            Guid userId)
        {
            //Find the document and verify that the user owns it
            var document = await _context.Documents
                .FirstOrDefaultAsync(d =>
                    d.Id == documentId &&
                    d.UserId == userId &&
                    !d.IsArchived);

            //If document is not found
            if (document == null)
                throw new KeyNotFoundException(
                    "Document not found.");

            //Only allow uploaded or failed documents to be processed
            if (document.Status != "Uploaded" &&
                document.Status != "Failed")
                throw new InvalidOperationException(
                    "Document is already processed or being processed.");

            //Mark the document as Processing
            document.Status = "Processing";
            document.UpdatedAt = DateTimeOffset.UtcNow;

            //Save the update
            await _context.SaveChangesAsync();

            try
            {
                //Extract text from the stored document
                var extraction = _extractionService.ExtractText(
                    document.FilePath);

                //Reject documents without readable text
                if (string.IsNullOrWhiteSpace(extraction.FullText))
                    throw new InvalidDataException(
                        "No readable text was found.");

                //Prepare the new chunks
                var newChunks = new List<DocumentChunk>();

                int chunkIndex = 0;

                //Process each extracted page or section
                foreach (var section in extraction.Sections)
                {
                    //Divide this section into smaller text chunks
                    var parts = SplitIntoChunks(section.Text);

                    foreach (string part in parts)
                    {
                        //Create a chunk connected to the Document
                        newChunks.Add(new DocumentChunk
                        {
                            Id = Guid.NewGuid(),
                            DocumentId = document.Id,
                            ChunkIndex = chunkIndex,
                            Content = part,
                            PageNumber = section.PageNumber,
                            SectionTitle = null,
                            CreatedAt = DateTimeOffset.UtcNow
                        });

                        //Give the next chunk a new index
                        chunkIndex++;
                    }
                }


                //Find any previous chunks for this document
                var oldChunks = await _context.DocumentChunks
                    .Where(c => c.DocumentId == document.Id)
                    .ToListAsync();

                //Remove old chunks to avoid duplicate records
                _context.DocumentChunks.RemoveRange(oldChunks);

                //Add the newly extracted chunks
                _context.DocumentChunks.AddRange(newChunks);

                //Mark extraction as complete
                document.Status = "Ready";
                document.UpdatedAt = DateTimeOffset.UtcNow;

                //Save the chunks and Document status
                await _context.SaveChangesAsync();

                //Return how many chunks were created
                return newChunks.Count;
            }
            catch
            {
                //Clear pending changes if saving chunks failed
                _context.ChangeTracker.Clear();

                //Find the document again
                var failedDocument = await _context.Documents
                    .FirstOrDefaultAsync(d =>
                        d.Id == documentId &&
                        d.UserId == userId &&
                        !d.IsArchived);

                if (failedDocument != null &&
                    failedDocument.Status == "Processing")
                {
                    //Record that processing failed
                    failedDocument.Status = "Failed";
                    failedDocument.UpdatedAt = DateTimeOffset.UtcNow;

                    await _context.SaveChangesAsync();
                }

                //Pass the original error back to the caller
                throw;
            }
        }

        //Split text into smaller pieces
        private static List<string> SplitIntoChunks(string text)
        {
            var chunks = new List<string>();

            //Ignore empty sections
            if (string.IsNullOrWhiteSpace(text))
                return chunks;

            int position = 0;

            //Keep reading until the full text has been processed
            while (position < text.Length)
            {
                //Calculate how many characters remain
                int remaining = text.Length - position;

                //Read at most 1000 characters
                int length = Math.Min(ChunkSize, remaining);

                //Get the next piece of text
                string chunk = text.Substring(position, length);

                //Avoid breaking words when possible
                if (length < remaining)
                {
                    int lastSpace = chunk.LastIndexOf(' ');

                    //Only split at a space if it is reasonably close
                    if (lastSpace > length / 2)
                    {
                        length = lastSpace + 1;
                        chunk = text.Substring(position, length);
                    }
                }

                //Remove surrounding whitespace
                chunk = chunk.Trim();

                //Save nonempty chunks
                if (!string.IsNullOrWhiteSpace(chunk))
                    chunks.Add(chunk);

                //Move forward by the consumed characters
                position += length;
            }

            return chunks;
        }
    }
}
