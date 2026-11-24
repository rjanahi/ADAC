namespace DocumentSystemApi.Dtos.Document
{

    //The complete AI comparison between two document versions
    public class DocumentComparisonResultDto
    {
        //The documents that were compared
        public Guid PreviousDocumentId { get; set; }
        public Guid CurrentDocumentId { get; set; }

        //Brief explanation of the main differences
        public string Summary { get; set; } = string.Empty;

        //Individual changes identified by the AI
        public List<DocumentChangeDto> Changes { get; set; } = new();

        //Name of the AI model used
        public string ModelName { get; set; } = string.Empty;
    }


    //One meaningful change between document versions
    public class DocumentChangeDto
    {
        //Added, Removed, Modified, or Unchanged
        public string ChangeType { get; set; } = string.Empty;

        //Short description of the subject
        public string Title { get; set; } = string.Empty;

        //Explanation of what changed
        public string Description { get; set; } = string.Empty;

        //Relevant text from the earlier document
        public string? PreviousExcerpt { get; set; }

        //Relevant text from the updated document
        public string? CurrentExcerpt { get; set; }

        //Optional location information
        public string? PreviousLocation { get; set; }
        public string? CurrentLocation { get; set; }
    }
}