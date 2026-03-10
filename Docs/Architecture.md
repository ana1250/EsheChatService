# Eshe Chat Service — Architecture Documentation

This document provides a comprehensive, ground-truth overview of the Eshe Chat Service codebase: its architecture, every component, data model, logic flow, and security posture.

---

## 1. Architecture Overview

Eshe Chat Service is a **Blazor Interactive Server** web application built on **.NET 8**. The UI runs entirely on the server and communicates with the browser over a persistent **WebSocket** connection (a "circuit"). Real-time multi-tab synchronization is handled by **ASP.NET Core SignalR**.

### Technology Stack

| Layer | Technology |
|---|---|
| Framework | Blazor Server (InteractiveServer render mode) |
| Database | SQL Server via Entity Framework Core (`Microsoft.EntityFrameworkCore.SqlServer`) |
| ORM | EF Core with `IDbContextFactory<ChatDbContext>` for thread-safe, short-lived contexts |
| Authentication | Google OAuth 2.0 + Cookie auth (`Cookies` scheme) |
| Real-Time | ASP.NET Core SignalR (WebSocket transport, automatic reconnect) |
| AI | Mistral AI REST API (`mistral-large-latest`, temp 0.7, max 1000 tokens) |
| Markdown | `Markdig` for server-side Markdown→HTML rendering |
| Client Scripts | Vanilla JavaScript (`app.js`) for scroll management, textarea auto-resize, clipboard, and code block formatting |

### Project Structure

```
EsheChatService/
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor          # Root layout, sidebar + Toaster
│   │   ├── NavMenu.razor             # Sidebar: folders, sessions, search, modals, user menu
│   │   ├── ChatSessionRow.razor      # Reusable session row component (rename, menu, select)
│   │   └── Toaster.razor             # Global toast notification renderer
│   └── Pages/
│       └── Chat.razor                # Main chat page (messages, input, Send/Stop/Regenerate)
├── Data/
│   └── ChatDbContext.cs              # EF Core DbContext with Fluent API configuration
├── Models/
│   ├── AppUser.cs                    # User entity (Email, ExternalUserId, IsActive)
│   ├── ChatFolder.cs                 # Folder entity (Name, Order, IsExpanded, UserOwnerId)
│   ├── ChatSession.cs               # Session entity (Title, timestamps, FolderId, UserOwnerId)
│   ├── ChatMessage.cs               # Message entity (Role enum, Content, ChatSessionId)
│   └── SharedSession.cs             # Sharing junction (SharedWithUserId, SharedWithEmail, RemovedAt)
├── Services/
│   ├── Chat/
│   │   ├── ChatService.cs           # AI API communication, guest mode interception
│   │   └── ChatHub.cs               # SignalR hub with authorization guards
│   ├── Repositories/
│   │   ├── IChatRepository.cs       # Repository interface (18 methods)
│   │   └── ChatRepository.cs        # EF Core implementation using IDbContextFactory
│   ├── Sessions/
│   │   └── ChatSessionManager.cs    # In-memory state manager + business logic + IDOR enforcement
│   ├── User/
│   │   ├── CurrentUser.cs           # ICurrentUser – extracts identity from HttpContext
│   │   └── UserManager.cs           # Google OAuth user validation/creation
│   └── ToastService.cs              # Event-driven toast notification queue
├── Shared/Icons/                     # SVG icon components (UserIcon, AssistantIcon)
├── wwwroot/js/app.js                 # Client-side JS (scroll, auto-resize, clipboard, code blocks)
└── Program.cs                        # DI registration, middleware pipeline, auth config, endpoints
```

---

## 2. Dependency Injection (Program.cs)

All services are registered as **Scoped** (one instance per Blazor circuit/HTTP request):

| Registration | Purpose |
|---|---|
| `AddScoped<ChatService>()` | AI API calls |
| `AddScoped<IChatRepository, ChatRepository>()` | Database access (Repository Pattern) |
| `AddScoped<ChatSessionManager>()` | In-memory state + business logic |
| `AddScoped<ToastService>()` | Toast notification queue |
| `AddScoped<UserManager>()` | OAuth user validation |
| `AddScoped<ICurrentUser, CurrentUser>()` | Identity extraction |
| `AddHttpClient<ChatService>()` | Named HttpClient for Mistral API |
| `AddDbContextFactory<ChatDbContext>()` | Thread-safe DbContext factory |
| `AddSignalR()` | Real-time hub infrastructure |
| `AddHttpContextAccessor()` | Access to HTTP request context in Blazor Server |

