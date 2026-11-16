using DocumentSystemApi.Data;
using DocumentSystemApi.Dtos;
using DocumentSystemApi.Models;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text;

namespace DocumentSystemApi.Services;

public class DocumentService
{
    private readonly DocumentDbContext _context;
    private readonly string _storagePath;

    private const long MaxFileSize = 10 * 1024 * 1024;

    private static readonly string[] AllowedExtensions =
    {
        ".pdf", ".docx", ".txt"
    };

    public DocumentService(DocumentDbContext context)
    {
        _context = context;

        //Set the location where uploaded documents will be stored
        _storagePath = @"..\Uploads";

        Directory.CreateDirectory(_storagePath);
    }

    //Upload Service
    public async Task<Document> UploadAsync(
        IFormFile file,
        string title,
        Guid userId)
    {
        //Original documents start at version 1
        return await SaveUploadedDocumentAsync(
            file,
            title,
            userId,
            parentDocumentId: null,
            documentVersion: 1);
    }


    //Shared upload logic for original documents and revisions
    private async Task<Document> SaveUploadedDocumentAsync(
        IFormFile file,
        string title,
        Guid userId,
        Guid? parentDocumentId,
        int documentVersion)
    {

        //No file
        if (file == null || file.Length == 0)
            throw new ArgumentException("File is required.");

        //File too large
        if (file.Length > MaxFileSize)
            throw new ArgumentException(
                "Maximum file size is 10 MB.");

        // The original filename is untrusted input.
        string originalName = Path.GetFileName(
            file.FileName.Replace('\\', '/'));

        if (string.IsNullOrWhiteSpace(originalName) ||
            originalName.Length > 255)
            throw new ArgumentException("Invalid filename.");

        string extension = Path.GetExtension(originalName)
            .ToLowerInvariant();

        //Not Allowed File Type
        if (!AllowedExtensions.Contains(extension))
            throw new ArgumentException("Unsupported file type.");

        title = title?.Trim() ?? "";
        //Empty tile or too long
        if (string.IsNullOrWhiteSpace(title) ||
            title.Length > 200)
            throw new ArgumentException(
                "Title must contain 1 to 200 characters.");

        // Random storage name. To avoid duplication.
        string storedName =
            $"{Guid.NewGuid():N}{extension}";

        string path = Path.Combine(_storagePath, storedName);

        // Save to private, non-public storage.
        await using (var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write))
        {
            await file.CopyToAsync(stream);
        }

