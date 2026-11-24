namespace DocumentSystemApi.Dtos.Chat
{
    //Represents conversation details returned to React
    public class ChatConversationResponseDto
    {
        //Unique conversation identifier
        public Guid Id { get; set; }

        //Document associated with this conversation
        public Guid DocumentId { get; set; }

        //Conversation owner
        public Guid UserId { get; set; }

        //Title displayed in the chat interface
        public string Title { get; set; } = "";

        //When the conversation was created
        public DateTimeOffset CreatedAt { get; set; }

        //When the conversation was last updated
        public DateTimeOffset? UpdatedAt { get; set; }
    }

}