### Middleware Pipeline Order
`HttpsRedirection` → `StaticFiles` → `Antiforgery` → `Authentication` → `Authorization` → `MapHub<ChatHub>("/chathub")` → `MapRazorComponents`

### Endpoints
- `GET /login` — Initiates Google OAuth challenge, redirects to `/` on completion
- `GET /logout` — Signs out of cookie scheme, redirects to `/`
- `WS /chathub` — SignalR WebSocket hub

---

## 3. Data Layer

### 3A. Database Models

**`AppUser`** — Registered user record
- `Id` (Guid PK), `Email` (string), `ExternalUserId` (Google `sub` claim, nullable), `IsActive` (bool), `CreatedAt`, `LastLoginAt`

**`ChatFolder`** — User-owned organizational folder
- `Id` (Guid PK), `Name` (max 100), `Order` (int), `IsExpanded` (bool, client-side state), `UserOwnerId` (Guid)
- Has many `ChatSession` via FK `FolderId` with `OnDelete: SetNull` (deleting folder ungroups sessions)

**`ChatSession`** — A conversation thread
- `Id` (Guid PK), `Title` (max 200, auto-generated from first message), `CreatedAt`, `UpdatedAt`, `FolderId` (nullable), `UserOwnerId` (nullable Guid)
- Has many `ChatMessage` via FK `ChatSessionId` with `OnDelete: Cascade`
- Has many `SharedSession` via navigation `SharedWith`

**`ChatMessage`** — A single chat bubble
- `Id` (Guid PK, auto-generated), `Role` (enum: User, Assistant, System), `Content` (string), `CreatedAt`, `ChatSessionId` (Guid FK)

**`ChatSession.SharedWith`** → **`SharedSession`** — Sharing junction table
- `Id` (Guid PK), `ChatSessionId` (FK → Cascade), `SharedWithUserId` (Guid, resolved when known), `SharedWithEmail` (string, fallback for unregistered users), `SharedAt`, `RemovedAt` (nullable, soft-delete)

### 3B. ChatDbContext

Uses Fluent API in `OnModelCreating` with explicit key, property, and relationship configuration for all 5 entities. `DbSet` properties use expression-bodied syntax.

### 3C. IChatRepository / ChatRepository

The Repository Pattern cleanly separates EF Core operations from business logic. `ChatRepository` creates a **new `DbContext` via `_dbFactory.CreateDbContext()`** in every method, ensuring no concurrent access conflicts in async Blazor pipelines.

**18 methods** covering:
- User data: `GetUserFoldersAsync`, `GetUserSessionsAsync`, `GetSharedSessionsAsync`, `GetUserIdByEmailAsync`
- CRUD: Folders (Create, Update, Delete, DeleteWithSessions), Sessions (Create, Update, Delete), Messages (Get, Add, Delete, Exists, UpdateSessionTimeAndAdd)
- Sharing: `GetSharedSessionAsync`, `GetSharedSessionsBySessionIdAsync`, `AddSharedSessionAsync`, `UpdateSharedSessionAsync`

---

## 4. Service Layer

### 4A. ICurrentUser / CurrentUser

Extracts identity from the `HttpContext`:
- `IsAuthenticated` — checks `HttpContext.User.Identity.IsAuthenticated`
- `Email` — reads `ClaimTypes.Email` from the auth cookie
- `UserId` — **lazy-cached** Guid lookup: queries `db.Users.Where(u => u.Email == Email && u.IsActive).Select(u => u.Id).FirstOrDefault()` once per circuit, then caches in `_cachedUserId`

### 4B. UserManager

Single method: `ValidateAndUpdateGoogleUserAsync(email, sub)`. Called during the Google OAuth `OnCreatingTicket` event in `Program.cs`. Looks up the user by email using `.FirstOrDefaultAsync()` (crash-safe). If found, sets `ExternalUserId` (one-time link) and updates `LastLoginAt`. If not found, throws — preventing unregistered users from signing in.

### 4C. ChatService

