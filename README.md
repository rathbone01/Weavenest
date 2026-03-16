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

Weavenest is a multi-user web chat interface for locally-hosted large language models. It connects to an [Ollama](https://ollama.com) server running on your network and provides a polished, real-time streaming chat experience — all without sending a single byte to external services.

Built for individuals and small groups who want the power of modern AI assistants while keeping full control of their data and infrastructure.

## Features

- **Real-time streaming** — Token-by-token response streaming with live context window tracking
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
| Logging | [Serilog](https://serilog.net) |

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [SQL Server](https://www.microsoft.com/sql-server) (Express, Developer, or full — LocalDB works too)
- [Ollama](https://ollama.com) running on an accessible host with at least one model pulled

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
    // Point to your Ollama server
    "BaseUrl": "http://localhost:11434",
    "DefaultModel": "",
    "DefaultContextLength": 8192
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

## Project Structure

```
Weavenest/
├── Weavenest/                  # Main web application (Blazor Server)
│   ├── Components/
│   │   ├── Pages/              # Chat, Login, Register, Folders
│   │   ├── Chat/               # ChatBubble, ContextWindowDisplay
│   │   ├── Folders/            # FolderDialog
│   │   └── Layout/             # MainLayout, NavMenu, AuthLayout
│   ├── Configuration/          # DI registration, theming
│   └── wwwroot/                # Static assets
│
├── Weavenest.Services/         # Business logic layer
│   ├── OllamaService.cs        # LLM communication & streaming
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
| `Ollama:BaseUrl` | URL of your Ollama server | `http://192.168.0.23:11434` |
| `Ollama:DefaultModel` | Model to pre-select (empty = user picks) | `""` |
| `Ollama:DefaultContextLength` | Default context window size in tokens | `8192` |

## Ollama Setup

If you haven't set up Ollama yet:

```bash
# Install Ollama (Linux/macOS)
curl -fsSL https://ollama.com/install.sh | sh

# Pull a model
ollama pull llama3
ollama pull deepseek-r1:8b    # For thinking/reasoning support

# Ollama serves on port 11434 by default
```

To allow connections from other machines on your network, set the `OLLAMA_HOST` environment variable:

```bash
OLLAMA_HOST=0.0.0.0 ollama serve
```

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
- [MudBlazor](https://mudblazor.com) — Material Design components for Blazor
- [Serilog](https://serilog.net) — Structured logging for .NET
