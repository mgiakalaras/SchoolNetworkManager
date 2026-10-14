namespace SchoolNetworkManager.Models;

/// <summary>Πόσες συσκευές ήταν online σε κάθε αυτόματο έλεγχο (για το γράφημα της εβδομάδας).</summary>
public class StatusSnapshot
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int Online { get; set; }
    public int Total { get; set; }
}
