import { getCsrfToken } from "./authService";

//Base URL for the C# AnalysisController
const API_URL = "/api/Analysis";

export const analysisService = {

    //Generate a new general analysis for a document
    async generateGeneralAnalysis(documentId) {

        //Get a CSRF token for this authenticated session
        const csrfToken = await getCsrfToken();

        //Ask the backend to generate and save an analysis
        const response = await fetch(
            `${API_URL}/${documentId}/general`,
            {
                method: "POST",

                //Send the user's authentication cookie
                credentials: "include",

                //Protect the request from CSRF attacks
                headers: {
                    "X-CSRF-TOKEN": csrfToken,
                },
            }
        );

        //Read the response
        const data = await response.json().catch(() => null);

        //Show an error if analysis failed
        if (!response.ok) {
            throw new Error(
                data?.message ||
                `Analysis failed (${response.status}).`
            );
        }

        return data;
    },


    //Generate a specialized QA analysis for a document
    async generateQAAnalysis(documentId) {

        //Get the CSRF token for the authenticated user
        const csrfToken = await getCsrfToken();

        //Send the QA analysis request to the C# backend
        const response = await fetch(
            `${API_URL}/${documentId}/qa`,
            {
                method: "POST",

                //Include the authentication cookie
                credentials: "include",

                //Protect the request using the CSRF token
                headers: {
                    "X-CSRF-TOKEN": csrfToken,
                },
            }
        );

        //Read the JSON response returned by the backend
        const data = await response.json().catch(() => null);

        //Handle unsuccessful analysis requests
        if (!response.ok) {
            throw new Error(
                data?.message ||
                `QA analysis failed (${response.status}).`
            );
        }

        //Return information about the saved analysis
        return data;
    },



    //Get previous analyses belonging to one document
    async getAnalysisHistory(documentId) {

        //GET requests do not require a CSRF token
        const response = await fetch(
            `${API_URL}/${documentId}`,
            {
                credentials: "include",
            }
        );

        if (!response.ok) {
            throw new Error("Could not load analysis history.");
        }

        return await response.json();
    },


    //Get the full results of one saved analysis
    async getAnalysisResult(analysisId) {

        const response = await fetch(
            `${API_URL}/result/${analysisId}`,
            {
                credentials: "include",
            }
        );

        if (!response.ok) {
            throw new Error("Could not load analysis results.");
        }

        return await response.json();
    },


    //Download a PDF of a saved analysis version
    async downloadAnalysisPdf(analysisId) {

        if (!analysisId) {
            throw new Error("No analysis selected.");
         }

        //Request the PDF using the existing login session
        const response = await fetch(
            `${API_URL}/${encodeURIComponent(analysisId)}/pdf`,
            {
                method: "GET",
                credentials: "include",
            }
        );

        if (!response.ok) {
            const errorData = await response.json().catch(() => null);

            throw new Error(
                errorData?.message ||
                `PDF download failed (${response.status}).`
            );
        }

        //Convert the response into a downloadable file
        const pdfBlob = await response.blob();

        //Ensure the server returned a PDF
        if (!response.headers
            .get("Content-Type")
            ?.toLowerCase()
            .includes("application/pdf")) {
            throw new Error("The server did not return a PDF.");
        }

        //Create a temporary browser URL
        const url = window.URL.createObjectURL(pdfBlob);

        try {
            //Create a temporary download link
            const link = document.createElement("a");

            link.href = url;
            link.download = `Analysis_${analysisId}.pdf`;

            document.body.appendChild(link);

            //Start the download
            link.click();

            //Remove the temporary link
            link.remove();
        } finally {
            //Release the temporary object URL
            window.URL.revokeObjectURL(url);
        }
    },

};
