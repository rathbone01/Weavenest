namespace Weavenest.DataAccess.Interfaces;

public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
    string? EncryptNullable(string? plaintext);
    string? DecryptNullable(string? ciphertext);
    bool IsReady { get; }
}
