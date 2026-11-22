import { useEffect, useRef, useState } from "react";
import { chatService } from "../../services/chatService";
import "./DocumentChat.css";

//Embedded chat for the document shown in AnalysisViewer
function DocumentChat({ document }) {
    //Keep track of conversations and the messages in the active one
    const [conversations, setConversations] = useState([]);
    const [selectedId, setSelectedId] = useState(null);
    const [messages, setMessages] = useState([]);
    const [question, setQuestion] = useState("");

    //Track requests so the user sees loading and error states
    const [loading, setLoading] = useState(true);
    const [loadingMessages, setLoadingMessages] = useState(false);
    const [creating, setCreating] = useState(false);
    const [sending, setSending] = useState(false);
    const [error, setError] = useState("");

    //Ignore outdated requests when switching between conversations
    const messageRequest = useRef(0);
    const bottomRef = useRef(null);

    //Load saved conversations when the document changes
    useEffect(() => {
        let active = true;

        async function loadConversations() {
            setLoading(true);
            setError("");
            setConversations([]);
            setSelectedId(null);
            setMessages([]);
            setQuestion("");
            setLoadingMessages(false);
            messageRequest.current += 1;

            try {
                const results = await chatService.getConversations(document.id);
                if (!active) return;

                setConversations(results);
                //Open the most recently updated conversation automatically
                if (results.length > 0) {
                    setLoadingMessages(true);
                    setSelectedId(results[0].id);
                }
            } catch (err) {
                if (active) setError(err.message);
            } finally {
                if (active) setLoading(false);
            }
        }

        loadConversations();
        return () => { active = false; };
    }, [document.id]);

    //Load messages when the selected conversation changes

    useEffect(() => {
        if (!selectedId) return;

        let active = true;
        const requestId = ++messageRequest.current;

        async function loadMessages() {
            try {
                const results = await chatService.getMessages(selectedId);

                if (active && requestId === messageRequest.current) {
                    setMessages(results);
                }
            } catch (err) {
                if (active && requestId === messageRequest.current) {
                    setError(err.message);
                }
            } finally {
                if (active && requestId === messageRequest.current) {
                    setLoadingMessages(false);
                }
            }
        }

        loadMessages();

        return () => {
            active = false;
        };
    }, [selectedId]);

    //Keep the latest message visible as the chat grows
    useEffect(() => {
        bottomRef.current?.scrollIntoView({ block: "end", behavior: "smooth" });
    }, [messages, sending]);

    //Start a new chat without leaving the Analysis page
    async function handleNewChat() {
        if (creating || sending) return;
        setCreating(true);
        setError("");

        try {
            const result = await chatService.createConversation(
                document.id,
                "New Conversation"
            );
            setConversations((current) => [result, ...current]);
            setMessages([]);
            setLoadingMessages(true);
            setSelectedId(result.id);
            setQuestion("");
        } catch (err) {
            setError(err.message);
        } finally {
            setCreating(false);
        }
    }

    //Save the question, generate the AI response, and show both messages
    async function handleSend(event) {
        event.preventDefault();
        const text = question.trim();
        if (!text || loading || creating || sending || loadingMessages) return;

        setSending(true);
        setError("");
        let conversationId = selectedId;

        try {
            //Create the first conversation automatically if necessary
            if (!conversationId) {
                const result = await chatService.createConversation(
                    document.id,
                    "New Conversation"
                );
                conversationId = result.id;
                setConversations((current) => [result, ...current]);
                setMessages([]);
                setLoadingMessages(true);
                setSelectedId(conversationId);
            }

            setQuestion("");
            const result = await chatService.sendMessage(conversationId, text);

            //Reload from the database to avoid duplicate or out-of-order messages
            const savedMessages = await chatService.getMessages(conversationId);
            messageRequest.current += 1;
            setLoadingMessages(false);
            setMessages(savedMessages);

            //Move this conversation to the top of the sidebar
            setConversations((current) => {
                const updated = current.map((item) =>
                    item.id === conversationId
                        ? { ...item, updatedAt: result.assistantMessage.createdAt }
                        : item
                );
                return [
                    ...updated.filter((item) => item.id === conversationId),
                    ...updated.filter((item) => item.id !== conversationId),
                ];
            });
        } catch (err) {
            setError(err.message);
            setQuestion(text);

            //A failed AI request may still have saved the user's message
            if (conversationId) {
                try {
                    setMessages(await chatService.getMessages(conversationId));
                } catch {
                    //Preserve the original error
                }
            }
        } finally {
            setSending(false);
        }
    }

    return (
        <div className="document-chat-page embedded">
            {/* Heading stays within the right-hand analysis panel */}
            <div className="embedded-chat-header">
                <h2>AI Document Chat</h2>
                <p>Ask questions about {document.title}.</p>
            </div>

            <div className="chat-layout">
                {/* Saved conversations */}
                <aside className="chat-sidebar">
                    <div className="chat-sidebar-heading">
                        <h2>Conversations</h2>
                        <button
                            type="button"
                            disabled={loading || creating || sending}
                            onClick={handleNewChat}
                        >
                            {creating ? "Creating..." : "+ New Chat"}
                        </button>
                    </div>

                    <div className="chat-conversation-list">
                        {loading && <p>Loading...</p>}
                        {!loading && conversations.length === 0 && (
                            <p>No conversations yet.</p>
                        )}
                        {conversations.map((item) => (
                            <button
                                type="button"
                                key={item.id}
                                disabled={sending || creating}
                                className={
                                    selectedId === item.id
                                        ? "chat-conversation active"
                                        : "chat-conversation"
                                }
                                onClick={() => {
                                    if (selectedId === item.id) return;

                                    setError("");
                                    setMessages([]);
                                    setLoadingMessages(true);
                                    setSelectedId(item.id);
                                }}
                            >
                                {item.title}
                            </button>
                        ))}
                    </div>
                </aside>

                {/* Chat messages and question input */}
                <div className="chat-main">
                    <div className="chat-messages" role="log" aria-label="Chat messages">
                        {!selectedId && !loading && (
                            <div className="chat-empty">
                                <h2>Ask your document anything</h2>
                                <p>Start a conversation using the box below.</p>
                            </div>
                        )}
                        {loadingMessages && <p>Loading messages...</p>}

                        {!loadingMessages && messages.map((item) => (
                            <article
                                key={item.id}
                                className={
                                    item.role === "User"
                                        ? "chat-message user"
                                        : "chat-message assistant"
                                }
                            >
                                <span className="chat-message-role">
                                    {item.role === "User" ? "You" : "AI Assistant"}
                                </span>
                                <div className="chat-message-content">{item.content}</div>

                                {/* Each citation expands to show its original excerpt */}
                                {item.sources?.length > 0 && (
                                    <div className="chat-sources">
                                        <strong>Sources</strong>
                                        {item.sources.map((source) => (
                                            <details className="chat-source" key={source.chunkId}>
                                                <summary>
                                                    {source.pageNumber != null
                                                        ? `Page ${source.pageNumber}, chunk ${source.chunkIndex}`
                                                        : `Chunk ${source.chunkIndex}`}
                                                </summary>
                                                <p>{source.excerpt}</p>
                                            </details>
                                        ))}
                                    </div>
                                )}
                            </article>
                        ))}

                        {sending && (
                            <div className="chat-typing" role="status">
                                AI is reading the document...
                            </div>
                        )}
                        <div ref={bottomRef} />
                    </div>

                    {error && <div className="chat-error" role="alert">{error}</div>}

                    {/* Enter sends the question; Shift+Enter inserts a newline */}
                    <form className="chat-input-form" onSubmit={handleSend}>
                        <textarea
                            value={question}
                            rows={2}
                            maxLength={2000}
                            placeholder="Ask something about the document..."
                            disabled={loading || loadingMessages || creating || sending}
                            onChange={(event) => setQuestion(event.target.value)}
                            onKeyDown={(event) => {
                                if (event.key === "Enter" && !event.shiftKey &&
                                    !event.nativeEvent.isComposing) {
                                    event.preventDefault();
                                    if (question.trim() && !sending && !loadingMessages &&
                                        !loading && !creating) {
                                        event.currentTarget.form?.requestSubmit();
                                    }
                                }
                            }}
                        />
                        <button
                            type="submit"
                            disabled={!question.trim() || loading || loadingMessages ||
                                creating || sending}
                        >
                            {sending ? "Sending..." : "Send"}
                        </button>
                    </form>
                    <p className="chat-disclaimer">
                        AI answers may be incomplete. Verify important details in the source.
                    </p>
                </div>
            </div>
        </div>
    );
}

export default DocumentChat;
