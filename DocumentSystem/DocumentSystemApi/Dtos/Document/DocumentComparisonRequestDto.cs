using System.ComponentModel.DataAnnotations;

namespace DocumentSystemApi.Dtos.Document
{

    public class DocumentComparisonRequestDto
    {
        //The earlier uploaded document version
        [Required]
        public Guid PreviousDocumentId { get; set; }

        //The updated document version
        [Required]
        public Guid CurrentDocumentId { get; set; }
    }
}