using DocumentSystemApi.Dtos.Chat;
using DocumentSystemApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace DocumentSystemApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly ChatConversationService _conversationService;
        private readonly ChatMessageService _messageService;
        private readonly DocumentChatService _documentChatService;
        private readonly ILogger<ChatController> _logger;

        //Constructor: receive all chat services
        public ChatController(
            ChatConversationService conversationService,
            ChatMessageService messageService,
            DocumentChatService documentChatService,
            ILogger<ChatController> logger)
        {
            _conversationService = conversationService;
            _messageService = messageService;
            _documentChatService = documentChatService;
            _logger = logger;
        }


        //Read the user ID from the authentication cookie
        private Guid? GetUserId()
        {
            //Retrieve the authenticated user's identifier
            string? value = User.FindFirstValue(
                ClaimTypes.NameIdentifier);

            //Convert the identifier into a GUID
            if (Guid.TryParse(value, out Guid userId))
                return userId;

            return null;
        }


        //POST /api/Chat/conversations
        //Create a new conversation about a document
        [HttpPost("conversations")]
        public async Task<IActionResult> CreateConversation(
            [FromBody] CreateConversationDto dto)
        {
            //Identify the authenticated user
            Guid? userId = GetUserId();

            if (userId == null)
                return Unauthorized();

            try
            {
                //Create a new conversation for this document
                var conversation =
                    await _conversationService.CreateConversationAsync(
                        dto.DocumentId,
                        userId.Value,
                        dto.Title);

                //Return 201 Created and the new conversation
                return CreatedAtAction(
                    nameof(GetConversation),
                    new { conversationId = conversation.Id },
                    conversation);
            }
            catch (KeyNotFoundException)
            {
                //Hide documents that do not belong to the user
                return NotFound(new
                {
                    message = "Document not found."
                });
            }
            catch (ArgumentException ex)
            {
                //Return errors for invalid request values
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
            catch (InvalidOperationException ex)
            {
                //The document isn't ready for chat
                return Conflict(new
                {
                    message = ex.Message
                });
            }
        }


        //GET /api/Chat/documents/{documentId}/conversations
        //Retrieve conversations belonging to one document
        [HttpGet("documents/{documentId:guid}/conversations")]
        public async Task<IActionResult> GetDocumentConversations(
            Guid documentId)
        {
            //Identify the logged-in user
            Guid? userId = GetUserId();

            if (userId == null)
                return Unauthorized();

            try
            {
                //Retrieve active conversations for this document
                var conversations =
                    await _conversationService
                        .GetUserConversationsAsync(
                            documentId,
                            userId.Value);

                return Ok(conversations);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new
                {
                    message = "Document not found."
                });
            }
        }


        //GET /api/Chat/conversations/{conversationId}
        //Retrieve one saved conversation
        [HttpGet("conversations/{conversationId:guid}")]
        public async Task<IActionResult> GetConversation(
            Guid conversationId)
        {
            //Identify the logged-in user
            Guid? userId = GetUserId();

            if (userId == null)
                return Unauthorized();

            try
            {
                //Verify ownership and retrieve conversation details
                var conversation =
                    await _conversationService
                        .GetConversationByIdAsync(
                            conversationId,
                            userId.Value);

                return Ok(conversation);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new
                {
                    message = "Conversation not found."
                });
            }
        }


        //GET /api/Chat/conversations/{conversationId}/messages
        //Retrieve previous messages from a conversation
        [HttpGet("conversations/{conversationId:guid}/messages")]
        public async Task<IActionResult> GetMessages(
            Guid conversationId)
        {
            //Identify the logged-in user
            Guid? userId = GetUserId();

            if (userId == null)
                return Unauthorized();

            try
            {
                //Retrieve messages in chronological order
                var messages =
                    await _messageService.GetMessagesAsync(
                        conversationId,
                        userId.Value);

                return Ok(messages);
            }
            catch (KeyNotFoundException)
            {
                return NotFound(new
                {
                    message = "Conversation not found."
                });
            }
        }


        //POST /api/Chat/messages
        //Save the question, generate an answer and save it
        [HttpPost("messages")]
        public async Task<IActionResult> SendMessage(
            [FromBody] ChatRequestDto dto)
        {
            //Identify the authenticated user
            Guid? userId = GetUserId();

            if (userId == null)
                return Unauthorized();

            //Reject empty conversation identifiers
            if (dto.ConversationId == Guid.Empty)
            {
                return BadRequest(new
                {
                    message = "A valid conversation ID is required."
                });
            }

            //Reject questions containing only whitespace
            if (string.IsNullOrWhiteSpace(dto.Message))
            {
                return BadRequest(new
                {
                    message = "Message cannot be empty."
                });
            }

            try
            {
                //Verify the conversation belongs to this user
                var conversation =
                    await _conversationService
                        .GetConversationByIdAsync(
                            dto.ConversationId,
                            userId.Value);

                //Retrieve conversation history before saving the new question
                var previousMessages =
                    await _messageService.GetRecentMessagesAsync(
                        dto.ConversationId,
                        userId.Value,
                        limit: 8);

                //Save the user's question before calling OpenAI
                var userMessage =
                    await _messageService.SaveMessageAsync(
                        dto.ConversationId,
                        userId.Value,
                        "User",
                        dto.Message);

                //Generate an AI answer using the document and conversation history
                var answer =
                    await _documentChatService.AskAsync(
                        conversation.DocumentId,
                        userId.Value,
                        dto.Message,
                        previousMessages);

                //Save the generated answer and its citations
                var assistantMessage =
                    await _messageService.SaveMessageAsync(
                        dto.ConversationId,
                        userId.Value,
                        "Assistant",
                        answer.Answer,
                        answer.Sources,
                        answer.ModelName);

                //Return both newly created messages to React
                return Ok(new
                {
                    userMessage,
                    assistantMessage
                });
            }
            catch (KeyNotFoundException)
            {
                //Conversation or document does not exist
                return NotFound(new
                {
                    message = "Conversation not found."
                });
            }
            catch (ArgumentException ex)
            {
                //Invalid conversation or message data
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
            catch (HttpRequestException ex)
            {
                //Record errors communicating with OpenAI
                _logger.LogError(
                    ex, "Document chat AI request failed.");

                return StatusCode(502, new
                {
                    message = "AI chat service is unavailable."
                });
            }
            catch (System.Text.Json.JsonException ex)
            {
                //Record unexpected AI response errors
                _logger.LogError(
                    ex, "Invalid AI chat response.");

                return StatusCode(502, new
                {
                    message = "AI returned an invalid response."
                });
            }
            catch (InvalidOperationException ex)
            {
                //Record the complete error in the Visual Studio console
                _logger.LogError(ex, "Chat request failed.");

                //Show the actual reason during local development
                return Conflict(new
                {
                    message = "Chat request could not be completed.",

                    //Remove this detail before deploying to production
                    detail = HttpContext.RequestServices
                        .GetRequiredService<IWebHostEnvironment>()
                        .IsDevelopment()
                            ? ex.Message
                            : null
                });
            }
        }
    }
}
