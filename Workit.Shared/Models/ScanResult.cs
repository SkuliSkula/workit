namespace Workit.Shared.Models;

public sealed class ScanResult
{
    public int     Imported   { get; set; }
    public int     Skipped    { get; set; }
    public int     Errors     { get; set; }
    public string? FatalError { get; set; }
    public bool    Success    => FatalError is null;
}
