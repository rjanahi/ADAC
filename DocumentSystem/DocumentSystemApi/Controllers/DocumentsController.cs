
using DocumentSystemApi.Dtos;
using DocumentSystemApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DocumentSystemApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")] // /api/Documents
public class DocumentsController : ControllerBase
{
    private readonly DocumentService _documentService;

    //Service responsible for processing uploaded documents
    private readonly DocumentProcessingService _processingService;

    //Service responsible for AI document classification
    private readonly DocumentClassificationService _classificationService;

    //Service responsible for comparing document versions using AI
    private readonly DocumentComparisonService _comparisonService;

    //Receive document services through dependency injection
    public DocumentsController(
        DocumentService documentService,
        DocumentProcessingService processingService,
        DocumentClassificationService classificationService,
        DocumentComparisonService comparisonService)
    {
        _documentService = documentService;
        _processingService = processingService;
        _classificationService = classificationService;
        _comparisonService = comparisonService;
    }

    [HttpPost("upload")] //POST /api/Documents/upload 
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] string title)
    {
        //Find the user
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);
        //make sure they are authorized
        if (!Guid.TryParse(userIdValue, out Guid userId))
            return Unauthorized();

        try
        {
            //Upload file
            var Document = await _documentService.UploadAsync(
                file, title, userId);

            return StatusCode(201, new
            {
                message = "Document uploaded successfully.",
                DocumentId = Document.Id,
                title = Document.Title,
                documentType = Document.DocumentType,
                status = Document.Status
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }


    //Upload a new version of an existing document
    [HttpPost("{id:guid}/versions")] //POST /api/Documents/{id}/versions
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> UploadDocumentVersion(
        Guid id,
        [FromForm] IFormFile file)
    {
        //Get the authenticated user's ID
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out Guid userId))
            return Unauthorized();

        try
        {
            //Upload the revision using the existing service
            var document =
                await _documentService.UploadDocumentVersionAsync(
                    id,
                    file,
                    userId);

            //Return the new document's information
            return StatusCode(201, new
            {
                message = "New document version uploaded successfully.",
                documentId = document.Id,
                parentDocumentId = document.ParentDocumentId,
                documentVersion = document.DocumentVersion,
                title = document.Title,
                status = document.Status
            });
        }
        catch (KeyNotFoundException)
        {
            //The requested document does not exist or isn't owned
            return NotFound(new
            {
                message = "Document not found."
            });
        }
        catch (ArgumentException ex)
        {
            //Invalid file, size, extension, or content
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            //A database constraint may have rejected the upload
            //This can happen with simultaneous version uploads
            return Conflict(new
            {
                message = "The document version could not be saved. " +
                          "Please retry the upload."
            });
        }
    }

    //Return all active documents belonging to the logged-in user
    [HttpGet] //GET /api/Documents
    public async Task<IActionResult> GetDocuments()
    {
        //Read the user ID from the authentication cookie
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        //Reject requests without a valid user ID
        if (!Guid.TryParse(userIdValue, out Guid userId))
            return Unauthorized();

        //Get the user's documents from the service
        var documents =
            await _documentService.GetUserDocumentsAsync(userId);

        return Ok(documents);
    }

    //Return information about a specific document
    [HttpGet("{id:guid}")] //GET /api/Documents/{id}
    public async Task<IActionResult> GetDocument(Guid id)
    {
        //Identify the currently authenticated user
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out Guid userId))
            return Unauthorized();

        //Find the document using both its ID and owner
        var document =
            await _documentService.GetDocumentByIdAsync(
                id, userId);

        //Return 404 if the document wasn't found or wasn't owned
        if (document == null)
            return NotFound(new
            {
                message = "Document not found."
            });

        return Ok(document);
    }

    //Retrieve all versions of a document
    [HttpGet("{id:guid}/versions")] //GET /api/Documents/{id}/versions
    public async Task<IActionResult> GetDocumentVersions(
        Guid id)
    {
        //Get the authenticated user's ID
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out Guid userId))
            return Unauthorized();

        try
        {
            //Retrieve all versions belonging to this document
            var versions =
                await _documentService.GetDocumentVersionsAsync(
                    id,
                    userId);

            //Return newest version first
            return Ok(versions);
        }
        catch (KeyNotFoundException)
        {
            //Document doesn't exist or doesn't belong to user
            return NotFound(new
            {
                message = "Document not found."
            });
        }
    }


    //Compare two uploaded document versions using AI
    [HttpPost("compare")] //GET /api/Documents/compare
    public async Task<IActionResult> CompareDocuments(
        [FromBody] DocumentComparisonRequestDto request)
    {
        //Get the authenticated user's ID
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        //Reject requests without a valid authenticated user
        if (!Guid.TryParse(userIdValue, out Guid userId))
            return Unauthorized();

        //Check that both document IDs are provided and different
        if (
            request.PreviousDocumentId == Guid.Empty ||
            request.CurrentDocumentId == Guid.Empty ||
            request.PreviousDocumentId == request.CurrentDocumentId
        )
        {
            return BadRequest(new
            {
                message = "Select two different valid document versions."
            });
        }

        try
        {
            //Compare both documents using the OpenAI service
            DocumentComparisonResultDto result =
                await _comparisonService.CompareAsync(
                    request.PreviousDocumentId,
                    request.CurrentDocumentId,
                    userId
                );

            //Return the AI-generated comparison
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            //Invalid comparison request
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (KeyNotFoundException)
        {
            //One or both documents don't exist or aren't owned
            return NotFound(new
            {
                message = "One or both documents were not found."
            });
        }
        catch (InvalidOperationException ex)
        {
            //Documents aren't ready, are unrelated, or can't be compared
            return Conflict(new
            {
                message = ex.Message
            });
        }
        catch (HttpRequestException)
        {
            //The external AI service failed
            return StatusCode(502, new
            {
                message = "The AI comparison service is unavailable."
            });
        }
        catch (System.Text.Json.JsonException)
        {
            //The external AI response could not be parsed
            return StatusCode(502, new
            {
                message = "The AI comparison returned an invalid response."
            });
        }
    }

    //Soft delete a document by marking it as archived
    [HttpDelete("{id:guid}")] //DELETE /api/Documents/{id}
    public async Task<IActionResult> DeleteDocument(Guid id)
    {
        //Identify the user requesting the deletion
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdValue, out Guid userId))
            return Unauthorized();

        //Archive the document if it belongs to the user
        bool archived =
            await _documentService.ArchiveDocumentAsync(
                id, userId);

        //Return 404 if the document wasn't found or wasn't owned
        if (!archived)
            return NotFound(new
            {
                message = "Document not found."
            });

        return Ok(new
        {
            message = "Document archived successfully."
        });
    }

    //Extract text from a document and save chunks
    [HttpPost("{id:guid}/process")] //POST /api/Documents/{id}/process
    public async Task<IActionResult> ProcessDocument(Guid id)
    {
        //Identify the authenticated user
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        //Reject requests without a valid user ID
        if (!Guid.TryParse(userIdValue, out Guid userId))
            return Unauthorized();

        try
        {
            //Process only a document owned by this user
            int chunkCount =
                await _processingService.ProcessDocumentAsync(
                    id, userId);

            //Return the number of chunks created
            return Ok(new
            {
                message = "Document processed successfully.",
                documentId = id,
                chunksCreated = chunkCount,
                status = "Ready"
            });
        }
        catch (KeyNotFoundException)
        {
            //Document doesn't exist or belongs to another user
            return NotFound(new
            {
                message = "Document not found."
            });
        }
        catch (InvalidOperationException ex)
        {
            //The document is already being processed
            return Conflict(new
            {
                message = ex.Message
            });
        }
        catch (Exception)
        {
            //Don't expose internal file paths or server details
            return Problem(
                title: "Document processing failed.",
                statusCode: 500);
        }
    }

    //Classify an extracted document as General or QA
    [HttpPost("{id:guid}/classify")] //POST /api/Documents/{id}/classify
    public async Task<IActionResult> ClassifyDocument(Guid id)
    {
        //Get the authenticated user's ID
        string? userIdValue = User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        //Reject users without a valid ID
        if (!Guid.TryParse(userIdValue, out Guid userId))
            return Unauthorized();

        try
        {
            //Classify the document and save the result
            string documentType =
                await _classificationService.ClassifyDocumentAsync(
                    id, userId);

            return Ok(new
            {
                message = "Document classified successfully.",
                documentId = id,
                documentType
            });
        }
        catch (KeyNotFoundException)
        {
            //Document does not exist or belongs to someone else
            return NotFound(new
            {
                message = "Document not found."
            });
        }
        catch (InvalidOperationException)
        {
            //Document has not been processed or AI result was invalid
            return Conflict(new
            {
                message = "Document is not ready for classification, "
                        + "or classification could not be completed."
            });
        }
        catch (HttpRequestException)
        {
            //The external AI service failed
            return StatusCode(502, new
            {
                message = "AI classification service is unavailable."
            });
        }
    }


}
