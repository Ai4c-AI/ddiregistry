using System;
using System.ComponentModel.DataAnnotations;

namespace Ddi.Registry.Data
{
    public class RepresentationRegistration
    {
        public RepresentationRegistration()
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

        public string Type { get; set; }
        public string JsonSchema { get; set; }
        public string ShaclTemplateIrdi { get; set; }
        public ApprovalState ApprovalState { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}