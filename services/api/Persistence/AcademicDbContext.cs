using Microsoft.EntityFrameworkCore;

namespace DegreePilot.Api.Persistence;

public sealed class AcademicDbContext(DbContextOptions<AcademicDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<CatalogEdition> Catalogs => Set<CatalogEdition>();
    public DbSet<CatalogCourse> Courses => Set<CatalogCourse>();
    public DbSet<StudentProfile> Students => Set<StudentProfile>();
    public DbSet<EnrollmentAttempt> Attempts => Set<EnrollmentAttempt>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<AppUser>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Email).IsUnique();
            b.ToTable("Users", t => t.HasCheckConstraint("CK_User_Role", "\"Role\" IN ('student','advisor','admin')"));
        });
        model.Entity<CatalogEdition>().HasKey(x => x.Id);
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