Handles AI API communication:
1. **Guest interception**: If `!_currentUser.IsAuthenticated`, returns a random "please sign in" message from a hardcoded pool of 5 friendly prompts. No API call is made.
2. **Message preparation**: Filters empty/invalid messages, ensures at least one User message exists, sorts by `CreatedAt`, takes the last 10, and injects a System prompt ("You are a helpful, concise assistant") if none exists.
3. **API call**: POSTs to `https://api.mistral.ai/v1/chat/completions` with `model: "mistral-large-latest"`, `temperature: 0.7`, `max_tokens: 1000`. Uses Bearer auth from `IConfiguration["AI:ApiKey"]`.
4. **Cancellation**: Accepts a `CancellationToken`, passes it through to `HttpClient.SendAsync()`. Catches `TaskCanceledException` and returns `"*(Generation stopped by user)*"`.

### 4D. ChatSessionManager

The central state orchestrator for a Blazor circuit. Injected with `IChatRepository` and `ICurrentUser`.

**In-memory state**: `_sessions` (owned), `_sharedWithMe`, `_folders`, `_sharedByMe`, `ActiveSession`, plus a `SemaphoreSlim _loadLock` for thread-safe initialization.

**Key methods**:
- `LoadAsync()` — Double-checked locking pattern. Loads owned folders, owned sessions (with messages), and shared sessions (by `UserId` or `Email` fallback).
- `CreateSessionAsync(firstMessage)` — Auto-generates title via `GenerateTitle()` (first 40 chars, capitalized), persists to DB, inserts at position 0 in memory.
- `SetActiveAsync(sessionId)` — Refreshes messages from DB, sets `ActiveSession`.
- `AddMessageInMemory(role, content, messageId?)` — Local-only. Returns the `ChatMessage` for callers to persist asynchronously.
- `RenameSessionAsync`, `DeleteSessionAsync`, `MoveSessionToFolderAsync`, `DeleteFolderAndSessionsAsync` — All enforce `UserOwnerId == _currentUser.UserId` before executing.
- `ShareSessionAsync(sessionId, email)` — Looks up target user by email. Creates `SharedSession` with both `SharedWithUserId` and `SharedWithEmail` for forward compatibility.
- `RemoveShareAsync(sharedSessionId)` — Soft-deletes by setting `RemovedAt`. Enforces ownership.
- `OnChange` event — Subscribed by Blazor components to trigger `StateHasChanged()`.

### 4E. ToastService

Lightweight event-driven notification queue:
- `ShowToast(message, type)` — Enqueues a `ToastMessage` (with auto-generated Guid), fires `OnChange`, auto-removes after 3 seconds via `Task.Delay`.
- Types: `"info"`, `"success"`, `"error"` — each renders a different SVG icon in `Toaster.razor`.
- `RemoveToast(id)` — Manual dismissal before auto-hide.

---

## 5. Real-Time Layer (ChatHub)

SignalR hub mapped at `/chathub`. Injected with `IDbContextFactory<ChatDbContext>`.

### Authorization Guard
Private `IsUserAuthorizedForSession(sessionId)` method:
1. Extracts `ClaimTypes.Email` from `Context.User`
2. Looks up the active user in the DB
3. Checks if the user is the session owner OR has an active `SharedSession` (not soft-deleted)
4. Returns `bool` — called at the start of every method

### Endpoints

| Method | Broadcast Target | Purpose |
|---|---|---|
| `JoinSession(sessionId)` | — | Adds connection to the SignalR group (after auth check) |
| `LeaveSession(sessionId)` | — | Removes connection from the group |
| `SendAssistantMessage(sessionId, messageId, content)` | `.OthersInGroup()` | Syncs AI reply to other tabs |
| `SendUserMessage(sessionId, messageId, content)` | `.OthersInGroup()` | Syncs user message to other tabs |
| `AssistantTyping(sessionId, isTyping)` | `.OthersInGroup()` | Syncs typing indicator to other tabs |

All broadcast methods use `.OthersInGroup()` to exclude the sender, preventing redundant re-renders and network round-trips.

---

## 6. UI Layer

### 6A. MainLayout.razor

Root layout: renders a collapsible `<NavMenu>` sidebar and the page `@Body`. Includes `<Toaster />` for global notifications. Calls `SessionManager.LoadAsync()` on initialization.

### 6B. NavMenu.razor (820 lines)

