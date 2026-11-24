namespace DocumentSystemApi.Dtos.Analysis
{
    //Represents the complete result of a general AI analysis
    public class GeneralAnalysisResultDto
    {
        //Short explanation of what the document is about
        public string Overview { get; set; } = "";

        //Overall evaluation of the document
        public string OverallAssessment { get; set; } = "";

        //Important concerns identified in the document
        public List<string> KeyRisks { get; set; } = new();

        //Suggested actions based on the document
        public List<string> Recommendations { get; set; } = new();

        //Individual findings discovered during analysis
        public List<GeneralFindingDto> Findings { get; set; } = new();
    }
}
