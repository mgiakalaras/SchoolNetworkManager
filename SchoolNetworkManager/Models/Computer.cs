using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace SchoolNetworkManager.Models;

public class Computer
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Η IP Address είναι υποχρεωτική.")]
    [Display(Name = "IP Address")]
    public string IpAddress { get; set; } = string.Empty;

    [Display(Name = "Τύπος Μηχανήματος")]
    public string MachineType { get; set; } = MachineTypes.Default;

    [Display(Name = "Name")]
    public string? Name { get; set; }

    [Display(Name = "MAC Address")]
    public string? MacAddress { get; set; }

    [Display(Name = "Location")]
    public string? Location { get; set; }

    [Display(Name = "Username")]
    public string? Username { get; set; }

    [Display(Name = "Password")]
    public string? Password { get; set; }

    [Display(Name = "Extras")]
    public string? Extras { get; set; }

    [Display(Name = "NOTES")]
    public string? Notes { get; set; }

    [Display(Name = "Σημειώσεις")]
    public string? Comments { get; set; }

    [Display(Name = "LAN Messenger")]
    public bool HasLanMessenger { get; set; }

    [Display(Name = "Veyon")]
    public bool HasVeyon { get; set; }

    [Display(Name = "Online")]
    public bool? IsOnline { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? UpdatedAt { get; set; }
    [NotMapped]
    public bool IsEmptySlot =>
        !string.IsNullOrWhiteSpace(Name) &&
        Name.Trim().Contains("EMPTY", StringComparison.OrdinalIgnoreCase);

    [NotMapped]
    public string DisplayMachineType =>
        IsEmptySlot ? "-" :
        string.IsNullOrWhiteSpace(MachineType) ? MachineTypes.Default : MachineType;
}