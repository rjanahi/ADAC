
import { getCsrfToken } from "./authService";

//Base address for document endpoints
const API_URL = "/api/Documents";

export const documentService = {

    // Get documents belonging to the logged-in user
    async getDocuments(isArchived = false) {

        // Send the archive filter as a query parameter
        const response = await fetch(
            `${API_URL}?isArchived=${isArchived}`,
            {
                method: "GET",
                credentials: "include",
            }
        );

        if (!response.ok) {
            throw new Error("Failed to load documents.");
        }

        return await response.json();
    },


    //Upload a selected document to the backend
    async uploadDocument(file, title) {

        //Get a valid CSRF request token
        const csrfToken = await getCsrfToken();

        //Create multipart form data
        const formData = new FormData();

        //Add the document's title
        formData.append("title", title);

        //Add the actual file
        formData.append("file", file);

        //Send document to the upload endpoint
        const response = await fetch(`${API_URL}/upload`, {
            method: "POST",

            //Include the user's authentication cookie
            credentials: "include",

            //Send the antiforgery request token
            headers: {
                "X-CSRF-TOKEN": csrfToken,
            },

            //Send the file and title
            body: formData,
        });

        //Read the response from the API
        const data = await response.json().catch(() => null);

        //Handle unsuccessful uploads
        if (!response.ok) {
            throw new Error(
                data?.message ||
                data?.title ||
                `Upload failed (${response.status}).`
            );
        }

        //Return the uploaded document information
        return data;
    },

    //Process an uploaded document
    async processDocument(documentId) {

        //Get the CSRF token for this authenticated session
        const csrfToken = await getCsrfToken();

        //Send the processing request to the C# backend
        const response = await fetch(
            `${API_URL}/${documentId}/process`,
            {
                method: "POST",

                //Include the authentication cookie
                credentials: "include",

                //Include the antiforgery request token
                headers: {
                    "X-CSRF-TOKEN": csrfToken,
                },
            }
        );

        //Read the response from the backend
        const data = await response.json().catch(() => null);

        //Handle processing errors
        if (!response.ok) {
            throw new Error(
                data?.message ||
                `Document processing failed (${response.status}).`
            );
        }

        //Return the processing result
        return data;
    },

    //Classify a processed document using AI
    async classifyDocument(documentId) {

        //Get the CSRF token for the authenticated user
        const csrfToken = await getCsrfToken();

        //Send the classification request to the backend
        const response = await fetch(
            `${API_URL}/${documentId}/classify`,
            {
                method: "POST",

                //Send the authentication cookie
                credentials: "include",

                //Send the CSRF token to protect the request
                headers: {
                    "X-CSRF-TOKEN": csrfToken,
                },
            }
        );

        //Read the backend response
        const data = await response.json().catch(() => null);

        //Handle unsuccessful classification
        if (!response.ok) {
            throw new Error(
                data?.message ||
                `Document classification failed (${response.status}).`
            );
        }

        //Return the AI classification result
        return data;
    },

    //Archive a document without permanently deleting it
    async archiveDocument(documentId) {

        //Get the CSRF token for the authenticated session
        const csrfToken = await getCsrfToken();

        //Send a DELETE request to the C# backend
        const response = await fetch(
            `${API_URL}/${documentId}`,
            {
                method: "DELETE",

                //Include the authentication cookie
                credentials: "include",

                //Protect the request with the CSRF token
                headers: {
                    "X-CSRF-TOKEN": csrfToken,
                },
            }
        );

        //Read the backend response
        const data = await response.json().catch(() => null);

        //Handle unsuccessful archiving
        if (!response.ok) {
            throw new Error(
                data?.message ||
                `Failed to archive document (${response.status}).`
            );
        }

        //Return the successful response
        return data;
    },

    //Activate a document
    async activateDocument(documentId) {

        //Get the CSRF token for the authenticated session
        const csrfToken = await getCsrfToken();

        //Send a POST request to the C# backend
        const response = await fetch(
            `${API_URL}/${documentId}/activate`,
            {
                method: "POST",

                //Include the authentication cookie
                credentials: "include",

                //Protect the request with the CSRF token
                headers: {
                    "X-CSRF-TOKEN": csrfToken,
                },
            }
        );

        //Read the backend response
        const data = await response.json().catch(() => null);

        //Handle unsuccessful archiving
        if (!response.ok) {
            throw new Error(
                data?.message ||
                `Failed to restore document (${response.status}).`
            );
        }

        //Return the successful response
        return data;
    },


};

//Retrieve all versions of a document
export async function getDocumentVersions(documentId) {
    const response = await fetch(
        `/api/Documents/${documentId}/versions`,
        {
            method: "GET",
            credentials: "include",
        }
    );

    if (!response.ok) {
        throw new Error("Failed to retrieve document versions.");
    }

    return await response.json();
}


//Upload an updated version of an existing document
export async function uploadDocumentVersion(documentId, file) {

    //Get a valid CSRF token from our authentication service
    const csrfToken = await getCsrfToken();

    //Create the multipart form data
    const formData = new FormData();

    //The backend expects the uploaded file under "file"
    formData.append("file", file);

    //Send the updated document to the API
    const response = await fetch(
        `${API_URL}/${documentId}/versions`,
        {
            method: "POST",

            //Include the authentication cookie
            credentials: "include",

            //Protect the request using the CSRF token
            headers: {
                "X-CSRF-TOKEN": csrfToken,
            },

            //Send the selected file
            body: formData,
        }
    );

    //Read the response
    const data = await response.json().catch(() => null);

    //Handle upload errors
    if (!response.ok) {
        throw new Error(
            data?.message ||
            `Document version upload failed (${response.status}).`
        );
    }

    //Return the uploaded document information
    return data;
}


//Compare two versions of a document using AI
export async function compareDocumentVersions(
    previousDocumentId,
    currentDocumentId
) {
    //Get the CSRF token from our authentication service
    const csrfToken = await getCsrfToken();

    //Send both document IDs to the backend
    const response = await fetch(`${API_URL}/compare`, {
        method: "POST",
        credentials: "include",

        headers: {
            "Content-Type": "application/json",
            "X-CSRF-TOKEN": csrfToken,
        },

        body: JSON.stringify({
            previousDocumentId,
            currentDocumentId,
        }),
    });

    //Read the API response
    const data = await response.json().catch(() => null);

    //Handle failed comparisons
    if (!response.ok) {
        throw new Error(
            data?.message ||
            `Document comparison failed (${response.status}).`
        );
    }

    //Return the AI-generated comparison
    return data;
}
