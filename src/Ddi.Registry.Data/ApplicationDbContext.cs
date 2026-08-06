using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ddi.Registry.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Agency> Agencies { get; set; }
        public DbSet<Assignment> Assignments { get; set; }
        public DbSet<ConceptRegistration> ConceptRegistrations { get; set; }
        public DbSet<ConceptRelation> ConceptRelations { get; set; }
        public DbSet<Delegation> Delegations { get; set; }
        public DbSet<ExportAction> ExportActions { get; set; }
        public DbSet<RepresentationRegistration> RepresentationRegistrations { get; set; }
        public DbSet<Service> Services { get; set; }
        public DbSet<VariableRegistration> VariableRegistrations { get; set; }
        public DbSet<HttpResolver> HttpResolvers { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ConceptRegistration>()
                .HasIndex(entity => entity.Irdi)
                .IsUnique();

            builder.Entity<ConceptRegistration>()
                .HasIndex(entity => new { entity.AgencyId, entity.Name, entity.Version })
                .IsUnique();

            builder.Entity<ConceptRegistration>()
                .HasIndex(entity => entity.AgencyId);

            builder.Entity<ConceptRegistration>()
                .HasIndex(entity => entity.ApprovalState);

            builder.Entity<ConceptRegistration>()
                .HasIndex(entity => entity.CreatedAt);

            builder.Entity<RepresentationRegistration>()
                .HasIndex(entity => entity.Irdi)
                .IsUnique();

            builder.Entity<RepresentationRegistration>()
                .HasIndex(entity => new { entity.AgencyId, entity.Name, entity.Version })
                .IsUnique();

            builder.Entity<RepresentationRegistration>()
                .HasIndex(entity => entity.AgencyId);

            builder.Entity<RepresentationRegistration>()
                .HasIndex(entity => entity.ApprovalState);

            builder.Entity<RepresentationRegistration>()
                .HasIndex(entity => entity.CreatedAt);

            builder.Entity<VariableRegistration>()
                .HasIndex(entity => entity.Irdi)
                .IsUnique();

            builder.Entity<VariableRegistration>()
                .HasIndex(entity => new { entity.AgencyId, entity.Name, entity.Version })
                .IsUnique();

            builder.Entity<VariableRegistration>()
                .HasIndex(entity => entity.AgencyId);

            builder.Entity<VariableRegistration>()
                .HasIndex(entity => entity.ApprovalState);

            builder.Entity<VariableRegistration>()
                .HasIndex(entity => entity.CreatedAt);

            builder.Entity<VariableRegistration>()
                .HasOne<ConceptRegistration>()
                .WithMany()
                .HasForeignKey(entity => entity.ConceptIrdi)
                .HasPrincipalKey(entity => entity.Irdi)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<VariableRegistration>()
                .HasOne<RepresentationRegistration>()
                .WithMany()
                .HasForeignKey(entity => entity.RepresentationIrdi)
                .HasPrincipalKey(entity => entity.Irdi)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
