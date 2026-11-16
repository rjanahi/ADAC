
using DocumentSystemApi.Data;
using DocumentSystemApi.Dtos;
using DocumentSystemApi.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DocumentSystemApi.Services
{
    public class ChatMessageService
    {
        private readonly DocumentDbContext _context;

        //Constructor: receive the database context
        public ChatMessageService(DocumentDbContext context)
        {
            _context = context;
        }


        //Save a message inside an existing conversation
        public async Task<ChatMessageResponseDto> SaveMessageAsync(
            Guid conversationId,
            Guid userId,
            string role,
            string content,
            List<ChatSourceDto>? sources = null,
            string? modelName = null)
        {
            //Reject empty identifiers
            if (conversationId == Guid.Empty ||
                userId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Valid conversation and user IDs are required.");
            }

            //Only accept roles supported by our database
            if (role != "User" && role != "Assistant")
                throw new ArgumentException("Invalid message role.");

            //Reject empty messages
            if (string.IsNullOrWhiteSpace(content))
                throw new ArgumentException(
                    "Message cannot be empty.");

            //Find the conversation and verify ownership
            var conversation = await _context.ChatConversations
                .FirstOrDefaultAsync(c =>
                    c.Id == conversationId &&
                    c.UserId == userId &&
                    !c.IsArchived &&
                    _context.Documents.Any(d =>
                        d.Id == c.DocumentId &&
                        d.UserId == userId &&
                        !d.IsArchived));

            //Don't allow messages in unauthorized conversations
            if (conversation == null)
                throw new KeyNotFoundException(
                    "Conversation not found.");

            //Prepare the source list
            var messageSources = sources ?? new List<ChatSourceDto>();

            //User messages should not contain AI citations
            if (role == "User" && messageSources.Count > 0)
                throw new ArgumentException(
                    "User messages cannot contain AI citations.");

            //Verify that all cited chunks belong to this document
            if (messageSources.Count > 0)
            {
                var sourceIds = messageSources
                    .Select(s => s.ChunkId)
                    .Distinct()
                    .ToList();

                //Count matching chunks for the conversation document
                int validCount = await _context.DocumentChunks
                    .CountAsync(c =>
                        c.DocumentId == conversation.DocumentId &&
                        sourceIds.Contains(c.Id));

                //Reject citations pointing to another document
                if (validCount != sourceIds.Count ||
                    messageSources.Any(s =>
                        s.ChunkId == Guid.Empty))
                {
                    throw new ArgumentException(
                        "One or more source references are invalid.");
                }
            }

            //Record the current UTC time
            var now = DateTimeOffset.UtcNow;

            //Create a new chat message entity
            var message = new ChatMessage
            {
                //Generate a unique message ID
                Id = Guid.NewGuid(),

                //Associate the message with its conversation
                ConversationId = conversationId,

                //Store the message role and text
                Role = role,
                Content = content.Trim(),

                //Serialize citations as JSON for assistant messages
                SourceReferences = messageSources.Count > 0
                    ? JsonSerializer.Serialize(messageSources)
                    : null,

                //Store the AI model when available
                ModelName = modelName,

                //Save the creation timestamp
                CreatedAt = now
            };

            //Add the message to the database
            _context.ChatMessages.Add(message);

            //Update the conversation's latest activity timestamp
            conversation.UpdatedAt = now;

            //Save both changes together
            await _context.SaveChangesAsync();

            //Return a safe response DTO
            return ToResponseDto(message);
        }


        //Get all saved messages from one conversation
        public async Task<List<ChatMessageResponseDto>>
            GetMessagesAsync(Guid conversationId, Guid userId)
        {
            //Verify ownership through the conversation and document
            bool conversationExists =
                await _context.ChatConversations
                    .AnyAsync(c =>
                        c.Id == conversationId &&
                        c.UserId == userId &&
                        !c.IsArchived &&
                        _context.Documents.Any(d =>
                            d.Id == c.DocumentId &&
                            d.UserId == userId &&
                            !d.IsArchived));

            if (!conversationExists)
                throw new KeyNotFoundException(
                    "Conversation not found.");

            //Retrieve messages in chronological order
            var messages = await _context.ChatMessages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync();

            //Convert database entities into response DTOs
            return messages
                .Select(ToResponseDto)
                .ToList();
        }


        //Convert one saved message into a response DTO
        private static ChatMessageResponseDto ToResponseDto(
            ChatMessage message)
        {
            //Start with an empty list of references
            var sources = new List<ChatSourceDto>();

            //Deserialize the saved JSON when references exist
            if (!string.IsNullOrWhiteSpace(
                message.SourceReferences))
            {
                sources = JsonSerializer.Deserialize<
                    List<ChatSourceDto>>(
                        message.SourceReferences) ?? new();
            }

            //Return information needed by React
            return new ChatMessageResponseDto
            {
                Id = message.Id,
                ConversationId = message.ConversationId,
                Role = message.Role,
                Content = message.Content,
                Sources = sources,
                CreatedAt = message.CreatedAt
            };
        }
    }
}
