namespace DocumentSystemApi.Dtos.Analysis
{
    //Represents one document chunk found by retrieval
    public class RetrievedChunkDto
    {
        //Unique identifier of the chunk
        public Guid ChunkId { get; set; }

        //Position of the chunk inside the document
        public int ChunkIndex { get; set; }

        //The extracted document text
        public string Content { get; set; } = "";

        //Original page number when available
        public int? PageNumber { get; set; }

        //Section heading when available
        public string? SectionTitle { get; set; }

        //Simple keyword relevance score
        public int Score { get; set; }
    }
}
