# ADAC Document Intelligence

Is a full-stack document management and AI analysis platform built with an ASP.NET Core backend and a React frontend. The application allows users to upload documents, manage versions, process files, and generate AI-powered summaries, analyses, and chat-based insights.

The core application lives under the `DocumentSystem` folder and is split into:

- `DocumentSystem/DocumentSystemApi` — ASP.NET Core Web API for authentication, document processing, AI analysis, and persistence
- `DocumentSystem/documentsystemclient` — React + Vite front-end for the user dashboard and analysis workspace

## Features

- Secure user registration and login using cookie-based authentication
- Document upload and document versioning
- Document processing and classification workflows
- AI-powered general analysis for uploaded documents
- Specialized QA analysis for review-oriented workflows
- Summaries and findings persisted to the database
- AI chat for asking questions about a document
- Comparison of document versions using AI
- PDF export for saved analyses
- Document archive

## Tech Stack

- Backend: ASP.NET Core, C#, Entity Framework Core, SQL Server
- Frontend: React, Vite, JavaScript
- AI integration: OpenAI-compatible API services
- PDF generation: QuestPDF
- Document processing: OpenXML and PDF parsing libraries

## Project Structure

```text
ADAC/
├── .github/
├── DocumentSystem/
│   ├── DocumentSystemApi/
│   │   ├── Controllers/
│   │   ├── Data/
│   │   ├── Dtos/
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── appsettings.json
│   │   └── Program.cs
│   └── documentsystemclient/
│       ├── src/
│       ├── package.json
│       ├── vite.config.js
│       └── index.html
├── README.md
└── .gitignore
```

## Prerequisites

Before running the project, make sure you have:

- .NET 10 SDK
- Node.js 18+ and npm
- SQL Server LocalDB or another SQL Server instance
- An OpenAI API key

## Configuration

Update the backend configuration in `DocumentSystem/DocumentSystemApi/appsettings.json` before starting the API.

```json
{
  "ConnectionStrings": {
    "DocumentDB": "Server=(localdb)\\MSSQLLocalDB;Database=DocumentDB;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "OpenAI": {
    "ApiKey": "your-openai-api-key",
    "Model": "gpt-4o-mini"
  }
}
```

If you are using a different SQL Server instance, replace the connection string accordingly.

## Running the Backend

From the repository root:

```bash
cd DocumentSystem/DocumentSystemApi
dotnet restore
dotnet run
```

The API runs by default on:

- HTTPS: `https://localhost:7127`
- HTTP: `http://localhost:5157`

## Running the Frontend

Open a second terminal and run:

```bash
cd DocumentSystem/documentsystemclient
npm install
npm run dev
```

The Vite app runs by default on:

- `http://localhost:54824`

The frontend is configured to proxy `/api` requests to the backend at `https://localhost:7127`.

## Usage

1. Open the application in the browser.
2. Register a new account or log in.
3. Upload a document from the dashboard.
4. Process and classify the document.
5. Generate a general or QA analysis.
6. Review findings, discussion chat, summary views, and export options.
