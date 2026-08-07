using HouseholdTasks.Server.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace HouseholdTasks.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<FamilyMember> FamilyMembers => Set<FamilyMember>();
    public DbSet<HouseholdTask> Tasks => Set<HouseholdTask>();
    public DbSet<TaskAssignment> TaskAssignments => Set<TaskAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FamilyMember>()
            .HasIndex(m => m.Email)
            .IsUnique();

        modelBuilder.Entity<TaskAssignment>()
            .HasOne(a => a.HouseholdTask)
            .WithMany(t => t.Assignments)
            .HasForeignKey(a => a.HouseholdTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TaskAssignment>()
            .HasOne(a => a.FamilyMember)
            .WithMany(m => m.Assignments)
            .HasForeignKey(a => a.FamilyMemberId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<HouseholdTask>()
            .HasOne(t => t.CompletedByMember)
            .WithMany()
            .HasForeignKey(t => t.CompletedByMemberId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
