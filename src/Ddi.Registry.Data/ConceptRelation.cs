using System;
using System.ComponentModel.DataAnnotations;

namespace Ddi.Registry.Data
{
    public class ConceptRelation
    {
        public ConceptRelation()
        {
            Id = Guid.NewGuid();
            CreatedAt = DateTime.UtcNow;
        }

        [Key]
        public Guid Id { get; set; }

        [Required]
        public string SourceConceptIrdi { get; set; }

        public string TargetConceptIrdi { get; set; }
        public string TargetExternalIrdi { get; set; }
        public bool IsCrossAgency { get; set; }
        public string CreatedByUserId { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}