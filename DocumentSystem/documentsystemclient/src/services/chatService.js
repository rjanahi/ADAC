
import { getCsrfToken } from "./authService";

//Base URL for the ASP.NET ChatController
const API_URL = "/api/Chat";

//Read an API response and report useful errors
async function readResponse(response) {
  const data = await response.json().catch(() => null);

  //Stop when the backend returns an error
  if (!response.ok) {
    throw new Error(
      data?.message || `Request failed (${response.status}).`
    );
  }

  return data;
}

export const chatService = {

  //Create a new conversation for a document
  async createConversation(documentId, title) {
    const csrfToken = await getCsrfToken();

    const response = await fetch(
      `${API_URL}/conversations`,
      {
        method: "POST",
        credentials: "include",

        headers: {
          "Content-Type": "application/json",
          "X-CSRF-TOKEN": csrfToken,
        },

        body: JSON.stringify({
          documentId,
          title,
        }),
      }
    );

    return readResponse(response);
  },


  //Get existing conversations for one document
  async getConversations(documentId) {
    const response = await fetch(
      `${API_URL}/documents/${documentId}/conversations`,
      {
        credentials: "include",
      }
    );

    return readResponse(response);
  },


  //Retrieve the messages from one conversation
  async getMessages(conversationId) {
    const response = await fetch(
      `${API_URL}/conversations/${conversationId}/messages`,
      {
        credentials: "include",
      }
    );

    return readResponse(response);
  },


  //Send a question and receive the saved AI answer
  async sendMessage(conversationId, message) {
    const csrfToken = await getCsrfToken();

    const response = await fetch(
      `${API_URL}/messages`,
      {
        method: "POST",
        credentials: "include",

        headers: {
          "Content-Type": "application/json",
          "X-CSRF-TOKEN": csrfToken,
        },

        body: JSON.stringify({
          conversationId,
          message,
        }),
      }
    );

    return readResponse(response);
  },
};