        try
        {
            //Validate file content
            ValidateFileContent(path, extension);

            //Only reject duplicates for normal document uploads
            if (parentDocumentId == null)
            {
                // Find possible duplicates for THIS user only.
                var candidates = await _context.Documents
                .Where(d =>
                    d.UserId == userId &&
                    !d.IsArchived &&
                    d.FileSizeBytes == file.Length)
                .Select(d => d.FilePath)
                .ToListAsync();

                //Gether all the bytes inside the file
                byte[] uploadedBytes =
                    await File.ReadAllBytesAsync(path);

                foreach (string existingPath in candidates)
                {
                    if (!File.Exists(existingPath))
                        continue;

                    byte[] existingBytes =
                        await File.ReadAllBytesAsync(existingPath);

                    if (uploadedBytes.AsSpan()
                        .SequenceEqual(existingBytes))
                    {
                        throw new InvalidOperationException(
                            "This document has already been uploaded.");
                    }
                }
            }

            var document = new Document
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Title = title,
                OriginalFileName = originalName,
                StoredFileName = storedName,
                FilePath = path,
                ContentType = extension switch
                {
                    ".pdf" => "application/pdf",
                    ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    _ => "text/plain"
                },
                FileSizeBytes = file.Length,
                Status = "Uploaded",
                ParentDocumentId = parentDocumentId,
                DocumentVersion = documentVersion,
                DocumentType = "Unknown",
                UploadedAt = DateTimeOffset.UtcNow,
                IsArchived = false
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            return document;
        }
        catch
        {
            // Clean up if validation or database save fails.
            if (File.Exists(path))
                File.Delete(path);

            throw;
        }
    }

    //Upload a new version of an existing document
    public async Task<Document> UploadDocumentVersionAsync(
        Guid documentId,
        IFormFile file,
        Guid userId)
    {
        //Find the original document and verify ownership
        Document originalDocument =
            await GetOriginalDocumentAsync(
                documentId,
                userId);

        //Calculate the next document version
        int nextVersion =
            await GetNextDocumentVersionAsync(
                originalDocument.Id);

        Document newDocument =
            await SaveUploadedDocumentAsync(
                file,
                originalDocument.Title,
                userId,
                parentDocumentId: originalDocument.Id,
                documentVersion: nextVersion);

        //Return the newly created document
        return newDocument;
    }


    //Retrieve every active version of a document
    public async Task<List<DocumentResponseDto>> GetDocumentVersionsAsync(
        Guid documentId,
        Guid userId)
    {
        //Find the original document and verify ownership
        Document originalDocument =
            await GetOriginalDocumentAsync(documentId, userId);

        //Get the original document and all its revisions
        var versions = await _context.Documents
            .Where(d =>
                d.UserId == userId &&
                !d.IsArchived &&
                (
                    d.Id == originalDocument.Id ||
                    d.ParentDocumentId == originalDocument.Id
                ))

            //Display the newest version first
            .OrderByDescending(d => d.DocumentVersion)

            //Return only the information required by the frontend
            .Select(d => new DocumentResponseDto
            {
                Id = d.Id,
                Title = d.Title,
                OriginalFileName = d.OriginalFileName,
                ContentType = d.ContentType,
                FileSizeBytes = d.FileSizeBytes,
                Status = d.Status,
                DocumentType = d.DocumentType,
                UploadedAt = d.UploadedAt,

                //Version information
                ParentDocumentId = d.ParentDocumentId,
                DocumentVersion = d.DocumentVersion
            })
            .ToListAsync();

        return versions;
    }


    //Find the original document for any uploaded version
    private async Task<Document> GetOriginalDocumentAsync(
        Guid documentId,
        Guid userId)
    {
        //Find the requested document and verify its owner
        var document = await _context.Documents
            .FirstOrDefaultAsync(d =>
                d.Id == documentId &&
                d.UserId == userId &&
                !d.IsArchived);

        //Reject documents that don't exist or belong to another user
        if (document == null)
            throw new KeyNotFoundException(
                "Document not found.");

        //An original document has no parent
        if (document.ParentDocumentId == null)
            return document;

        //For a revision, find its original document
        var originalDocument = await _context.Documents
            .FirstOrDefaultAsync(d =>
                d.Id == document.ParentDocumentId.Value &&
                d.UserId == userId &&
                !d.IsArchived);

        //Reject revisions whose original document is unavailable
        if (originalDocument == null)
            throw new KeyNotFoundException(
                "Original document not found.");

        return originalDocument;
    }


    //Find the next available version number for a document
    private async Task<int> GetNextDocumentVersionAsync(
        Guid originalDocumentId)
    {
        //Find the highest version among existing revisions
        int latestVersion = await _context.Documents
            .Where(d =>
                d.Id == originalDocumentId ||
                d.ParentDocumentId == originalDocumentId)
            .MaxAsync(d => (int?)d.DocumentVersion) ?? 0;

        //The next version is one greater than the latest
        return checked(latestVersion + 1);
    }

    private static void ValidateFileContent(
        string path, string extension)
    {
        if (extension == ".pdf")
        {
            using var stream = File.OpenRead(path);
            byte[] header = new byte[5];

            if (stream.Read(header) != 5 ||
                Encoding.ASCII.GetString(header) != "%PDF-")
                throw new ArgumentException("Invalid PDF file.");
        }
        else if (extension == ".docx")
        {
            try
            {
                using var archive = ZipFile.OpenRead(path);

                if (archive.GetEntry("word/document.xml") == null)
                    throw new ArgumentException(
                        "Invalid Word document.");
            }
            catch (InvalidDataException)
            {
                throw new ArgumentException(
                    "Invalid Word document.");
            }
        }
        else if (extension == ".txt")
        {
            try
            {
                var utf8 = new UTF8Encoding(false, true);
                string content = utf8.GetString(
                    File.ReadAllBytes(path));

                if (content.Contains('\0'))
                    throw new ArgumentException(
                        "Invalid text file.");
            }
            catch (DecoderFallbackException)
            {
                throw new ArgumentException(
                    "Text file must use UTF-8.");
            }
        }
    }


    //Get all active documents belonging to a specific user
    public async Task<List<DocumentResponseDto>> GetUserDocumentsAsync(
        Guid userId)
    {
        //Find documents owned by the user that are not archived
        var documents = await _context.Documents
            .Where(d =>
                d.UserId == userId &&
                !d.IsArchived)

            //Display the newest documents first
            .OrderByDescending(d => d.UploadedAt)

            //Select only the fields we want to return
            .Select(d => new DocumentResponseDto
            {
                Id = d.Id,
                Title = d.Title,
                OriginalFileName = d.OriginalFileName,
                ContentType = d.ContentType,
                FileSizeBytes = d.FileSizeBytes,
                Status = d.Status,
                ParentDocumentId = d.ParentDocumentId,
                DocumentVersion = d.DocumentVersion,
                DocumentType = d.DocumentType,
                UploadedAt = d.UploadedAt
            }).ToListAsync();

        return documents;
    }


    //Get information about one document owned by the user
    public async Task<DocumentResponseDto?> GetDocumentByIdAsync(
        Guid documentId,
        Guid userId)
    {
        //Search using both document ID and user ID
        var document = await _context.Documents
            .Where(d =>
                d.Id == documentId &&
                d.UserId == userId &&
                !d.IsArchived)

            //Convert the Document into a Document Response Dto
            .Select(d => new DocumentResponseDto
            {
                Id = d.Id,
                Title = d.Title,
                OriginalFileName = d.OriginalFileName,
                ContentType = d.ContentType,
                FileSizeBytes = d.FileSizeBytes,
                Status = d.Status,
                ParentDocumentId = d.ParentDocumentId,
                DocumentVersion = d.DocumentVersion,
                DocumentType = d.DocumentType,
                UploadedAt = d.UploadedAt
            }).FirstOrDefaultAsync(); //Return the document or null if it doesn't exist

        return document;
    }


    //Archive a document without physically deleting it
    public async Task<bool> ArchiveDocumentAsync(
        Guid documentId,
        Guid userId)
    {
        //Find the document and verify its owner
        var document = await _context.Documents
            .FirstOrDefaultAsync(d =>
                d.Id == documentId &&
                d.UserId == userId &&
                !d.IsArchived);

        //Return false if the document wasn't found
        if (document == null)
            return false;

        //Mark the document as archived
        document.IsArchived = true;

        //Update the modification date
        document.UpdatedAt = DateTimeOffset.UtcNow;

        //Save changes
        await _context.SaveChangesAsync();

        return true;
    }


}
