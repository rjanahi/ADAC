
using System.ComponentModel.DataAnnotations;

namespace DocumentSystemApi.Dtos
{
    //Represents a request to create a new chat conversation
    public class CreateConversationDto
    {
        //The document the user wants to chat about
        [Required]
        public Guid DocumentId { get; set; }

        //Optional name for the conversation
        [StringLength(200)]
        public string? Title { get; set; }
    }
}
