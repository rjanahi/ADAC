
using DocumentSystemApi.Dtos;
using DocumentSystemApi.Models;
using Microsoft.EntityFrameworkCore;
using DocumentSystemApi.Data;

namespace QAReportSystemApi.Services
{
    public class ChatConversationService
    {
        private readonly DocumentDbContext _context;

        //Constructor: receive the database context
        public ChatConversationService(
            DocumentDbContext context)
        {
            _context = context;
        }


        //Create a new conversation for an uploaded document
        public async Task<ChatConversationResponseDto>
            CreateConversationAsync(
                Guid documentId,
                Guid userId,
                string? title)
        {
            //Reject empty identifiers
            if (documentId == Guid.Empty ||
                userId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Valid document and user IDs are required.");
            }

            //Find the document and verify ownership
            var document = await _context.Documents
                .FirstOrDefaultAsync(d =>
                    d.Id == documentId &&
                    d.UserId == userId &&
                    !d.IsArchived);

            //Reject missing or unauthorized documents
            if (document == null)
                throw new KeyNotFoundException(
                    "Document not found.");

            //Only allow chat when document processing is complete
            if (document.Status != "Ready")
                throw new InvalidOperationException(
                    "Document must be processed before chatting.");

            //Make sure extracted text exists
            bool hasChunks = await _context.DocumentChunks
                .AnyAsync(c => c.DocumentId == documentId);

            if (!hasChunks)
                throw new InvalidOperationException(
                    "Document has no extracted text.");

            //Create a default title if none was provided
            string conversationTitle =
                string.IsNullOrWhiteSpace(title)
                    ? "New Conversation"
                    : title.Trim();

            //Validate the title against the database limit
            if (conversationTitle.Length > 200)
                throw new ArgumentException(
                    "Conversation title cannot exceed 200 characters.");

            //Get the current UTC timestamp
            var now = DateTimeOffset.UtcNow;

            //Create the database entity
            var conversation = new ChatConversation
            {
                //Generate a unique conversation ID
                Id = Guid.NewGuid(),

                //Associate the conversation with its document
                DocumentId = documentId,

                //Store the authenticated user's ID
                UserId = userId,

                //Store the conversation title
                Title = conversationTitle,

                //Set timestamps
                CreatedAt = now,
                UpdatedAt = now,

                //New conversations are not archived
                IsArchived = false
            };

            //Add the conversation to Entity Framework
            _context.ChatConversations.Add(conversation);

            //Insert the conversation into SQL Server
            await _context.SaveChangesAsync();

            //Return the newly created conversation
            return ToResponseDto(conversation);
        }


        //Get active conversations for one document
        public async Task<List<ChatConversationResponseDto>>
            GetUserConversationsAsync(
                Guid documentId,
                Guid userId)
        {
            //Verify that the document belongs to this user
            bool documentExists = await _context.Documents
                .AnyAsync(d =>
                    d.Id == documentId &&
                    d.UserId == userId &&
                    !d.IsArchived);

            if (!documentExists)
                throw new KeyNotFoundException(
                    "Document not found.");

            //Retrieve conversations belonging to this user
            var conversations = await _context.ChatConversations
                
                .Where(c =>
                    c.DocumentId == documentId &&
                    c.UserId == userId &&
                    !c.IsArchived)

                //Show the most recently updated conversations first
                .OrderByDescending(c =>
                    c.UpdatedAt ?? c.CreatedAt)

                //Convert database records into response DTOs
                .Select(c => new ChatConversationResponseDto
                {
                    Id = c.Id,
                    DocumentId = c.DocumentId,
                    UserId = c.UserId,
                    Title = c.Title,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .ToListAsync();

            return conversations;
        }


        //Retrieve one conversation using its ID
        public async Task<ChatConversationResponseDto>
            GetConversationByIdAsync(
                Guid conversationId,
                Guid userId)
        {
            //Find the requested conversation
            var conversation = await _context.ChatConversations
                .Where(c =>
                    c.Id == conversationId &&
                    c.UserId == userId &&
                    !c.IsArchived &&

                    //Ensure the parent document is still accessible
                    _context.Documents.Any(d =>
                        d.Id == c.DocumentId &&
                        d.UserId == userId &&
                        !d.IsArchived))

                //Return only properties required by React
                .Select(c => new ChatConversationResponseDto
                {
                    Id = c.Id,
                    DocumentId = c.DocumentId,
                    UserId = c.UserId,
                    Title = c.Title,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                })
                .FirstOrDefaultAsync();

            //Don't expose conversations belonging to other users
            if (conversation == null)
                throw new KeyNotFoundException(
                    "Conversation not found.");

            return conversation;
        }


        //Convert a conversation entity into a response DTO
        private static ChatConversationResponseDto ToResponseDto(
            ChatConversation conversation)
        {
            return new ChatConversationResponseDto
            {
                Id = conversation.Id,
                DocumentId = conversation.DocumentId,
                UserId = conversation.UserId,
                Title = conversation.Title,
                CreatedAt = conversation.CreatedAt,
                UpdatedAt = conversation.UpdatedAt
            };
        }
    }
}
