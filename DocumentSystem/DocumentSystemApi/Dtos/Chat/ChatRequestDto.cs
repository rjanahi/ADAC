using System.ComponentModel.DataAnnotations;

namespace DocumentSystemApi.Dtos.Chat
{
    //Represents a question sent by the user
    public class ChatRequestDto : IValidatableObject
    {
        //Conversation where the message belongs
        public Guid ConversationId { get; set; }

        //Question the user wants to ask
        [Required]
        [StringLength(2000, MinimumLength = 1)]
        public string Message { get; set; } = "";

        //Validate information that attributes alone cannot check
        public IEnumerable<ValidationResult> Validate(
            ValidationContext validationContext)
        {
            //Reject an empty conversation identifier
            if (ConversationId == Guid.Empty)
            {
                yield return new ValidationResult(
                    "A valid conversation ID is required.",
                    new[] { nameof(ConversationId) });
            }

            //Reject messages containing only whitespace
            if (string.IsNullOrWhiteSpace(Message))
            {
                yield return new ValidationResult(
                    "Message cannot be empty.",
                    new[] { nameof(Message) });
            }
        }
    }
}
