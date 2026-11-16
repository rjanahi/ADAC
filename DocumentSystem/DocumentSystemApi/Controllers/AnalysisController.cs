
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DocumentSystemApi.Data;
using DocumentSystemApi.Services;
using System.Security.Claims;
using System.Text.Json;
using System.Collections.Concurrent;

namespace DocumentSystemApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")] // /api/Analysis
    public class AnalysisController : ControllerBase
    {
        private readonly GeneralAnalysisService _analysisService;
        private readonly AnalysisPersistenceService _persistenceService;
        private readonly DocumentDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly QAAnalysisService _qaAnalysisService;
        private readonly AnalysisPdfService _analysisPdfService;
        //Coordinate analysis requests for each document
        private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> AnalysisLocks = new();

        //Constructor: receive required services
        public AnalysisController(
          GeneralAnalysisService analysisService,
          QAAnalysisService qaAnalysisService,
          AnalysisPersistenceService persistenceService,
          DocumentDbContext context,
          IConfiguration configuration,
          AnalysisPdfService analysisPdfService)
        {
            //Service for analyzing general documents
            _analysisService = analysisService;

            //Service for analyzing QA Documents
            _qaAnalysisService = qaAnalysisService;

            //Service for saving generated results
            _persistenceService = persistenceService;

            //Database connection
            _context = context;

            //Application settings, including AI model name
            _configuration = configuration;

            //
            _analysisPdfService = analysisPdfService;
        }


        //Get the ID of the authenticated user
        private Guid? GetUserId()
        {
            //Read user ID from authentication claims
            string? userIdValue = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            //Convert the claim to a Guid
            if (Guid.TryParse(userIdValue, out Guid userId))
                return userId;

            //Return null if there is no valid ID
            return null;
        }


        //Generate and save a general document analysis
        [HttpPost("{documentId:guid}/general")] //POST /api/Analysis/{id}/general
        public async Task<IActionResult> GenerateGeneralAnalysis(
            Guid documentId)
        {
            //Identify the logged-in user
            Guid? userId = GetUserId();

            if (userId == null)
                return Unauthorized();

            //Get the lock associated with this document
            var analysisLock = AnalysisLocks.GetOrAdd(
                documentId,
                _ => new SemaphoreSlim(1, 1));

            //Wait for another analysis of this document to finish
            await analysisLock.WaitAsync();


            try
            {

                //Check whether this document already has a General analysis
                Guid? existingAnalysisId =
                    await GetExistingAnalysisIdAsync(
                        documentId,
                        userId.Value,
                        "General");

                //Reuse the existing result instead of generating another
                if (existingAnalysisId.HasValue)
                {
                    return Ok(new
                    {
                        analysisId = existingAnalysisId.Value,
                        message = "This document already has a saved analysis.",
                        reused = true
                    });
                }

                //Generate the AI analysis
                var result = await _analysisService.AnalyzeAsync(
                    documentId,
                    userId.Value);

                //Get the configured AI model name
                string modelName =
                    _configuration["OpenAI:Model"]
                    ?? "Not specified";

                //Save the analysis and findings in SQL Server
                var savedAnalysis =
                    await _persistenceService.SaveGeneralAnalysisAsync(
                        documentId,
                        userId.Value,
                        result,
                        modelName);

                //Return information about the saved analysis
                return Ok(new
                {
                    message = "Analysis completed successfully.",
                    analysisId = savedAnalysis.Id,
                    documentId = savedAnalysis.DocumentId,
                    analysisType = savedAnalysis.AnalysisType,
                    version = savedAnalysis.Version,
                    status = savedAnalysis.Status,
                    findingsCount = result.Findings?.Count ?? 0
                });
            }
            catch (KeyNotFoundException)
            {
                //Document not found or belongs to another user
                return NotFound(new
                {
                    message = "Document not found."
                });
            }
            catch (InvalidOperationException)
            {
                //Document not ready or AI result invalid
                return Conflict(new
                {
                    message = "Analysis could not be completed.",
                    detail = "Check the document processing status."
                });
            }
            catch (HttpRequestException)
            {
                //External AI request failed
                return StatusCode(502, new
                {
                    message = "AI analysis service is unavailable."
                });
            }
            catch (JsonException)
            {
                //AI returned an unexpected JSON response
                return StatusCode(502, new
                {
                    message = "AI returned an invalid analysis response."
                });
            }
            finally
            {
                //Allow the next request to check for a saved analysis
                analysisLock.Release();
            }
        }


       
        //Get the analysis history for a document
        [HttpGet("{documentId:guid}")] //GET /api/Analysis/{id}
        public async Task<IActionResult> GetAnalysisHistory(
            Guid documentId)
        {
            //Identify the logged-in user
            Guid? userId = GetUserId();

            if (userId == null)
                return Unauthorized();

            //Check whether the document belongs to the user
            bool documentExists = await _context.Documents
                .AnyAsync(r =>
                    r.Id == documentId &&
                    r.UserId == userId.Value &&
                    !r.IsArchived);

            //Do not expose documents owned by other users
            if (!documentExists)
                return NotFound(new
                {
                    message = "Document not found."
                });

            //Retrieve saved analysis records for this document
            var analyses = await _context.DocumentAnalyses
                .Where(a => a.DocumentId == documentId)

                //Newest analyses first
                .OrderByDescending(a => a.CreatedAt)

                //Return only fields needed for history
                .Select(a => new
                {
                    analysisId = a.Id,
                    analysisType = a.AnalysisType,
                    version = a.Version,
                    status = a.Status,
                    modelName = a.ModelName,
                    createdAt = a.CreatedAt,
                    completedAt = a.CompletedAt
                })
                .ToListAsync();

            return Ok(analyses);
        }


        
        //Get one analysis together with its findings
        [HttpGet("result/{analysisId:guid}")]//GET /api/Analysis/result/{id}
        public async Task<IActionResult> GetAnalysisResult(
            Guid analysisId)
        {
            //Identify the logged-in user
            Guid? userId = GetUserId();

            if (userId == null)
                return Unauthorized();

            //Find the analysis and verify ownership
            //through the Documents table
            var analysis = await _context.DocumentAnalyses
                .Where(a =>
                    a.Id == analysisId &&
                    _context.Documents.Any(r =>
                        r.Id == a.DocumentId &&
                        r.UserId == userId.Value &&
                        !r.IsArchived))
                .FirstOrDefaultAsync();

            if (analysis == null)
                return NotFound(new
                {
                    message = "Analysis not found."
                });

            //Retrieve findings belonging to this analysis
            var findings = await _context.DocumentFindings
                .Where(f => f.AnalysisId == analysisId)
                .OrderBy(f => f.CreatedAt)
                .Select(f => new
                {
                    f.Id,
                    f.Title,
                    f.Description,
                    f.Category,
                    f.Severity,
                    f.AffectedArea,
                    f.Impact,
                    f.Recommendation,
                    f.SourceLocation
                })
                .ToListAsync();

            //Return analysis information and its findings
            return Ok(new
            {
                analysisId = analysis.Id,
                documentId = analysis.DocumentId,
                analysisType = analysis.AnalysisType,
                version = analysis.Version,
                status = analysis.Status,
                overallAssessment = analysis.OverallAssessment,

                //Return test statistics for QA analyses
                //These values will be null for general analyses
                totalTests = analysis.TotalTests,
                passedTests = analysis.PassedTests,
                failedTests = analysis.FailedTests,
                blockedTests = analysis.BlockedTests,
                skippedTests = analysis.SkippedTests,

                //Convert stored JSON lists back into arrays
                //Assessment and recommendations
                keyRisks = DeserializeList(analysis.KeyRisks),
                recommendations = DeserializeList(analysis.Recommendations),

                //Additional analysis information
                modelName = analysis.ModelName,
                createdAt = analysis.CreatedAt,
                completedAt = analysis.CompletedAt,
                findings
            });
        }


        //Convert a JSON string into a C# string list
        private static List<string> DeserializeList(
            string? json)
        {
            //Return an empty list if the column is empty
            if (string.IsNullOrWhiteSpace(json))
                return new List<string>();

            try
            {
                //Convert stored JSON into a list
                return JsonSerializer.Deserialize<List<string>>(json)
                    ?? new List<string>();
            }
            catch (JsonException)
            {
                //Handle malformed JSON without failing the request
                return new List<string>();
            }
        }


        //POST /api/Analysis/{documentId}/qa
        //Generate and save a specialized QA Document analysis
        [HttpPost("{documentId:guid}/qa")] //POST /api/Analysis/{id}/qa
        public async Task<IActionResult> GenerateQAAnalysis(
            Guid documentId)
        {
            //Get the authenticated user's ID
            Guid? userId = GetUserId();

            //Reject requests without a valid user ID
            if (userId == null)
                return Unauthorized();

            var analysisLock = AnalysisLocks.GetOrAdd(
                documentId,
                _ => new SemaphoreSlim(1, 1));

            await analysisLock.WaitAsync();

            try
            {

                //Check whether this document already has a QA analysis
                Guid? existingAnalysisId =
                    await GetExistingAnalysisIdAsync(
                        documentId,
                        userId.Value,
                        "QA");

                if (existingAnalysisId.HasValue)
                {
                    return Ok(new
                    {
                        analysisId = existingAnalysisId.Value,
                        documentId,
                        analysisType = "General",
                        status = "Completed",
                        message = "Existing analysis retrieved successfully.",
                        reused = true
                    });
                }


                //Generate the QA analysis using OpenAI
                var result = await _qaAnalysisService.AnalyzeAsync(
                    documentId,
                    userId.Value);

                //Get the model name from configuration
                string modelName =
                    _configuration["OpenAI:Model"]
                    ?? "Not specified";

                //Save the analysis and its findings to SQL Server
                var savedAnalysis =
                    await _persistenceService.SaveQAAnalysisAsync(
                        documentId,
                        userId.Value,
                        result,
                        modelName);

                //Return information about the completed analysis
                return Ok(new
                {
                    message = "QA analysis completed successfully.",

                    //Unique identifier of the saved analysis
                    analysisId = savedAnalysis.Id,

                    //Document associated with this analysis
                    documentId = savedAnalysis.DocumentId,

                    //Identify the analysis type
                    analysisType = savedAnalysis.AnalysisType,

                    //Version of this analysis
                    version = savedAnalysis.Version,

                    //Current analysis status
                    status = savedAnalysis.Status,

                    //Number of findings generated
                    findingsCount = result.Findings?.Count ?? 0,

                    //Test execution statistics
                    totalTests = result.TotalTests,
                    passedTests = result.PassedTests,
                    failedTests = result.FailedTests,
                    blockedTests = result.BlockedTests,
                    skippedTests = result.SkippedTests
                });
            }
            catch (KeyNotFoundException)
            {
                //Return 404 for documents that don't exist
                //or belong to another user
                return NotFound(new
                {
                    message = "Document not found."
                });
            }
            catch (InvalidOperationException)
            {
                //Document may not be processed or classified as QA
                return Conflict(new
                {
                    message = "QA analysis could not be completed.",
                    detail = "Verify that the document is Ready "
                           + "and classified as QA."
                });
            }
            catch (HttpRequestException)
            {
                //Handle problems communicating with the AI API
                return StatusCode(502, new
                {
                    message = "AI analysis service is unavailable."
                });
            }
            catch (System.Text.Json.JsonException)
            {
                //Handle malformed AI responses
                return StatusCode(502, new
                {
                    message = "AI returned an invalid response."
                });
            }
            finally
            {
                analysisLock.Release();
            }
        }


        //Download a PDF of a specific saved analysis version
        [HttpGet("{analysisId:guid}/pdf")] //GET /api/Analysis/{id}/pdf
        public async Task<IActionResult> DownloadAnalysisPdf(
            Guid analysisId)
        {
            //Get the authenticated user's identity
            string? userIdValue = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (!Guid.TryParse(userIdValue, out Guid userId))
            {
                return Unauthorized();
            }

            try
            {
                //Generate the PDF for the selected analysis
                byte[] pdfBytes =
                    await _analysisPdfService.ExportAnalysisAsync(
                        analysisId,
                        userId);

                //Return a downloadable PDF file
                return File(
                    pdfBytes,
                    "application/pdf",
                    $"Analysis_{analysisId:N}.pdf");
            }
            catch (ArgumentException)
            {
                return BadRequest(new
                {
                    message = "Invalid analysis identifier."
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new
                {
                    message = "Analysis not found."
                });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new
                {
                    message = ex.Message
                });
            }
        }


        //Find the primary completed analysis for a document version
        private async Task<Guid?> GetExistingAnalysisIdAsync(
            Guid documentId,
            Guid userId,
            string analysisType)
        {
            //Confirm ownership through the associated document
            var existingAnalysis = await (
                from analysis in _context.DocumentAnalyses.AsNoTracking()
                join document in _context.Documents.AsNoTracking()
                    on analysis.DocumentId equals document.Id
                where document.Id == documentId
                    && document.UserId == userId
                    && !document.IsArchived
                    && analysis.AnalysisType == analysisType
                    && analysis.Status == "Completed"
                orderby analysis.Version descending
                select (Guid?)analysis.Id
            ).FirstOrDefaultAsync();

            return existingAnalysis;
        }

    }
}
