namespace DocumentSystemApi.Dtos.Analysis
{
    //Represents the complete result of a QA document analysis
    public class QAAnalysisResultDto
    {
        //Overall evaluation of the software testing results
        public string OverallAssessment { get; set; } = "";

        //Total number of test cases Documented
        //Null means the document does not provide this information
        public int? TotalTests { get; set; }

        //Number of successful test cases
        public int? PassedTests { get; set; }

        //Number of failed test cases
        public int? FailedTests { get; set; }

        //Number of blocked test cases
        public int? BlockedTests { get; set; }

        //Number of skipped test cases
        public int? SkippedTests { get; set; }

        //Important quality risks found in the Document
        public List<string> KeyRisks { get; set; } = new();

        //Recommended actions to improve software quality
        public List<string> Recommendations { get; set; } = new();

        //Individual defects or QA findings
        public List<QAFindingDto> Findings { get; set; } = new();
    }
}
