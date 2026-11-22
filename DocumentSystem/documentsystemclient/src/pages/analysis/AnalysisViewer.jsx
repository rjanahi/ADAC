
import { useEffect, useState } from "react";
import { analysisService } from "../../services/analysisService";
import "./AnalysisViewer.css";
import SummaryViewer from "../summary/SummaryViewer";
import DocumentChat from "./DocumentChat";
import {
    documentService,
    getDocumentVersions,
    uploadDocumentVersion,
    compareDocumentVersions
} from "../../services/documentService";

//Display analysis, summaries, document versions and AI Chat
function AnalysisViewerContent({
    document,
    onBack,
    onSelectVersion
}) {

    //Store previous analyses for this document
    const [history, setHistory] = useState([]);

    //Store the selected analysis result
    const [analysis, setAnalysis] = useState(null);

    //Track which operation is loading
    const [loading, setLoading] = useState(true);
    const [generating, setGenerating] = useState(false);

    //Store messages and errors
    const [error, setError] = useState("");
    const [message, setMessage] = useState("");
    const [messageVer, setMessageVer] = useState("");

    //Check whether the selected document is a QA Document
    const isQA = document.documentType === "QA";

    //Store every uploaded version of the selected document
    const [documentVersions, setDocumentVersions] = useState([]);
    //Store the primary saved analysis for each document version
    const [versionAnalyses, setVersionAnalyses] = useState({});

    //Track whether document versions are being loaded
    const [loadingVersions, setLoadingVersions] = useState(true);

    //Store errors related to document version retrieval
    const [versionError, setVersionError] = useState("");

    //Store the updated document selected by the user
    const [newVersionFile, setNewVersionFile] = useState(null);

    //Track whether the file is being uploaded
    const [uploadingVersion, setUploadingVersion] = useState(false);

    //Display errors related to version uploads
    const [uploadVersionError, setUploadVersionError] = useState("");

    //Track which document version is being processed
    const [processingVersionId, setProcessingVersionId] = useState(null);

    //Track the document currently being classified
    const [classifyingVersionId, setClassifyingVersionId] = useState(null);


    //The two uploaded document versions being compared
    const [previousVersionId, setPreviousVersionId] = useState("");
    const [currentVersionId, setCurrentVersionId] = useState("");

    //Store the calculated comparison results
    const [comparison, setComparison] = useState(null);
    //Currently selected comparison category
    const [comparisonFilter, setComparisonFilter] = useState("All");
    //Track which comparison findings are expanded
    const [expandedComparisonFindings, setExpandedComparisonFindings] = useState([]);

    //Track comparison loading and errors
    const [comparing, setComparing] = useState(false);
    const [comparisonError, setComparisonError] = useState("");

    //Track whether the user is viewing document summaries
    const [showSummaries, setShowSummaries] = useState(false);

    //Track whether a PDF is being generated
    const [downloadingPdf, setDownloadingPdf] = useState(false);

    //Display PDF export errors
    const [pdfError, setPdfError] = useState("");


    //Categories of document changes
    const comparisonCategories = [
        "Added",
        "Removed",
        "Modified",
        "Unchanged"
    ];

    //Count the findings in each category
    const comparisonCounts = Object.fromEntries(
        comparisonCategories.map(category => [
            category,
            (comparison?.changes ?? []).filter(
                change => change.changeType === category
            ).length
        ])
    );

    //Filter the findings displayed to the user
    const filteredComparisonChanges =
        (comparison?.changes ?? []).filter(change =>
            comparisonFilter === "All" ||
            change.changeType === comparisonFilter
        );

    //Check whether this document already has a completed analysis
    const existingAnalysis = history.find(
        item => item.status === "Completed"
    );

    const hasSavedAnalysis = Boolean(existingAnalysis);

    //Load analysis history when opening the viewer
    useEffect(() => {
        let active = true;

        async function loadInitialHistory() {
            try {
                setLoading(true);

                //Get all previous analyses for this document
                const results =
                    await analysisService.getAnalysisHistory(document.id);

                if (!active) return;

                //Only show history for the selected document's analysis type
                const filteredHistory = results.filter(
                    (item) =>
                        item.analysisType === (isQA ? "QA" : "General")
                );

                setHistory(filteredHistory);


                const primaryAnalysis = filteredHistory
                    .filter(item => item.status === "Completed")
                    .sort((a, b) => b.version - a.version)[0];

                if (primaryAnalysis) {
                    const latest = await analysisService.getAnalysisResult(
                        primaryAnalysis.analysisId
                    );

                    //Avoid setting state after switching documents
                    if (!active) return;

                    setAnalysis(latest);
                } else {
                    //New document versions have no analysis history yet
                    setAnalysis(null);
                }


            } catch (err) {
                if (active) setError(err.message);
            } finally {
                if (active) setLoading(false);
            }
        }

        loadInitialHistory();

        //Ignore results if the user leaves this page
        return () => {
            active = false;
        };
    }, [document.id, isQA]);

    //Load document versions when the selected document changes
    useEffect(() => {
        let active = true;

        async function loadVersions() {
            try {
                //Start loading and clear previous errors
                setLoadingVersions(true);
                setVersionError("");

                //Retrieve all versions from the backend
                const versions = await getDocumentVersions(document.id);

                //Get the saved analyses belonging to each document version
                const analysisResults = await Promise.all(
                    versions.map(async (version) => {
                        const analyses =
                            await analysisService.getAnalysisHistory(version.id);

                        //Select the latest completed analysis
                        const primaryAnalysis = analyses
                            .filter(item => item.status === "Completed")
                            .sort((a, b) => b.version - a.version)[0] ?? null;

                        return {
                            documentId: version.id,
                            analysis: primaryAnalysis
                        };
                    })
                );

                //Stop if the user has switched documents
                if (!active) return;

                //Create a lookup using each document's ID
                const analysisLookup = Object.fromEntries(
                    analysisResults.map(result => [
                        result.documentId,
                        result.analysis
                    ])
                );

                setDocumentVersions(versions);
                setVersionAnalyses(analysisLookup);

            } catch (err) {
                if (active) {
                    setVersionError(err.message);
                }
            } finally {
                if (active) {
                    setLoadingVersions(false);
                }
            }
        }

        loadVersions();

        //Prevent stale responses from updating the page
        return () => {
            active = false;
        };

    }, [document.id]);

    //Expand or collapse a comparison finding
    function toggleComparisonFinding(findingId) {
        setExpandedComparisonFindings(previous =>
            previous.includes(findingId)
                ? previous.filter(id => id !== findingId)
                : [...previous, findingId]
        );
    }

    //Handle selection of an updated document
    function handleVersionFileChange(event) {
        //Get the first selected file
        const file = event.target.files?.[0];

        //Clear previous upload errors
        setUploadVersionError("");

        //Clear the previous selection if no file was chosen
        if (!file) {
            setNewVersionFile(null);
            return;
        }

        //Allow PDF, Word, and text documents
        const allowedExtensions = [".pdf", ".docx", ".txt"];

        const extension = file.name
            .substring(file.name.lastIndexOf("."))
            .toLowerCase();

        //Check the file extension
        if (!allowedExtensions.includes(extension)) {
            setUploadVersionError(
                "Only PDF, DOCX, and TXT files are supported."
            );
            setNewVersionFile(null);
            event.target.value = "";
            return;
        }

        //Maximum file size: 10 MB
        const maxFileSize = 10 * 1024 * 1024;

        if (file.size === 0 || file.size > maxFileSize) {
            setUploadVersionError(
                "The file must be larger than 0 bytes and no more than 10 MB."
            );
            setNewVersionFile(null);
            event.target.value = "";
            return;
        }

        //Save the selected file in React state
        setNewVersionFile(file);
    }

    //Upload the selected file as a new document version
    async function handleUploadNewVersion() {
        //Do not upload if no file is selected
        if (!newVersionFile || uploadingVersion) {
            return;
        }

        try {
            //Disable the upload controls while uploading
            setUploadingVersion(true);

            //Clear previous messages
            setUploadVersionError("");
            setError("");
            setMessageVer("");

            //Upload the revision through the document service
            const uploadedDocument = await uploadDocumentVersion(
                document.id,
                newVersionFile
            );

            //Refresh the document version list
            const updatedVersions = await getDocumentVersions(
                document.id
            );

            setDocumentVersions(updatedVersions);

            //The newly uploaded document version has no analysis yet
            setVersionAnalyses(previous => ({
                ...previous,
                [uploadedDocument.id]: null
            }));

            //Clear the selected file
            setNewVersionFile(null);

            //Show the new version number
            setMessage(
                `Document version ${uploadedDocument.documentVersion} ` +
                "uploaded successfully. It is ready for processing."
            );

        } catch (err) {
            //Show the error returned by the API
            setUploadVersionError(err.message);

        } finally {
            //Re-enable the upload controls
            setUploadingVersion(false);
        }
    }

    //Process and classify a newly uploaded document version
    async function handlePrepareDocumentVersion(versionId) {
        //Prevent multiple operations at once
        if (processingVersionId || classifyingVersionId) {
            return;
        }

        try {
            //Clear previous messages
            setVersionError("");
            setUploadVersionError("");
            setError("");
            setMessage("");

            //Step 1: Extract the document's text and create chunks
            setProcessingVersionId(versionId);

            await documentService.processDocument(versionId);

            //Processing has completed
            setProcessingVersionId(null);

            //Step 2: Classify the processed document
            setClassifyingVersionId(versionId);

            const result =
                await documentService.classifyDocument(versionId);

            //Refresh all document versions
            const updatedVersions =
                await getDocumentVersions(document.id);

            setDocumentVersions(updatedVersions);

            //Notify the user
            setMessage(
                `Document processed and classified as ${result.documentType}. ` +
                "You can now generate its analysis."
            );

        } catch (err) {
            //Show the API error
            setVersionError(err.message);

            //Refresh statuses even if classification failed
            try {
                const versions =
                    await getDocumentVersions(document.id);

                setDocumentVersions(versions);
            } catch {
                //Keep the original error message
            }

        } finally {
            //Always unlock the controls
            setProcessingVersionId(null);
            setClassifyingVersionId(null);
        }
    }

    //Generate a new analysis based on document type
    async function handleGenerate() {

        //Avoid generating another analysis for the same document
        if (hasSavedAnalysis || generating || loading) {
            return;
        }

        try {
            //Show the loading state
            setGenerating(true);

            //Clear previous messages
            setError("");
            setMessage("");

            let result;

            //Choose the correct AI analysis service
            if (isQA) {

                //Generate a specialized QA analysis
                result = await analysisService.generateQAAnalysis(
                    document.id
                );

            } else {

                //Generate a general document analysis
                result = await analysisService.generateGeneralAnalysis(
                    document.id
                );
            }

            //Retrieve the complete saved analysis from SQL Server
            const saved = await analysisService.getAnalysisResult(
                result.analysisId
            );

            //Display the new analysis
            setAnalysis(saved);


            //Refresh the saved analyses for this document version
            const updatedHistory =
                await analysisService.getAnalysisHistory(document.id);

            //Keep only analyses matching this document's type
            const filteredHistory = updatedHistory.filter(
                item => item.analysisType === (isQA ? "QA" : "General")
            );

            //Update the current document's analysis history
            setHistory(filteredHistory);

            //Find the primary completed analysis
            const primaryAnalysis = filteredHistory
                .filter(item => item.status === "Completed")
                .sort((a, b) => b.version - a.version)[0] ?? null;

            //Immediately update its status in Document Versions
            setVersionAnalyses(previous => ({
                ...previous,
                [document.id]: primaryAnalysis
            }));


            //Display a success message
            setMessage(
                isQA
                    ? "QA analysis completed successfully."
                    : "General analysis completed successfully."
            );

        } catch (err) {

            //Display errors returned by the backend
            setError(err.message);

        } finally {

            //Enable the Generate button again
            setGenerating(false);
        }
    }

    //Show summaries for the currently selected analysis
    if (showSummaries && analysis) {
        return (
            <SummaryViewer
                analysis={analysis}
                document={document}
                onBack={() => setShowSummaries(false)}
            />
        );
    }


    //Compare two uploaded document versions using OpenAI
    async function handleCompareVersions() {

        //Require both versions
        if (!previousVersionId || !currentVersionId) {
            setComparisonError("Select two document versions.");
            return;
        }

        //Prevent comparing a document with itself
        if (previousVersionId === currentVersionId) {
            setComparisonError("Choose two different versions.");
            return;
        }

        //Find the selected document information
        const previousVersion = documentVersions.find(
            version => version.id === previousVersionId
        );

        const currentVersion = documentVersions.find(
            version => version.id === currentVersionId
        );

        //Ensure both selected versions exist
        if (!previousVersion || !currentVersion) {
            setComparisonError("Selected document version not found.");
            return;
        }

        //The backend expects previous to be older than current
        if (
            previousVersion.documentVersion >=
            currentVersion.documentVersion
        ) {
            setComparisonError(
                "Select an older document under Previous " +
                "and a newer document under Updated."
            );
            return;
        }

        try {
            //Show the comparison loading state
            setComparing(true);
            setComparisonError("");
            setComparison(null);

            setComparisonFilter("All");
            setExpandedComparisonFindings([]);

            //Ask the backend to compare both documents using AI
            const result = await compareDocumentVersions(
                previousVersionId,
                currentVersionId
            );

            //Store the AI result and selected version information
            setComparison({
                ...result,
                previousVersion,
                currentVersion,
            });

        } catch (err) {
            setComparisonError(err.message);

        } finally {
            setComparing(false);
        }
    }



    //Download the saved analysis currently displayed

    async function handleDownloadPdf() {
        //Get the currently displayed saved analysis
        const selectedAnalysisId = analysis?.analysisId;

        if (!selectedAnalysisId) {
            setPdfError("Please select a saved analysis first.");
            return;
        }

        //Only completed analyses can be exported
        if (analysis.status?.toLowerCase() !== "completed") {
            setPdfError("Only completed analyses can be exported.");
            return;
        }

        try {
            setDownloadingPdf(true);
            setPdfError("");

            //Call the method inside the existing analysisService object
            await analysisService.downloadAnalysisPdf(
                selectedAnalysisId
            );

        } catch (error) {
            setPdfError(
                error.message || "Failed to download the PDF."
            );
        } finally {
            setDownloadingPdf(false);
        }
    }


    return (
        <div className="analysis-page">

            {/* Navigation and page title */}
            <header className="analysis-header">
                <div>
                    <button
                        type="button"
                        className="back-button"
                        onClick={onBack}
                    >
                        Back to Documents
                    </button>

                    {/* Display a title based on the document type */}
                    <h1>
                        {isQA ? "QA Document Analysis" : "Document Analysis"}
                    </h1>
                    <p>{document.title}</p>
                </div>



                {!hasSavedAnalysis && (
                    <button
                        type="button"
                        onClick={handleGenerate}
                        disabled={
                            generating ||
                            loading ||
                            document.status !== "Ready"
                        }
                    >
                        {generating
                            ? "Analyzing Document..."
                            : "Generate Analysis"}
                    </button>
                )}

                {hasSavedAnalysis && (
                    <span className="analysis-saved-label">
                        Analysis Completed
                    </span>
                )}

            </header>


            {/* Analysis and document chat share this workspace */}
            <div className="analysis-chat-layout">
                {/* Left side: existing analysis history, statistics and findings */}
                <div className="analysis-panel">

                    {/* Display success and error messages */}
                    {message && (
                        <p className="analysis-success">{message}</p>
                    )}

                    {error && (
                        <p className="analysis-error">{error}</p>
                    )}

                    {/* Display validation errors */}
                    {uploadVersionError && (
                        <p className="analysis-error">
                            {uploadVersionError}
                        </p>
                    )}

                    {/* Display uploaded document versions */}
                    <section className="analysis-card">
                        <h2>Document Versions</h2>

                        {loadingVersions && (
                            <p>Loading document versions...</p>
                        )}

                        {versionError && (
                            <p className="analysis-error">{versionError}</p>
                        )}

                        {!loadingVersions && !versionError && (
                            <div className="history-list">
                                {documentVersions.map((version) => {
                                    const savedAnalysis = versionAnalyses[version.id];

                                    return (
                                            <div
                                                key={version.id}
                                                className="document-version-item"
                                            >
                                                <div>
                                                    <strong>
                                                        Document Version {version.documentVersion}
                                                    </strong>

                                                    <p>{version.originalFileName}</p>
                                                </div>

                                                <div>
                                                    <span>{version.status}</span>

                                                    {version.id === document.id && (
                                                        <strong> · Currently Viewing</strong>
                                                    )}
                                            </div>


                                            {/* Saved AI analysis for this document version */}
                                            <div className="document-version-analysis">
                                                {savedAnalysis ? (
                                                    <>
                                                        <span className="document-analysis-status completed">
                                                            Analysis Completed
                                                        </span>

                                                        <p>{savedAnalysis.analysisType} Analysis</p>

                                                        {version.id === document.id ? (
                                                            <span className="document-analysis-current">
                                                                Currently Viewing
                                                            </span>
                                                        ) : version.status === "Ready" &&
                                                            ["QA", "General"].includes(version.documentType) ? (
                                                            <button
                                                                type="button"
                                                                onClick={() => onSelectVersion(version)}
                                                                disabled={
                                                                    loading ||
                                                                    generating ||
                                                                    uploadingVersion ||
                                                                    processingVersionId !== null ||
                                                                    classifyingVersionId !== null
                                                                }
                                                            >
                                                                View Analysis
                                                            </button>
                                                        ) : null}
                                                    </>
                                                ) : (
                                                    <span className="document-analysis-status pending">
                                                        No Analysis Yet
                                                    </span>
                                                )}
                                            </div>


                                                {/* Process new document versions */}
                                                {version.status === "Uploaded" && (
                                                    <button
                                                        type="button"
                                                        className="document-version-process-button"

                                                        onClick={() =>
                                                            handlePrepareDocumentVersion(version.id)
                                                        }

                                                        disabled={
                                                            processingVersionId !== null ||
                                                            classifyingVersionId !== null ||
                                                            uploadingVersion
                                                        }
                                                    >
                                                        {processingVersionId === version.id
                                                            ? "Processing Document..."
                                                            : classifyingVersionId === version.id
                                                                ? "Classifying Document..."
                                                                : "Process & Classify"}
                                                    </button>
                                                )}

                                                {/* Retry classification if processing succeeded earlier */}
                                                {version.status === "Ready" &&
                                                    version.documentType === "Unknown" && (
                                                        <button
                                                            type="button"
                                                            className="document-version-process-button"
                                                            disabled={
                                                                processingVersionId !== null ||
                                                                classifyingVersionId !== null
                                                            }
                                                            onClick={async () => {
                                                                try {
                                                                    setVersionError("");
                                                                    setClassifyingVersionId(version.id);

                                                                    await documentService.classifyDocument(
                                                                        version.id
                                                                    );

                                                                    const versions =
                                                                        await getDocumentVersions(document.id);

                                                                    setDocumentVersions(versions);
                                                                    setMessage("Document classified successfully.");

                                                                } catch (err) {
                                                                    setVersionError(err.message);

                                                                } finally {
                                                                    setClassifyingVersionId(null);
                                                                }
                                                            }}
                                                        >
                                                            {classifyingVersionId === version.id
                                                                ? "Classifying..."
                                                                : "Retry Classification"}
                                                        </button>
                                                    )}

                                                {/* Switch to a processed and classified document version */}
                                            {version.id !== document.id &&
                                                !savedAnalysis &&
                                                version.status === "Ready" &&
                                                ["QA", "General"].includes(version.documentType) && (
                                                        <button
                                                            type="button"
                                                            className="document-version-select-button"
                                                            onClick={() => onSelectVersion(version)}

                                                            //Prevent switching during active operations
                                                            disabled={
                                                                uploadingVersion ||
                                                                processingVersionId !== null ||
                                                                classifyingVersionId !== null ||
                                                                generating
                                                            }
                                                        >
                                                            View Version {version.documentVersion}
                                                        </button>
                                                    )}

                                            </div>
                                    );
                                })}
                            </div>
                        )}


                        {/* Upload an updated version of this document */}
                        <div className="document-version-upload">
                            <div className="document-version-upload-header">
                                <h3>Upload New Version</h3>
                                <p>
                                    Select an updated report to create a new document
                                    version. Previous analyses will be preserved.
                                </p>
                            </div>

                            {/* File selection */}
                            <label
                                className="document-version-file-label"
                                htmlFor="document-version-file"
                            >
                                Choose Updated Document
                            </label>

                            <input
                                id="document-version-file"
                                type="file"
                                accept=".pdf,.docx,.txt"
                                onChange={handleVersionFileChange}
                                disabled={uploadingVersion}
                            />

                            {/* Display the selected filename */}
                            {newVersionFile && (
                                <div className="document-version-selected">
                                    <span>{newVersionFile.name}</span>

                                    <span>
                                        {(newVersionFile.size / (1024 * 1024))
                                            .toFixed(2)} MB
                                    </span>
                                </div>
                            )}

                            <button
                                type="button"
                                className="document-version-upload-button"
                                onClick={handleUploadNewVersion}

                                //Only allow uploading when a valid file is selected
                                disabled={!newVersionFile || uploadingVersion}
                            >
                                {uploadingVersion
                                    ? "Uploading Document..."
                                    : "Upload New Version"}
                            </button>

                            <p className="document-version-upload-note">
                                Supported formats: PDF, DOCX, TXT. Maximum size: 10 MB.
                            </p>
                        </div>



                    </section>


                    {/* Compare saved analyses across document versions */}
                    {documentVersions.length >= 2 && (
                        <section className="analysis-card">
                            <h2>Compare Document Versions</h2>

                            <p>
                                Compare two uploaded document versions using AI
                                to identify meaningful changes in their content,
                                findings, risks and recommendations.
                            </p>

                            <div className="comparison-selectors">

                                {/* Select the older document */}
                                <label>
                                    Previous Version

                                    <select
                                        value={previousVersionId}
                                        onChange={(event) => {
                                            setPreviousVersionId(event.target.value);
                                            setComparison(null);
                                        }}
                                    >
                                        <option value="">Select version</option>

                                        {documentVersions
                                            .filter(version => version.status === "Ready")
                                            .map(version => (
                                                <option
                                                    key={version.id}
                                                    value={version.id}
                                                >
                                                    Document Version {version.documentVersion}
                                                </option>
                                            ))}
                                    </select>
                                </label>

                                {/* Select the updated document */}
                                <label>
                                    Updated Version

                                    <select
                                        value={currentVersionId}
                                        onChange={(event) => {
                                            setCurrentVersionId(event.target.value);
                                            setComparison(null);
                                        }}
                                    >
                                        <option value="">Select version</option>

                                        {documentVersions
                                            .filter(version => version.status === "Ready")
                                            .map(version => (
                                                <option
                                                    key={version.id}
                                                    value={version.id}
                                                >
                                                    Document Version {version.documentVersion}
                                                </option>
                                            ))}
                                    </select>
                                </label>
                            </div>

                            <button
                                type="button"
                                onClick={handleCompareVersions}
                                disabled={
                                    comparing ||
                                    !previousVersionId ||
                                    !currentVersionId ||
                                    previousVersionId === currentVersionId
                                }
                            >
                                {comparing ? "Comparing..." : "Compare Analyses"}
                            </button>

                            {comparisonError && (
                                <p className="analysis-error">
                                    {comparisonError}
                                </p>
                            )}


                            {/* Display the AI-generated document differences */}
                            {comparison && (
                                <div className="comparison-results">

                                    <h3>
                                        Document Version{" "}
                                        {comparison.previousVersion.documentVersion}
                                        {" vs "}
                                        Document Version{" "}
                                        {comparison.currentVersion.documentVersion}
                                    </h3>

                                    {/* Overall explanation of the differences */}
                                    <div className="comparison-summary">
                                        <h4>Comparison Summary</h4>
                                        <p>{comparison.summary}</p>
                                    </div>

                                    {/* Comparison category counters */}
                                    <div className="comparison-stats">
                                        {comparisonCategories.map(category => (
                                            <button
                                                key={category}
                                                type="button"
                                                className={`comparison-stat-card ${comparisonFilter === category ? "active" : ""
                                                    }`}
                                                onClick={() =>
                                                    setComparisonFilter(
                                                        comparisonFilter === category
                                                            ? "All"
                                                            : category
                                                    )
                                                }
                                            >
                                                <span className="comparison-stat-number">
                                                    {comparisonCounts[category]}
                                                </span>

                                                <span className="comparison-stat-label">
                                                    {category}
                                                </span>
                                            </button>
                                        ))}
                                    </div>

                                    {/* Filter buttons */}
                                    <div className="comparison-filters">
                                        {["All", ...comparisonCategories].map(category => (
                                            <button
                                                key={category}
                                                type="button"
                                                className={
                                                    comparisonFilter === category ? "active" : ""
                                                }
                                                onClick={() => setComparisonFilter(category)}
                                            >
                                                {category}
                                            </button>
                                        ))}
                                    </div>


                                    {/* Organize changes by their category */}
                                    {["Modified", "Added", "Removed", "Unchanged"].map(
                                        changeType => {
                                            const changes = filteredComparisonChanges
                                                .filter(
                                                    change => change.changeType === changeType
                                                );

                                            //Hide empty categories
                                            if (changes.length === 0) {
                                                return null;
                                            }

                                            return (
                                                <section
                                                    key={changeType}
                                                    className="comparison-change-section"
                                                >
                                                    <h4>
                                                        {changeType}
                                                        {" ("}
                                                        {changes.length}
                                                        {")"}
                                                    </h4>


                                                    {changes.map((change, index) => {

                                                        const isExpanded =
                                                            expandedComparisonFindings.includes(index);

                                                        const hasEvidence =
                                                            Boolean(
                                                                change.previousExcerpt ||
                                                                change.currentExcerpt
                                                            );

                                                        return (
                                                            <article
                                                                key={index}
                                                                className={`comparison-change-card comparison-${changeType.toLowerCase()}`}
                                                            >
                                                                {/* Finding title and category */}
                                                                <div className="comparison-change-header">
                                                                    <h5>{change.title}</h5>

                                                                    <span
                                                                        className={`comparison-change-badge comparison-badge-${changeType.toLowerCase()}`}
                                                                    >
                                                                        {changeType}
                                                                    </span>
                                                                </div>

                                                                {/* Explanation of what changed */}
                                                                <p className="comparison-change-description">
                                                                    {change.description}
                                                                </p>

                                                                {/* Expand and collapse the supporting evidence */}
                                                                {hasEvidence && (
                                                                    <button
                                                                        type="button"
                                                                        className="comparison-evidence-toggle"
                                                                        aria-expanded={isExpanded}
                                                                        aria-controls={`${index}-evidence`}
                                                                        onClick={() =>
                                                                            toggleComparisonFinding(index)
                                                                        }
                                                                    >
                                                                        {isExpanded
                                                                            ? "Hide source excerpts"
                                                                            : "View source excerpts"}

                                                                        <span aria-hidden="true">
                                                                            {isExpanded ? " −" : " +"}
                                                                        </span>
                                                                    </button>
                                                                )}

                                                                {/* Before and after document excerpts */}
                                                                {hasEvidence && isExpanded && (
                                                                    <div
                                                                        id={`${index}-evidence`}
                                                                        className="comparison-excerpts"
                                                                    >
                                                                        <div className="comparison-excerpt-previous">
                                                                            <strong>Previous Version</strong>

                                                                            <p>
                                                                                {change.previousExcerpt ??
                                                                                    "No verified excerpt available"}
                                                                            </p>

                                                                            {change.previousLocation && (
                                                                                <small>{change.previousLocation}</small>
                                                                            )}
                                                                        </div>

                                                                        <div className="comparison-excerpt-current">
                                                                            <strong>Updated Version</strong>

                                                                            <p>
                                                                                {change.currentExcerpt ??
                                                                                    "No verified excerpt available"}
                                                                            </p>

                                                                            {change.currentLocation && (
                                                                                <small>{change.currentLocation}</small>
                                                                            )}
                                                                        </div>
                                                                    </div>
                                                                )}
                                                            </article>
                                                        );
                                                    })}

                                                </section>
                                            );
                                        }
                                    )}

                                    {comparison.changes?.length === 0 && (
                                        <p>
                                            No meaningful differences were identified
                                            in the supplied document content.
                                        </p>
                                    )}

                                    <p className="comparison-disclaimer">
                                        AI-generated comparison. A finding that is no
                                        longer reported is not necessarily resolved.
                                    </p>
                                </div>
                            )}
                        </section>
                    )}

                    {/* Loading state */}
                    {loading && <p>Loading analysis...</p>}

                    {/* AI generation state */}
                    {generating && (
                        <p>AI is analyzing the document...</p>
                    )}


                    {/* Show helpful message when there are no analyses */}
                    {!loading && !generating &&
                        !analysis && history.length === 0 && (
                            <section className="analysis-card">
                            <h2>No Analysis Yet</h2>

                            <p>
                                {document.status === "Ready"
                                    ? "Click Generate Analysis to create this document version's AI assessment."
                                    : "Process and classify this document before generating its AI analysis."}
                            </p>
                            </section>
                        )}


                    {/* Show the selected analysis */}
                    {!loading && analysis && (
                        <>

                            {/* Open the summary viewer for the selected analysis */}
                            <section className="analysis-card">
                                <h2>AI-Generated Summaries</h2>

                                <p>
                                    Generate a summary of this analysis or view
                                    previously saved versions.
                                </p>

                                <button
                                    type="button"
                                    onClick={() => setShowSummaries(true)}
                                    disabled={generating || loading}
                                >
                                    View / Generate Summaries
                                </button>


                            </section>


                            {/* Show testing statistics only for saved QA analyses */}
                            {analysis.analysisType === "QA" && (

                                <section className="analysis-card">

                                    <h2>Test Execution Statistics</h2>

                                    <div className="qa-stats-grid">

                                        {/* Total number of test cases */}
                                        <div className="qa-stat-card">
                                            <span>Total Tests</span>
                                            <strong>{analysis.totalTests ?? "N/A"}</strong>
                                        </div>

                                        {/* Successfully executed tests */}
                                        <div className="qa-stat-card qa-passed">
                                            <span>Passed</span>
                                            <strong>{analysis.passedTests ?? "N/A"}</strong>
                                        </div>

                                        {/* Failed test cases */}
                                        <div className="qa-stat-card qa-failed">
                                            <span>Failed</span>
                                            <strong>{analysis.failedTests ?? "N/A"}</strong>
                                        </div>

                                        {/* Tests that could not proceed */}
                                        <div className="qa-stat-card qa-blocked">
                                            <span>Blocked</span>
                                            <strong>{analysis.blockedTests ?? "N/A"}</strong>
                                        </div>

                                        {/* Tests that were skipped */}
                                        <div className="qa-stat-card">
                                            <span>Skipped</span>
                                            <strong>{analysis.skippedTests ?? "N/A"}</strong>
                                        </div>

                                    </div>

                                </section>
                            )}


                            {/* Analysis overview */}
                            <section className="analysis-card">
                                {/* PDF export controls */}
                                {analysis && (
                                    <div className="analysis-pdf-actions">
                                        <button
                                            type="button"
                                            className="analysis-download-pdf-button"
                                            onClick={handleDownloadPdf}
                                            disabled={
                                                downloadingPdf ||
                                                analysis.status?.toLowerCase() !== "completed"
                                            }
                                        >
                                            {downloadingPdf
                                                ? "Generating PDF..."
                                                : "Download PDF"}
                                        </button>

                                        {pdfError && (
                                            <p className="analysis-pdf-error" role="alert">
                                                {pdfError}
                                            </p>
                                        )}
                                    </div>
                                )}
                                <h2>
                                    Analysis Results
                                </h2>

                                <p className="analysis-meta">
                                    Document Version {document.documentVersion}
                                </p>
                                

                                <p className="analysis-meta">
                                    Type: {analysis.analysisType}
                                    {" | "}
                                    Status: {analysis.status}
                                </p>

                                <h3>Overview and Assessment</h3>

                                <p className="analysis-text">
                                    {analysis.overallAssessment}
                                </p>
                            </section>


                            {/* Identified risks */}
                            <section className="analysis-card">
                                <h2>Key Risks</h2>

                                {analysis.keyRisks?.length > 0 ? (
                                    <ul>
                                        {analysis.keyRisks.map((risk, index) => (
                                            <li key={index}>{risk}</li>
                                        ))}
                                    </ul>
                                ) : (
                                    <p>No key risks identified.</p>
                                )}
                            </section>


                            {/* AI recommendations */}
                            <section className="analysis-card">
                                <h2>Recommendations</h2>

                                {analysis.recommendations?.length > 0 ? (
                                    <ul>
                                        {analysis.recommendations.map(
                                            (recommendation, index) => (
                                                <li key={index}>{recommendation}</li>
                                            )
                                        )}
                                    </ul>
                                ) : (
                                    <p>No recommendations available.</p>
                                )}
                            </section>


                            {/* Individual findings */}
                            <section className="analysis-card">
                                <h2>Detailed Findings</h2>

                                {analysis.findings?.length > 0 ? (
                                    <div className="findings-list">
                                        {analysis.findings.map((finding) => (
                                            <article
                                                key={finding.id}
                                                className="finding-card"
                                            >
                                                <div className="finding-heading">
                                                    <h3>{finding.title}</h3>

                                                    <span className="severity">
                                                        {finding.severity}
                                                    </span>
                                                </div>

                                                <p>{finding.description}</p>

                                                <p>
                                                    <strong>Category:</strong>{" "}
                                                    {finding.category}
                                                </p>

                                                {/* Show affected software functionality for QA findings */}
                                                {analysis.analysisType === "QA" &&
                                                    finding.affectedArea && (
                                                        <p>
                                                            <strong>Affected Area:</strong>{" "}
                                                            {finding.affectedArea}
                                                        </p>
                                                    )}

                                                <p>
                                                    <strong>Impact:</strong>{" "}
                                                    {finding.impact}
                                                </p>

                                                <p>
                                                    <strong>Recommendation:</strong>{" "}
                                                    {finding.recommendation}
                                                </p>

                                                <p>
                                                    <strong>Source:</strong>{" "}
                                                    {finding.sourceLocation}
                                                </p>
                                            </article>
                                        ))}
                                    </div>
                                ) : (
                                    <p>No detailed findings identified.</p>
                                )}
                            </section>
                        </>
                    )}
                </div>

                {/* Right side: document chat works even before analysis is generated */}
                <aside className="analysis-chat-panel" aria-label="Document chat">
                    <DocumentChat document={document} />
                </aside>
            </div>
        </div>
    );
}



function AnalysisViewerSession({ document, onBack }) {
    const [selectedDocument, setSelectedDocument] = useState(document);

    //Switch between uploaded document versions
    function handleSelectDocumentVersion(version) {
        if (version.id === selectedDocument.id) {
            return;
        }

        //Only allow processed and classified documents
        if (
            version.status !== "Ready" ||
            !["QA", "General"].includes(version.documentType)
        ) {
            return;
        }

        setSelectedDocument(version);
    }

    return (
        <AnalysisViewerContent
            key={selectedDocument.id}
            document={selectedDocument}
            onBack={onBack}
            onSelectVersion={handleSelectDocumentVersion}
        />
    );
}

//Reset the viewer when opening a different document
function AnalysisViewer({ document, onBack }) {
    return (
        <AnalysisViewerSession
            key={document.id}
            document={document}
            onBack={onBack}
        />
    );
}

export default AnalysisViewer;