The most complex UI component. Features:
- **Search**: Live text filter across session titles
- **Folder tree**: Expandable folders with drag-to-move, inline rename (double-click or Enter), context menu (Rename, Delete)
- **Session list**: Grouped by folder + "Ungrouped" section + "Shared with you" section. Uses `ChatSessionRow` component.
- **Context menus**: Rename, Move, Share, Delete — each opens a dedicated modal
- **Modals** (5 total):
  - **Delete Chat** — Simple confirmation
  - **Delete Folder** — Radio choice: "Move chats to Ungrouped" vs "Delete all chats"
  - **Move Chat** — Folder picker with radio buttons + inline "New folder" creation
  - **Share Chat** — Email input + list of current shares with remove buttons
  - **Profile** — Email + provider display
- **User menu**: Avatar + masked email, Profile/Logout options for authenticated users; Sign In button for guests
- **Sidebar collapse**: Toggle between full sidebar and icon-only mode; `@bind-IsCollapsed` propagated to `MainLayout`

### 6C. ChatSessionRow.razor

Reusable component for rendering a single session in the nav. Features:
- Click to select, double-click to rename, `Escape` to cancel
- Inline rename input with Enter to commit, Escape/blur to cancel
- Three-dot context menu (⋯) for Rename, Move, Share, Delete
- Auto-focus on rename input via `OnAfterRenderAsync`
- Suppresses click after double-click to prevent double-navigation

### 6D. Chat.razor (600 lines)

The main chat page, routed at `/`, `/chat`, and `/chat/{SessionId:guid}`.

**State variables**: `_isSending`, `_assistantTyping`, `_scrollIntent`, `_showJumpToLatest`, `_shouldFocusChatInput`, `UserInput`, `_cts` (CancellationTokenSource), `_hub` (HubConnection), `_messagesRef`, `_chatInputRef`.

**Initialization** (`OnInitializedAsync`):
- Subscribes to `SessionManager.OnChange`
- Builds a SignalR `HubConnection` with WebSocket transport + automatic reconnect
- Registers `.On` listeners: `AssistantMessageReceived`, `UserMessageReceived`, `AssistantTyping` — each calculates `isNearBottom` *before* updating state to prevent scroll drift

**Session Navigation** (`OnParametersSetAsync`):
- Detects when `SessionId` changes, leaves old SignalR group, joins new one
- Calls `SessionManager.SetActiveAsync()`

**Send() — Fluid Input Pipeline**:
1. Guards: `_isSending` or empty input → return. If `_assistantTyping` → `StopGeneration()` + `Task.Yield()`.
2. Captures `UserInput`, clears it, resets textarea height.
3. Creates session if new (persists + joins SignalR group).
4. Adds user message to memory, triggers `StateHasChanged`.
5. Background `Task.Run`: Broadcasts user message + persists via `IChatRepository` (fresh `IServiceScopeFactory` scope).
6. Signals `AssistantTyping(true)` via SignalR.
7. **Sets `_isSending = false`** — releases the input lock so the user can queue another message while the AI is thinking.
8. Creates `CancellationTokenSource`, captures token locally.
9. Awaits `ChatService.GetReplyAsync()`. On `TaskCanceledException`: sets reply to `*(Generation stopped by user)*`. On other exceptions: renders error message.
10. Clears typing state, disposes CTS, signals `AssistantTyping(false)`.
11. Adds assistant message to memory, renders, triggers scroll.
12. Background `Task.Run`: Broadcasts + persists assistant message.

**RegenerateLast()**: Removes the last assistant message (memory + DB), re-calls `GetReplyAsync`, renders new reply. Handles `TaskCanceledException` gracefully.

**HandleKeyDown**: `Enter` → `Send()`. `Shift+Enter` → allows newline (browser default). The `app.js` `keydown` listener calls `e.preventDefault()` on raw Enter to stop the textarea from inserting a newline before Blazor processes it.

**StopGeneration()**: Calls `_cts?.Cancel()`, sets `_assistantTyping = false`, triggers `StateHasChanged`.

**Auto-Scroll**: Uses `ScrollIntent.ForceScroll` enum. JS `chatScroll.isNearBottom` (150px threshold) determines if user is near bottom. SignalR events pre-compute before DOM update. A "Jump to latest" floating button appears when scrolled up.

**ReadOnly Mode**: `IsReadOnly()` returns `true` if `UserOwnerId != CurrentUser.UserId` (shared sessions). Disables textarea, hides Send/Stop/Regenerate buttons.

