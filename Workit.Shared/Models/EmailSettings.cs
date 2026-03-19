namespace Workit.Shared.Models;

public sealed class EmailSettings
{
    public Guid   Id               { get; set; } = Guid.NewGuid();
    public Guid   CompanyId        { get; set; }
    public string ImapHost         { get; set; } = string.Empty;
    public int    ImapPort         { get; set; } = 993;
    public bool   UseSsl           { get; set; } = true;
    public string Username         { get; set; } = string.Empty;
    /// <summary>Stored as plaintext — the DB is the trust boundary.</summary>
    public string Password         { get; set; } = string.Empty;
    public string InvoiceFolder    { get; set; } = "INBOX";
    public bool   AutoScanEnabled  { get; set; } = false;
    public DateTimeOffset? LastScannedAt { get; set; }
}
