using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Persistence;

public sealed class AcademicDbContext(DbContextOptions<AcademicDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<CatalogEdition> Catalogs => Set<CatalogEdition>();
    public DbSet<CatalogCourse> Courses => Set<CatalogCourse>();
    public DbSet<StudentProfile> Students => Set<StudentProfile>();
    public DbSet<EnrollmentAttempt> Attempts => Set<EnrollmentAttempt>();
    public DbSet<AcademicTerm> Terms => Set<AcademicTerm>();
    public DbSet<CourseSection> Sections => Set<CourseSection>();
    public DbSet<SchedulePlan> Plans => Set<SchedulePlan>();
    public DbSet<PolicyDocument> PolicyDocuments => Set<PolicyDocument>();
    public DbSet<PolicySection> PolicySections => Set<PolicySection>();
    public DbSet<AdvisorAssignment> AdvisorAssignments => Set<AdvisorAssignment>();
    public DbSet<AdvisorSuggestion> AdvisorSuggestions => Set<AdvisorSuggestion>();
    public DbSet<MutationAudit> MutationAudits => Set<MutationAudit>();

    private void ProtectAuditHistory()
    {
        if (ChangeTracker.Entries<MutationAudit>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Mutation audit entries are append-only.");
    }
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ProtectAuditHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ProtectAuditHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        // SQLite is the lightweight HTTP-test store; PostgreSQL natively supports offset timestamps.
        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            model.Entity<MutationAudit>().Property(x => x.OccurredAt).HasConversion(
                v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            model.Entity<AdvisorSuggestion>().Property(x => x.CreatedAt).HasConversion(
                v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
            model.Entity<SchedulePlan>().Property(x => x.CreatedAt).HasConversion(
                v => v.UtcDateTime, v => new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)));
        }
        model.Entity<AdvisorAssignment>(b =>
        {
            b.HasKey(x => new { x.AdvisorId, x.StudentId });
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AdvisorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StudentProfile>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AdvisorSuggestion>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AdvisorId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<StudentProfile>().WithMany().HasForeignKey(x => new { x.StudentId, x.CatalogVersion })
                .HasPrincipalKey(x => new { x.UserId, x.CatalogVersion }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AcademicTerm>().WithMany().HasForeignKey(x => x.TermCode).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MutationAudit>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.OccurredAt);
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PolicyDocument>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.HasOne<PolicyDocument>().WithMany().HasForeignKey(x => x.ReplacesDocumentId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CatalogEdition>().WithMany().HasForeignKey(x => x.CatalogVersion).OnDelete(DeleteBehavior.Restrict);
            b.ToTable("PolicyDocuments", t =>
            {
                t.HasCheckConstraint("CK_Policy_Status", "\"Status\" IN ('draft','approved','retired')");
                t.HasCheckConstraint("CK_Policy_Version", "\"Version\" > 0");
            });
        });
        model.Entity<PolicySection>(b =>
        {
            b.HasKey(x => new { x.DocumentId, x.Id });
            b.HasOne<PolicyDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AcademicTerm>(b =>
        {
            b.HasKey(x => x.Code);
            b.ToTable("Terms", t =>
            {
                t.HasCheckConstraint("CK_Term_Dates", "\"StartsOn\" < \"EndsOn\"");
                t.HasCheckConstraint("CK_Term_Kind", "\"Kind\" IN ('Spring','Fall')");
            });
        });
        model.Entity<CourseSection>(b =>
        {
            b.HasKey(x => new { x.CatalogVersion, x.Id });
            b.HasIndex(x => new { x.CatalogVersion, x.TermCode, x.SectionCode }).IsUnique();
            b.HasOne<CatalogCourse>().WithMany().HasForeignKey(x => new { x.CatalogVersion, Code = x.CourseCode }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AcademicTerm>().WithMany().HasForeignKey(x => x.TermCode).OnDelete(DeleteBehavior.Restrict);
            b.ToTable("Sections", t =>
            {
                t.HasCheckConstraint("CK_Section_Seats", "\"Capacity\" > 0 AND \"AvailableSeats\" >= 0 AND \"AvailableSeats\" <= \"Capacity\"");
                t.HasCheckConstraint("CK_Section_Times", "\"StartLocal\" < \"EndLocal\"");
            });
        });
        model.Entity<SchedulePlan>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasOne<StudentProfile>().WithMany().HasForeignKey(x => new { x.StudentId, x.CatalogVersion })
                .HasPrincipalKey(x => new { x.UserId, x.CatalogVersion }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<AcademicTerm>().WithMany().HasForeignKey(x => x.TermCode).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<AppUser>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Email).IsUnique();
            b.ToTable("Users", t => t.HasCheckConstraint("CK_User_Role", "\"Role\" IN ('student','advisor','admin')"));
        });
        model.Entity<CatalogEdition>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Revision).IsConcurrencyToken();
            b.ToTable("Catalogs", t => t.HasCheckConstraint("CK_Catalog_Status", "\"Status\" IN ('draft','published')"));
        });
        model.Entity<CatalogCourse>(b =>
        {
            b.HasKey(x => new { x.CatalogVersion, x.Code });
            b.HasOne<CatalogEdition>().WithMany().HasForeignKey(x => x.CatalogVersion).OnDelete(DeleteBehavior.Restrict);
            b.ToTable("Courses", t =>
            {
                t.HasCheckConstraint("CK_Course_Credits", "\"Credits\" > 0 AND \"Credits\" <= 30");
                t.HasCheckConstraint("CK_Course_Category", "\"Category\" IN ('core','cs_elective','gen_ed','free_elective')");
            });
        });
        model.Entity<StudentProfile>(b =>
        {
            b.HasKey(x => x.UserId);
            b.HasAlternateKey(x => new { x.UserId, x.CatalogVersion });
            b.HasOne<AppUser>().WithOne().HasForeignKey<StudentProfile>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CatalogEdition>().WithMany().HasForeignKey(x => x.CatalogVersion).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<EnrollmentAttempt>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.StudentId, x.CourseCode, x.Term }).IsUnique();
            b.HasOne<StudentProfile>().WithMany().HasForeignKey(x => new { x.StudentId, x.CatalogVersion })
                .HasPrincipalKey(x => new { x.UserId, x.CatalogVersion }).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<CatalogCourse>().WithMany().HasForeignKey(x => new { x.CatalogVersion, x.CourseCode }).OnDelete(DeleteBehavior.Restrict);
            b.ToTable("Attempts", t => t.HasCheckConstraint("CK_Attempt_Grade", "\"Grade\" IN ('A','B','C','D','F','W','I','P')"));
        });
    }
}