**Markdown Rendering**: `ToHtml()` uses `Markdig` pipeline. CSS in `Chat.razor.css` comprehensively themes all Markdig output: `<p>`, `<code>` (inline + block), `<table>`, `<blockquote>` (blurple left border), `<a>`, `<ul>/<ol>`, `<hr>`.

**Code Block Formatting**: `formatCodeBlocks()` in `app.js` runs after each render. Wraps `<pre><code>` elements in styled containers with a language label header and per-block "Copy code" button via `navigator.clipboard`.

**Dispose** (`IAsyncDisposable`): Unsubscribes from `SessionManager.OnChange`, disposes CTS, stops and disposes `HubConnection`.

### 6E. Toaster.razor

Renders `ToastService.Toasts` as floating cards in the bottom-right corner. Each toast displays a contextual SVG icon (checkmark for success, X for error, info circle for info), the message text, and a manual dismiss button. Subscribes/unsubscribes to `ToastService.OnChange` via `IDisposable`.

### 6F. app.js

Client-side JavaScript module (`window.chatScroll`):
- `scrollToBottom(el)` — Scrolls the messages container to maximum scroll position
- `isNearBottom(el)` — Returns `true` if within 150px of the bottom
- `initAutoResize(el)` — Attaches `input` and `keydown` listeners to the textarea. Dynamically adjusts height up to 200px. Prevents default Enter (but not Shift+Enter).
- `resetAutoResize(el)` — Resets textarea height after send
- `copyText(text)` — `navigator.clipboard.writeText` wrapper
- `formatCodeBlocks()` — Finds unprocessed `<pre><code>` elements, detects language from CSS class, wraps in styled container with header (language label + Copy button)

---

## 7. Security

### Authentication
Google OAuth 2.0 flow via `AddGoogle("Google", ...)`. The `OnCreatingTicket` event calls `UserManager.ValidateAndUpdateGoogleUserAsync` — if the email is not in the `AppUser` table, the login is rejected with `ctx.Fail()`. Cookie auth persists the session.

### IDOR Prevention (Backend)
Every data mutation in `ChatSessionManager` filters by `UserOwnerId == _currentUser.UserId`:
- `RenameSessionAsync`, `DeleteSessionAsync`, `MoveSessionToFolderAsync`, `ShareSessionAsync` — session ownership
- `RenameFolderAsync`, `DeleteFolderAsync`, `DeleteFolderAndSessionsAsync` — folder ownership  
- `DeleteMessageAsync` — checks that the message's session is owned by the current user
- `RemoveShareAsync` — verifies session ownership before soft-deleting

### SignalR Endpoint Hardening
All 5 hub methods (`JoinSession`, `LeaveSession`, `SendAssistantMessage`, `SendUserMessage`, `AssistantTyping`) run `IsUserAuthorizedForSession()` which queries the database to verify the caller is either the owner or an active shared viewer. Unauthorized callers are silently dropped.

### Thread Safety
- `IDbContextFactory<ChatDbContext>` ensures each async operation gets its own `DbContext`, preventing `InvalidOperationException` from concurrent EF Core tracking.
- `IServiceScopeFactory` is used inside `Task.Run` blocks in `Chat.razor` to create isolated DI scopes for background persistence, avoiding stale scoped service references.
- `SemaphoreSlim _loadLock` in `ChatSessionManager.LoadAsync()` implements double-checked locking to prevent concurrent initializations.

### Resilience
- `.FirstOrDefault()` / `.FirstOrDefaultAsync()` used everywhere instead of `.Single()` to prevent hard crashes on edge-case data anomalies.
- All foreground SignalR calls guard with `_hub.State == HubConnectionState.Connected`.
- AI API errors are caught and rendered as user-visible error messages instead of crashing the circuit.
- `TaskCanceledException` is caught cleanly in both `Send()` and `RegenerateLast()`.

### API Keys
`AI:ApiKey` and `Auth:Google:ClientId`/`ClientSecret` are read from `IConfiguration` (typically `appsettings.json` / user secrets), keeping secrets out of source control.

### CSRF
`app.UseAntiforgery()` is deployed in the middleware pipeline. Blazor Server circuits inherently protect against CSRF via their WebSocket-based architecture.

### Message Deduplication
All tabs agree on a specific `Guid` for each message before persistence. `MessageExistsAsync(messageId)` is checked before every insert to prevent duplicate rows when multiple tabs attempt to save the same broadcast.
