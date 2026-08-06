using System;
using System.ComponentModel.DataAnnotations;

namespace Ddi.Registry.Data
{
    public class VariableRegistration
    {
        public VariableRegistration()
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTime.UtcNow;
            ApprovalState = ApprovalState.None;
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        public string Irdi { get; set; }

        [Required]
        public string AgencyId { get; set; }

        [Required]
        public string Name { get; set; }

        [Required]
        public string Version { get; set; }

        [Required]
        public string ConceptIrdi { get; set; }

        [Required]
        public string RepresentationIrdi { get; set; }

        public string SourceType { get; set; }
        public string CollectionMethod { get; set; }
        public string Universe { get; set; }
        public string QualityGate { get; set; }
        public ApprovalState ApprovalState { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}