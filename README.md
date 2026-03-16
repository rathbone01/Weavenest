<p align="center">
  <h1 align="center">Weavenest</h1>
  <p align="center">
    A private, self-hosted AI chat assistant powered by local LLMs through <a href="https://ollama.com">Ollama</a>.
    <br />
    No cloud. No telemetry. Your hardware, your data.
  </p>
</p>

---

## What is Weavenest?

Weavenest is a multi-user web chat interface for locally-hosted large language models. It connects to an [Ollama](https://ollama.com) server running on your network and provides a polished chat experience with optional agentic web search — all without sending a single byte to external services.

Built for individuals and small groups who want the power of modern AI assistants while keeping full control of their data and infrastructure.

## Features

- **Real-time streaming** — Token-by-token response streaming with live context window tracking
- **Agentic web search** — The model can search the web (via SearXNG) and fetch page content to answer questions with up-to-date information
- **Multi-user support** — Registration, login, and per-user chat history with JWT authentication
- **Model selection** — Switch between any model available on your Ollama server
- **Thinking/reasoning display** — See the internal reasoning of deep-thinking models (DeepSeek, QwQ, etc.)
- **User Memory** — Persistent per-user prompt that lets the AI remember your preferences across sessions
- **Folder organization** — Group and manage chat sessions into folders
- **Markdown rendering** — Rich message formatting with full Markdown support
- **Dark theme** — Purpose-built dark UI with a purple accent palette
- **No external dependencies at runtime** — Runs entirely on your local network

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | [Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) (Interactive Server) |
| UI Components | [MudBlazor](https://mudblazor.com) |
| Backend | ASP.NET Core (.NET 10) |
| Database | SQL Server + Entity Framework Core |
| AI Integration | [Ollama](https://ollama.com) via [OllamaSharp](https://github.com/awaescher/OllamaSharp) |
| Web Search | [SearXNG](https://docs.searxng.org) (self-hosted) |
| HTML Parsing | [HtmlAgilityPack](https://html-agility-pack.net) |
| Logging | [Serilog](https://serilog.net) |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [SQL Server](https://www.microsoft.com/sql-server) (Express, Developer, or full — LocalDB works too)
- [Ollama](https://ollama.com) running on an accessible host with at least one model pulled
- [Docker](https://www.docker.com) (optional, but recommended for running SearXNG)

## Quick Start

### 1. Clone the repository

```bash
git clone https://github.com/rathbone01/Weavenest.git
cd Weavenest
```

### 2. Configure the application

Edit `Weavenest/appsettings.json`:

```jsonc
{
  "ConnectionStrings": {
    // Point to your SQL Server instance
    "DefaultConnection": "Server=localhost;Database=Weavenest;Trusted_Connection=true;TrustServerCertificate=true"
  },
  "Jwt": {
    // IMPORTANT: Replace with your own base64 key (at least 256 bits)
    "Key": "<your-secret-key>",
    "Issuer": "Weavenest",
    "ExpirationMinutes": 480
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "Model": "qwen3:8b",
    "DefaultContextLength": 8192
  },
  "SearXNG": {
    // URL of your SearXNG instance — see setup below
    "BaseUrl": "http://localhost:8080"
  }
}
```

> **Tip:** Generate a JWT key with: `openssl rand -base64 32`

### 3. Apply database migrations

```bash
cd Weavenest
dotnet ef database update --project ../Weavenest.DataAccess
```

Or simply run the application — migrations are applied automatically in development mode.

### 4. Run

```bash
dotnet run --project Weavenest
```

Navigate to `https://localhost:5001` (or the port shown in your terminal), register an account, and start chatting.

---

## Web Search Setup (SearXNG)

Web search is powered by [SearXNG](https://docs.searxng.org), a self-hosted, privacy-respecting meta-search engine. It aggregates results from multiple search engines without tracking you.

Web search is **optional** — it can be toggled per-conversation via the settings cog in the chat header. When disabled, Weavenest falls back to standard streaming chat with no outbound requests.

### Running SearXNG with Docker

The quickest way to get SearXNG running locally:

```bash
docker run -d \
  --name searxng \
  -p 8080:8080 \
  -e SEARXNG_SECRET_KEY=change-me-to-a-random-string \
  searxng/searxng:latest
```

SearXNG will be available at `http://localhost:8080`.

### Enable JSON format (required)

By default SearXNG disables the JSON output format that Weavenest needs. You must enable it in the SearXNG configuration.

**Option A — Docker bind mount (recommended)**

Create a `searxng/settings.yml` file and mount it into the container:

```yaml
# searxng/settings.yml
server:
  secret_key: "change-me-to-a-random-string"

search:
  formats:
    - html
    - json
```

Then run with the config mounted:

```bash
docker run -d \
  --name searxng \
  -p 8080:8080 \
  -v $(pwd)/searxng:/etc/searxng \
  searxng/searxng:latest
```

**Option B — Docker Compose**

```yaml
# docker-compose.yml
services:
  searxng:
    image: searxng/searxng:latest
    container_name: searxng
    ports:
      - "8080:8080"
    volumes:
      - ./searxng:/etc/searxng
    environment:
      - SEARXNG_SECRET_KEY=change-me-to-a-random-string
    restart: unless-stopped
```

```bash
docker compose up -d
```

Verify it's working by visiting `http://localhost:8080` and running a search.

### Update appsettings.json

Point Weavenest at your SearXNG instance:

```json
"SearXNG": {
  "BaseUrl": "http://localhost:8080"
}
```

---

## How Web Search Works

When web search is enabled and you send a message, Weavenest runs an **agentic loop**:

1. Your message is sent to Ollama with two tool definitions: `web_search` and `web_fetch`
2. If the model decides to search, it calls `web_search` — Weavenest queries SearXNG and returns the top results
3. The model can then call `web_fetch` to read the full content of a specific page — **you will be prompted to approve the URL** before any fetch occurs
4. Once you approve a domain, it's whitelisted for the rest of that session (no further prompts for the same domain)
5. The model synthesizes a final answer with citations from the sources it found

Tool activity (search queries, results, fetch requests) is shown inline as compact cards in the chat, but is **not persisted** — only the final assistant reply is saved to your chat history.

> **Note:** Web fetch approval exists to protect against prompt injection from malicious web pages. Only approve domains you recognise and trust.

---

## Project Structure

```
Weavenest/
├── Weavenest/                  # Main web application (Blazor Server)
│   ├── Components/
│   │   ├── Pages/              # Chat, Login, Register, Folders
│   │   ├── Chat/               # ChatBubble, ToolMessageBubble, WebFetchApprovalDialog
│   │   ├── Folders/            # FolderDialog
│   │   └── Layout/             # MainLayout, NavMenu, AuthLayout
│   ├── Configuration/          # DI registration, theming
│   └── wwwroot/                # Static assets
│
├── Weavenest.Services/         # Business logic layer
│   ├── AgenticChatService.cs   # Agentic loop with tool calling
│   ├── OllamaService.cs        # LLM streaming (web search off)
│   ├── Tools/                  # SearXNGSearchTool, HtmlAgilityPackFetchTool
│   ├── AuthService.cs          # Authentication & registration
│   ├── TokenService.cs         # JWT generation & validation
│   └── CircuitSettings.cs      # Per-session state & system prompt
│
└── Weavenest.DataAccess/       # Data access layer
    ├── Models/                  # User, ChatSession, ChatMessage, Folder
    ├── Data/                    # DbContext & EF configurations
    ├── Repositories/            # Repository pattern implementations
    └── Migrations/              # EF Core migrations
```

## Configuration Reference

| Setting | Description | Default |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server connection string | `localhost` with trusted auth |
| `Jwt:Key` | Secret key for signing JWT tokens | — (must be set) |
| `Jwt:Issuer` | JWT issuer claim | `Weavenest` |
| `Jwt:ExpirationMinutes` | Token lifetime in minutes | `480` (8 hours) |
| `Ollama:BaseUrl` | URL of your Ollama server | `http://localhost:11434` |
| `Ollama:Model` | Model to pre-select (empty = first available) | `qwen3:8b` |
| `Ollama:DefaultContextLength` | Fallback context window size in tokens | `8192` |
| `SearXNG:BaseUrl` | URL of your SearXNG instance | `http://localhost:8080` |

## Ollama Setup

If you haven't set up Ollama yet:

```bash
# Install Ollama (Linux/macOS)
curl -fsSL https://ollama.com/install.sh | sh

# Pull a model
ollama pull qwen3:8b          # Recommended — good tool calling support
ollama pull deepseek-r1:8b    # For thinking/reasoning support

# Ollama serves on port 11434 by default
```

To allow connections from other machines on your network, set the `OLLAMA_HOST` environment variable:

```bash
OLLAMA_HOST=0.0.0.0 ollama serve
```

> **Tip:** For best web search results, use a model with strong tool-calling capability such as `qwen3:8b`, `mistral-nemo`, or any Llama 3.x variant.

## Contributing

Contributions are welcome! Whether it's bug fixes, new features, or documentation improvements — feel free to open an issue or submit a pull request.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-feature`)
3. Commit your changes
4. Push to your fork and open a Pull Request

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE). See the [LICENSE](LICENSE) file for details.

In short: you're free to use, modify, and distribute this software, but any derivative work must also be released under GPL-3.0.

## Acknowledgments

- [Ollama](https://ollama.com) — Local LLM inference made simple
- [OllamaSharp](https://github.com/awaescher/OllamaSharp) — .NET client for Ollama
- [SearXNG](https://docs.searxng.org) — Self-hosted privacy-respecting search
- [HtmlAgilityPack](https://html-agility-pack.net) — HTML parsing for .NET
- [MudBlazor](https://mudblazor.com) — Material Design components for Blazor
- [Serilog](https://serilog.net) — Structured logging for .NET
