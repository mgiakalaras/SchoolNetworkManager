namespace SchoolNetworkManager.Models;

/// <summary>Κτίριο του σχολείου· περιέχει ορόφους.</summary>
public class Building
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>Όροφος ενός κτιρίου· περιέχει χώρους και προαιρετικά κάτοψη.</summary>
public class Floor
{
    public int Id { get; set; }

    public int BuildingId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Αριθμός ορόφου (0 = ισόγειο, αρνητικός = υπόγειο).</summary>
    public int Level { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Σχετική διαδρομή της εικόνας κάτοψης (προαιρετική).</summary>
    public string? PlanImagePath { get; set; }

    /// <summary>Λόγος πλάτους/ύψους της κάτοψης.</summary>
    public double AspectRatio { get; set; } = 1.6;
}
