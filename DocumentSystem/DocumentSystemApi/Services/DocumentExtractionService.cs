
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentSystemApi.Dtos;
using UglyToad.PdfPig; //for pdf extraction
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor; 

namespace DocumentSystemApi.Services
{
    public class DocumentExtractionService
    {
        //Main method for extracting text from a document
        public ExtractionResult ExtractText(
            string filePath)
        {
            //Check whether the document exists
            if (!File.Exists(filePath))
                throw new FileNotFoundException(
                    "Document file was not found.");

            //Identify the document format
            string extension = Path.GetExtension(filePath)
                .ToLowerInvariant();

            //Call the correct extraction method
            return extension switch
            {
                ".txt" => ExtractTxt(filePath),
                ".pdf" => ExtractPdf(filePath),
                ".docx" => ExtractDocx(filePath),

                _ => throw new NotSupportedException(
                    "Unsupported document format.")
            };
        }


        //Extract text from a TXT document
        private ExtractionResult ExtractTxt(
            string filePath)
        {
            //Read the file using strict UTF-8 decoding
            var encoding = new UTF8Encoding(false, true);

            //Turn from byte to readable string
            string text = encoding.GetString(
                File.ReadAllBytes(filePath));

            //TXT files do not have real page numbers
            return new ExtractionResult
            {
                FullText = text,

                Sections = new List<ExtractedSection>
                {
                    new ExtractedSection
                    {
                        PageNumber = null,
                        Text = text
                    }
                }
            };
        }


        //Extract text from a PDF document
        private ExtractionResult ExtractPdf(
            string filePath)
        {
            var result = new ExtractionResult();

            var fullText = new StringBuilder();

            //Open the PDF file
            using var document = PdfDocument.Open(filePath);

            //Read each page separately
            foreach (var page in document.GetPages())
            {
                //Extract text using PDF content positioning
                string pageText =
                    ContentOrderTextExtractor.GetText(page);

                //Save the text with its original page number
                result.Sections.Add(new ExtractedSection
                {
                    PageNumber = page.Number,
                    Text = pageText
                });

                //Combine all pages into one text result
                fullText.AppendLine(pageText);
            }

            result.FullText = fullText.ToString();

            return result;
        }



        //Extract readable text from a Microsoft Word document
        private ExtractionResult ExtractDocx(string filePath)
        {
            //Open the Word document in read-only mode
            using var document =
                WordprocessingDocument.Open(filePath, false);

            //Locate the main document body
            var body = document.MainDocumentPart?
                .Document?
                .Body;

            //Reject documents without a readable body
            if (body == null)
                throw new InvalidDataException(
                    "The Word document has no readable body.");

            //Build the extracted document text
            var textBuilder = new StringBuilder();

            //Read paragraphs, including paragraphs inside tables
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                //Extract actual Word text elements only
                //This excludes field instructions such as PAGEREF
                var textParts = paragraph
                    .Descendants<Text>()
                    .Select(t => t.Text);

                //Combine the text elements into one paragraph
                string paragraphText = string.Concat(textParts);

                //Skip empty paragraphs
                if (string.IsNullOrWhiteSpace(paragraphText))
                    continue;

                //Preserve paragraph boundaries
                textBuilder.AppendLine(paragraphText.Trim());
            }

            //Get the final extracted text
            string text = textBuilder.ToString().Trim();

            //Reject documents with no extractable text
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException(
                    "The Word document contains no extractable text.");

            //DOCX does not provide reliable page positions
            return new ExtractionResult
            {
                FullText = text,

                Sections = new List<ExtractedSection>
            {
                new ExtractedSection
                {
                    PageNumber = null,
                    Text = text
                }
            }
            };
        }
    }
}
