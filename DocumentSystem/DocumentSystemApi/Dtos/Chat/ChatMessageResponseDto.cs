namespace DocumentSystemApi.Dtos.Chat
{
    public class ChatMessageResponseDto
    {
        //Unique identifier of the message
        public Guid Id { get; set; }

        //Conversation this message belongs to
        public Guid ConversationId { get; set; }

        //User or Assistant
        public string Role { get; set; } = "";

        //The actual message text
        public string Content { get; set; } = "";

        //References to document chunks supporting the answer
        public List<ChatSourceDto> Sources { get; set; } = new();

        //Date and time when the message was created
        public DateTimeOffset CreatedAt { get; set; }
    }


    //Represents a document source used by the AI
    public class ChatSourceDto
    {
        //Identifier of the source chunk
        public Guid ChunkId { get; set; }

        //Index of the chunk inside the document
        public int ChunkIndex { get; set; }

        //Original page number when available
        public int? PageNumber { get; set; }

        //Short excerpt from the supporting text
        public string Excerpt { get; set; } = "";
    }
}
