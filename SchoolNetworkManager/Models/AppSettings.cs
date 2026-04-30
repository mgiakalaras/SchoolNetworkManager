using System.ComponentModel.DataAnnotations;

namespace SchoolNetworkManager.Models;

public class AppSettings
{
    public int Id { get; set; } = 1;

    [Display(Name = "Τίτλος")]
    public string SchoolTitle { get; set; } = "School Network Manager";

    [Display(Name = "Υπότιτλος")]
    public string SchoolSubtitle { get; set; } = "Γρήγορη εικόνα του σχολικού δικτύου";

    [Display(Name = "Λογότυπο")]
    public string? LogoPath { get; set; }
}