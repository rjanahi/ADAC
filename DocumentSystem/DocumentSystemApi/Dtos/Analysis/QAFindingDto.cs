namespace DocumentSystemApi.Dtos.Analysis
{
    //Represents one issue or observation found in a QA Document
    public class QAFindingDto
    {
        //Short description of the issue
        public string Title { get; set; } = "";

        //Detailed explanation of the issue
        public string Description { get; set; } = "";

        //Type of finding, such as Defect or Risk
        public string Category { get; set; } = "";

        //Severity: Low, Medium, High or Critical
        public string Severity { get; set; } = "";

        //System feature affected by the issue
        public string AffectedArea { get; set; } = "";

        //Potential impact on users or system functionality
        public string Impact { get; set; } = "";

        //Suggested action to resolve or investigate the issue
        public string Recommendation { get; set; } = "";

        //Page number or chunk reference supporting the finding
        public string SourceLocation { get; set; } = "";
    }
}
