using Microsoft.AspNetCore.Identity;
using TinkerFlow.Domain.Enums;

namespace TinkerFlow.Domain.Entities;

public class User : IdentityUser<Guid>
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    
    public UserRole Role { get; set; }

    public bool IsActive { get; set; } = true;
    
    public bool MustChangePassword { get; set; } = false;
    
    public bool CanActAsTrainer { get; set; } = false;
    
    public bool CanActAsPrinter { get; set; } = false;
    
    public ICollection<TrainerGroupList> FavoriteLists { get; set; } = new List<TrainerGroupList>();
    
    public ICollection<UserBranch> UserBranches { get; set; } = new List<UserBranch>(); // Do jakich oddziałów należy
    public ICollection<Group> PrimaryGroups { get; set; } = new List<Group>(); // Grupy, w których jest głównym trenerem
    public ICollection<GroupSubstitute> SubstituteAssignments { get; set; } = new List<GroupSubstitute>(); // Jego zastępstwa
    public ICollection<Group> AssignedPrinterGroups { get; set; } = new List<Group>(); // Grupy, dla których jest drukarzem
}