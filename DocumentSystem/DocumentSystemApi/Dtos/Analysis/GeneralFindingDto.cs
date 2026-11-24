namespace DocumentSystemApi.Dtos.Analysis
{
    //Represents one finding identified by the AI
    public class GeneralFindingDto
    {
        //Short name describing the finding
        public string Title { get; set; } = "";

        //Detailed explanation of the finding
        public string Description { get; set; } = "";

        //Type of finding, such as Risk or Observation
        public string Category { get; set; } = "";

        //Importance level: Low, Medium, High or Critical
        public string Severity { get; set; } = "";

        //Possible effect of the finding
        public string Impact { get; set; } = "";

        //Suggested action to address the finding
        public string Recommendation { get; set; } = "";

        //Location of supporting text, when available
        public string SourceLocation { get; set; } = "";
    }
}
