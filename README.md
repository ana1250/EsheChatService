# Eshe Chat Service

A real-time AI chat application built with **Blazor Server**, **SignalR**, and the **Mistral AI API**. Features multi-tab synchronization, session management, sharing, and a polished dark-mode UI.

## ✨ Features

- **AI Chat** — Conversational interface powered by Mistral AI with Markdown rendering and syntax-highlighted code blocks
- **Fluid Input** — Type your next message while the AI is still generating; sending automatically cancels the current generation
- **Real-Time Sync** — Multiple browser tabs stay in sync via SignalR (messages, typing indicators)
- **Session Management** — Create, rename, delete, and organize chats into folders
- **Sharing** — Share sessions with other users by email (read-only access)
- **Stop & Regenerate** — Cancel in-progress AI responses and retry with one click
- **Code Blocks** — Auto-formatted with language labels and per-block copy buttons
- **Toast Notifications** — Non-blocking feedback for actions (copy, share, delete, etc.)
- **Guest Mode** — Unauthenticated users receive friendly sign-in prompts without consuming API credits
- **Security** — IDOR prevention, SignalR endpoint hardening, ownership-enforced mutations

## 🛠️ Tech Stack

| Layer | Technology |
|---|---|
| Framework | .NET 8 / Blazor Server |
| Database | SQL Server + Entity Framework Core |
| Auth | Google OAuth 2.0 + Cookie Authentication |
| Real-Time | ASP.NET Core SignalR |
| AI | Mistral AI API |
| Markdown | Markdig |

## 📁 Project Structure

```
EsheChatService/
├── Components/
│   ├── Layout/       # MainLayout, NavMenu, Toaster, ChatSessionRow
│   └── Pages/        # Chat.razor (main chat interface)
├── Data/             # ChatDbContext (EF Core)
├── Models/           # AppUser, ChatSession, ChatMessage, ChatFolder, SharedSession
├── Services/
│   ├── Chat/         # ChatService (AI), ChatHub (SignalR)
│   ├── Repositories/ # IChatRepository / ChatRepository
│   ├── Sessions/     # ChatSessionManager (state orchestrator)
│   └── User/         # CurrentUser, UserManager
└── wwwroot/js/       # app.js (scroll, clipboard, code blocks)
```

## 🚀 Getting Started

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- SQL Server (LocalDB or full instance)
- A [Google Cloud](https://console.cloud.google.com/) project with OAuth 2.0 credentials
- A [Mistral AI](https://mistral.ai/) API key

### Configuration

Update `appsettings.json` with your credentials:

```json
{
  "ConnectionStrings": {
    "ChatDb": "Your SQL Server connection string"
  },
  "Auth": {
    "Google": {
      "ClientId": "your-google-client-id",
      "ClientSecret": "your-google-client-secret"
    }
  },
  "AI": {
    "ApiKey": "your-mistral-api-key"
  }
}
```

### Run

```bash
cd EsheChatService/EsheChatService
dotnet ef database update    # Apply EF Core migrations
dotnet run
```

Open `https://localhost:5001` in your browser.

## 📐 Architecture

See [Docs/Architecture.md](Docs/Architecture.md) for a comprehensive deep-dive into every component, service, data model, and security mechanism.

## 📄 License

This project is for personal/portfolio use.
