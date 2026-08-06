using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Ddi.Registry.Data;

namespace Ddi.Registry.Web.Models
{
	public class DeleteAgencyRequestModel
	{
		public Agency Agency { get; set; }
		public string Reason { get; set; }

		public DeleteAgencyRequestModel()
		{

		}

		public DeleteAgencyRequestModel(Agency agency)
		{
			this.Agency = agency;
		}
	}

	public partial class ApproveModel
	{
		public IEnumerable<ConceptRegistration> RequestedConcepts { get; set; }
		public IEnumerable<RepresentationRegistration> RequestedRepresentations { get; set; }
		public IEnumerable<VariableRegistration> RequestedVariables { get; set; }
	}
}