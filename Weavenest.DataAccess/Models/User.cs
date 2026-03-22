namespace Weavenest.DataAccess.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? UserPrompt { get; set; }
    public byte[] EncryptionSalt { get; set; } = [];
    public bool IsDataEncrypted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ChatSession> Sessions { get; set; } = [];
    public List<Folder> Folders { get; set; } = [];
}
