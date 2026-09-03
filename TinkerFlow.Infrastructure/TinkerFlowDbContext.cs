using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TinkerFlow.Domain.Entities;

namespace TinkerFlow.Infrastructure;

public class TinkerFlowDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
{
    public TinkerFlowDbContext(DbContextOptions<TinkerFlowDbContext> options) : base(options)
    {
    }
    
    public DbSet<Student> Students { get; set; }
    public DbSet<Group> Groups { get; set; }
    public DbSet<Project> Projects { get; set; }
    public DbSet<PrintLog> PrintLogs { get; set; }
    public DbSet<ClassSession> ClassSessions { get; set; }
    public DbSet<StudentProject> StudentProjects { get; set; }
    public DbSet<TrainerGroupList> TrainerGroupLists { get; set; }
    public DbSet<Branch> Branches { get; set; }
    public DbSet<UserBranch> UserBranches { get; set; }
    public DbSet<GroupSubstitute> GroupSubstitutes { get; set; }
    public DbSet<SystemSetting> SystemSettings { get; set; }
    public DbSet<PrintBatch>  PrintBatches { get; set; }
    public DbSet<PrintJob> PrintJobs { get; set; }
    public DbSet<TrainerGroupListItem> TrainerGroupListItems { get; set; }
    public DbSet<StudentGroupHistory> StudentGroupHistories { get; set; }
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // KRYTYCZNE

        // 1. Relacja Trainer -> GroupList (stara, zostaje bez zmian)
        builder.Entity<TrainerGroupList>()
            .HasOne(tgl => tgl.Trainer)
            .WithMany(u => u.FavoriteLists)
            .HasForeignKey(tgl => tgl.TrainerId)
            .OnDelete(DeleteBehavior.Cascade);

        // 2. Tabela Łącznikowa: UserBranch (Klucz złożony)
        builder.Entity<UserBranch>()
            .HasKey(ub => new { ub.UserId, ub.BranchId });

        builder.Entity<UserBranch>()
            .HasOne(ub => ub.User)
            .WithMany(u => u.UserBranches)
            .HasForeignKey(ub => ub.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<UserBranch>()
            .HasOne(ub => ub.Branch)
            .WithMany(b => b.UserBranches)
            .HasForeignKey(ub => ub.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        // 3. Relacja Group -> Branch
        builder.Entity<Group>()
            .HasOne(g => g.Branch)
            .WithMany(b => b.Groups)
            .HasForeignKey(g => g.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        // 4. Relacja Group -> PrimaryTrainer
        builder.Entity<Group>()
            .HasOne(g => g.PrimaryTrainer)
            .WithMany(u => u.PrimaryGroups)
            .HasForeignKey(g => g.PrimaryTrainerId)
            .OnDelete(DeleteBehavior.SetNull);

        // 4a. Relacja Group -> AssignedPrinter
        builder.Entity<Group>()
            .HasOne(g => g.AssignedPrinter)
            .WithMany(u => u.AssignedPrinterGroups)
            .HasForeignKey(g => g.AssignedPrinterId)
            .OnDelete(DeleteBehavior.SetNull);

        // 5. Relacja GroupSubstitute -> Group & Trainer
        builder.Entity<GroupSubstitute>()
            .HasOne(gs => gs.Group)
            .WithMany(g => g.Substitutes)
            .HasForeignKey(gs => gs.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<GroupSubstitute>()
            .HasOne(gs => gs.SubstituteTrainer)
            .WithMany(u => u.SubstituteAssignments)
            .HasForeignKey(gs => gs.SubstituteTrainerId)
            .OnDelete(DeleteBehavior.Restrict);

        // KROK 2: Relacje i klucz dla TrainerGroupListItem
        builder.Entity<TrainerGroupListItem>()
            .HasKey(tgli => new { tgli.TrainerGroupListId, tgli.GroupId }); // Klucz złożony

        builder.Entity<TrainerGroupListItem>()
            .HasOne(tgli => tgli.TrainerGroupList)
            .WithMany(tgl => tgl.ListItems) // Odwołujemy się do kolekcji w encji-matce
            .HasForeignKey(tgli => tgli.TrainerGroupListId)
            .OnDelete(DeleteBehavior.Cascade); // Gdy usuniemy listę, wpisy się same wyczyszczą

        builder.Entity<TrainerGroupListItem>()
            .HasOne(tgli => tgli.Group)
            .WithMany() // Encja Group nie musi o tym wiedzieć
            .HasForeignKey(tgli => tgli.GroupId)
            .OnDelete(DeleteBehavior.Cascade); // Gdy usuniemy grupę całkowicie z bazy, zniknie z list

        // 6. Relacja Student -> Branch (opcjonalna, dla uczniów bez grupy)
        builder.Entity<Student>()
            .HasOne(s => s.Branch)
            .WithMany()
            .HasForeignKey(s => s.BranchId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}