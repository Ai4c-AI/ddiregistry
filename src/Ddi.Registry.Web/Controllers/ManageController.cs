using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Ddi.Registry.Data;
using Ddi.Registry.Web.Models;
using System.Net.Mail;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Identity;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Localization;

namespace Ddi.Registry.Web.Controllers
{
    public class ManageController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailSender _email;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IStringLocalizer<ManageController> _localizer;

        public ManageController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IEmailSender email, IStringLocalizer<ManageController> localizer)
        {
            _context = context;
            _email = email;
            _userManager = userManager;
            _roleManager = roleManager;
            _localizer = localizer;
        }

        #region Assignment
        [Authorize]
        public async Task<IActionResult> AddAssignment(string agencyId)
        {
            //RegistryProvider provider = new RegistryProvider();
            //string username = User.Identity.Name;
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!await _context.ManagesAgency(userId, agencyId))
            {
                return Forbid();
            }
            Agency agency = await _context.GetAgency(agencyId);
            AssignmentModel assignment = new AssignmentModel()
            {
                AgencyId = agencyId,
                AssignmentId = agencyId + "."
            };
            return View(assignment);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AddAssignment(AssignmentModel model)
        {
            //RegistryProvider provider = new RegistryProvider();
            //string username = User.Identity.Name;
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (ModelState.IsValid)
            {
                if (!await _context.ManagesAgency(userId, model.AgencyId))
                {
                    return Forbid();
                }
                Agency agency = await _context.GetAgency(model.AgencyId);

                string assignmentName = model.AssignmentId.ToLowerInvariant();
                if (!assignmentName.StartsWith(agency.AgencyId, StringComparison.InvariantCultureIgnoreCase))
                {
                    ModelState.AddModelError("", _localizer["AssignmentAgencyPrefixRequired"]);
                    model.AgencyId = agency.AgencyId;
                    model.AssignmentId = agency.AgencyId + ".";
                    return View(model);
                }
                
                if (await _context.GetAssignment(assignmentName) != null)
                {
                    ModelState.AddModelError("", _localizer["SubAgencyExists", assignmentName]);
                    model.AgencyId = agency.AgencyId;
                    model.AssignmentId = agency.AgencyId + ".";
                    return View(model);
                }

                Assignment assignment = new Assignment()
                {
                    AgencyId = model.AgencyId,
                    AssignmentId = assignmentName
                };
                await _context.Assignments.AddAsync(assignment);
                int result = await _context.SaveChangesAsync();


            }
            return RedirectToAction("ViewAgency", "Manage", new { agencyId = model.AgencyId });            
        }

        [Authorize]
        public async Task<IActionResult> EditAssignment(string assignmentId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            Assignment assignment = await _context.GetAssignment(assignmentId);
            if (assignment == null || !await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }

			Agency agency = await _context.GetAgency(assignment.AgencyId);
			if (agency == null)
			{
				return Forbid();
			}

            AssignmentModel model = new AssignmentModel()
            {
                AgencyId = assignment.AgencyId,
                AssignmentId = assignment.AssignmentId,
                Delegated = assignment.IsDelegated,
				IsDelegated = assignment.IsDelegated,
				Services = await _context.GetServicesForAssignment(assignment.AssignmentId),
				Delegations = await _context.GetDelegationsForAssignment(assignment.AssignmentId),
                HttpResolvers = await _context.GetHttpResolversForAssignment(assignment.AssignmentId)
            };
            return View(model);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> EditAssignment(AssignmentModel model)
        {
            //RegistryProvider provider = new RegistryProvider();
            //string username = User.Identity.Name;
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (ModelState.IsValid)
            {
                Assignment assignment = await _context.Assignments.FindAsync(model.AssignmentId);
                if (assignment == null || !await _context.ManagesAgency(userId, assignment.AgencyId))
                {
                    return Forbid();
                }
                Agency agency = await _context.GetAgency(assignment.AgencyId);

                string assignmentName = model.AssignmentId;
                if (assignmentName.Equals(agency.AgencyId, StringComparison.InvariantCultureIgnoreCase))
                {
                    //
                }
                else if (!assignmentName.StartsWith(agency.AgencyId + ".", StringComparison.InvariantCultureIgnoreCase))
                {
                    assignmentName = agency.AgencyId + "." + assignmentName;
                }

                assignment.AssignmentId = assignmentName.ToLowerInvariant();
                assignment.IsDelegated = model.Delegated;
                assignment.LastModified = DateTime.UtcNow;
                var updated = await _context.SaveChangesAsync();

				return RedirectToAction("EditAssignment", "Manage", new { assignmentId = assignment.AssignmentId });
            }
            return View(model);
        }

		[Authorize]
		public async Task<IActionResult> DeleteAssignment(string assignmentId)
		{
            //RegistryProvider provider = new RegistryProvider();
            //string username = User.Identity.Name;

            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            Assignment assignment = await _context.Assignments.FindAsync(assignmentId);
            if (assignment == null || !await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }
            Agency agency = await _context.GetAgency(assignment.AgencyId);

            // Don't allow deleting the top level assignment.
            if (assignment.AssignmentId == agency.AgencyId)
			{
				return RedirectToAction("ViewAgency", new { agencyId = agency.AgencyId });
			}

			return View(assignment);
		}

		[Authorize]
		[HttpPost]
		public async Task<IActionResult> DeleteAssignment(Assignment postAssignment)
		{
			//RegistryProvider provider = new RegistryProvider();
			//string username = User.Identity.Name;

            if (ModelState.IsValid)
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

                Assignment assignment = await _context.Assignments.FindAsync(postAssignment.AssignmentId);
                if (assignment == null || !await _context.ManagesAgency(userId, assignment.AgencyId))
                {
                    return Forbid();
                }
                Agency agency = await _context.GetAgency(assignment.AgencyId);

                // Don't allow deleting the top level assignment.
                if (assignment.AssignmentId == agency.AgencyId)
			    {
				    return RedirectToAction("ViewAgency", new { agencyId = agency.AgencyId });
			    }

			    _context.Assignments.Remove(assignment);
                await _context.SaveChangesAsync();

			    return RedirectToAction("ViewAgency", new { agencyId = agency.AgencyId });
            }
            return View(postAssignment);
		}


        #endregion

        #region Service
        [Authorize]
        public async Task<IActionResult> AddService(string assignmentId)
        {
            //RegistryProvider provider = new RegistryProvider();
            //string username = User.Identity.Name;
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
            Assignment assignment = await _context.Assignments.FindAsync(assignmentId);

            if (assignment == null || !await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }
            Service service = new Service()
            {
                AssignmentId = assignmentId
            };
            return View(service);
        }

        [Authorize]
        public async Task<IActionResult> DeleteService(string serviceId)
        {
            //RegistryProvider provider = new RegistryProvider();
            //string username = User.Identity.Name;
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
            Service service = await _context.Services.FindAsync(serviceId);
            if(service == null)
            {
                return NotFound();
            }
            Assignment assignment = await _context.Assignments.FindAsync(service.AssignmentId);
            if (assignment == null)
            {
                NotFound();
            }
            else if (!await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }

			return View(service);
        }

		[Authorize]
		[HttpPost]
		public async Task<IActionResult> DeleteService(Service postService)
		{
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
            Service service = await _context.Services.FindAsync(postService.ServiceId);
            if (service == null)
            {
                return NotFound();
            }
            Assignment assignment = await _context.Assignments.FindAsync(service.AssignmentId);
            if (assignment == null)
            {
                NotFound();
            }
            else if (!await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }

            _context.Services.Remove(service);
            await _context.SaveChangesAsync();

            await _context.RecordUpdate();

            return RedirectToAction("EditAssignment", "Manage", new { assignmentId = assignment.AssignmentId });
		}


        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AddService(Service postService)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (ModelState.IsValid)
            {
                Assignment assignment = await _context.Assignments.FindAsync(postService.AssignmentId);
                if (assignment == null)
                {
                    NotFound();
                }
                else if (!await _context.ManagesAgency(userId, assignment.AgencyId))
                {
                    return Forbid();
                }

                Service service = new Service();
                service.AssignmentId = postService.AssignmentId;
                service.ServiceName = postService.ServiceName;
                service.Hostname = postService.Hostname;
                service.Port = postService.Port;
                service.Protocol = postService.Protocol;
                service.TimeToLive = postService.TimeToLive;
                service.Priority = postService.Priority;
                service.Weight = postService.Weight;


                _context.Services.Add(service);
                await _context.SaveChangesAsync();

                await _context.RecordUpdate();

                return RedirectToAction("EditAssignment", "Manage", new { assignmentId = assignment.AssignmentId });
            }
            return View(postService);
        }

        [Authorize]
        public async Task<IActionResult> EditService(string serviceId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!await _context.ManagesService(userId, serviceId))
            {
                return Forbid();
            }
            Service service = await _context.Services.FindAsync(serviceId);
            return View(service);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditService(Service postService)
        {

            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (ModelState.IsValid)
            {
                if (!await _context.ManagesService(userId, postService.ServiceId))
                {
                    return Forbid();
                }
                var service = await _context.Services.FindAsync(postService.ServiceId);

                service.Hostname = postService.Hostname;
                service.Port = postService.Port;
                service.Priority = postService.Priority;
                service.Protocol = postService.Protocol;
                service.ServiceName = postService.ServiceName;
                service.TimeToLive = postService.TimeToLive;
                service.Weight = postService.Weight;

                await _context.SaveChangesAsync();

                await _context.RecordUpdate();

                Assignment assignment = await _context.Assignments.FindAsync(service.AssignmentId);
                return RedirectToAction("ViewAgency", "Manage", new { agencyId = assignment.AgencyId });
            }
            return View(postService);
        }



        #endregion


        #region HttpResolver
        [Authorize]
        public async Task<IActionResult> AddHttpResolver(string assignmentId)
        {
            //RegistryProvider provider = new RegistryProvider();
            //string username = User.Identity.Name;
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
            Assignment assignment = await _context.Assignments.FindAsync(assignmentId);

            if (assignment == null || !await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }
            HttpResolver resolver = new HttpResolver()
            {
                AssignmentId = assignmentId
            };
            return View(resolver);
        }

        [Authorize]
        public async Task<IActionResult> DeleteHttpResolver(string resolverId)
        {
            //RegistryProvider provider = new RegistryProvider();
            //string username = User.Identity.Name;
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
            HttpResolver resolver = await _context.HttpResolvers.FindAsync(resolverId);
            if (resolver == null)
            {
                return NotFound();
            }
            Assignment assignment = await _context.Assignments.FindAsync(resolver.AssignmentId);
            if (assignment == null)
            {
                NotFound();
            }
            else if (!await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }

            return View(resolver);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> DeleteHttpResolver(HttpResolver postHttpResolver)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
            HttpResolver resolver = await _context.HttpResolvers.FindAsync(postHttpResolver.Id);
            if (resolver == null)
            {
                return NotFound();
            }
            Assignment assignment = await _context.Assignments.FindAsync(resolver.AssignmentId);
            if (assignment == null)
            {
                NotFound();
            }
            else if (!await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }

            _context.HttpResolvers.Remove(resolver);
            await _context.SaveChangesAsync();


            return RedirectToAction("EditAssignment", "Manage", new { assignmentId = assignment.AssignmentId });
        }


        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AddHttpResolver(HttpResolver postHttpResolver)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (ModelState.IsValid)
            {
                Assignment assignment = await _context.Assignments.FindAsync(postHttpResolver.AssignmentId);
                if (assignment == null)
                {
                    NotFound();
                }
                else if (!await _context.ManagesAgency(userId, assignment.AgencyId))
                {
                    return Forbid();
                }

                // ensure the resolver does not already exist

                HttpResolver resolver = new HttpResolver();
                resolver.AssignmentId = postHttpResolver.AssignmentId;
                resolver.ResolutionType = postHttpResolver.ResolutionType;
                resolver.UrlTemplate = postHttpResolver.UrlTemplate;


                _context.HttpResolvers.Add(resolver);
                await _context.SaveChangesAsync();

                return RedirectToAction("EditAssignment", "Manage", new { assignmentId = assignment.AssignmentId });
            }
            return View(postHttpResolver);
        }

        [Authorize]
        public async Task<IActionResult> EditHttpResolver(string resolverId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!await _context.ManagesHttpResolver(userId, resolverId))
            {
                return Forbid();
            }
            HttpResolver service = await _context.HttpResolvers.FindAsync(resolverId);
            return View(service);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditHttpResolver(HttpResolver postHttpResolver)
        {

            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (ModelState.IsValid)
            {
                if (!await _context.ManagesHttpResolver(userId, postHttpResolver.Id))
                {
                    return Forbid();
                }
                var resolver = await _context.HttpResolvers.FindAsync(postHttpResolver.Id);

                resolver.AssignmentId = postHttpResolver.AssignmentId;
                resolver.ResolutionType = postHttpResolver.ResolutionType;
                resolver.UrlTemplate = postHttpResolver.UrlTemplate;

                await _context.SaveChangesAsync();

                Assignment assignment = await _context.Assignments.FindAsync(resolver.AssignmentId);
                return RedirectToAction("ViewAgency", "Manage", new { agencyId = assignment.AgencyId });
            }
            return View(postHttpResolver);
        }



        #endregion

        #region Delegation
        [Authorize]
        public async Task<IActionResult> AddDelegation(string assignmentId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            var assignment = await _context.Assignments.FindAsync(assignmentId); 
            if(assignment == null)
            {
                return Forbid();
            }
            if (!await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }
            Delegation delegation = new Delegation()
            {
                AssignmentId = assignmentId
            };
            return View(delegation);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AddDelegation(Delegation postDelegation)
        {
            if (ModelState.IsValid)
            {
                var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

                var assignment = await _context.Assignments.FindAsync(postDelegation.AssignmentId);
                if (assignment == null)
                {
                    return Forbid();
                }
                if (!await _context.ManagesAgency(userId, assignment.AgencyId))
                {
                    return Forbid();
                }

                Delegation delegation = new Delegation()
                {
                    AssignmentId = postDelegation.AssignmentId,
                    NameServer = postDelegation.NameServer
                };
                _context.Delegations.Add(delegation);
                await _context.SaveChangesAsync();

                return RedirectToAction("EditAssignment", "Manage", new { assignmentId = assignment.AssignmentId });
            }
            return View(postDelegation);
        }

        [Authorize]
        public async Task<IActionResult> EditDelegation(string delegationId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;


            Delegation delegation = await _context.Delegations.FindAsync(delegationId);
            if(delegation == null) { return Forbid(); }

            var assignment = await _context.Assignments.FindAsync(delegation.AssignmentId);
            if (assignment == null)
            {
                return Forbid();
            }
            if (!await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }

            return View(delegation);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditDelegation(Delegation postDelegation)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (ModelState.IsValid)
            {
                Delegation delegation = await _context.Delegations.FindAsync(postDelegation.DelegationId);
                if (delegation == null) { return Forbid(); }

                var assignment = await _context.Assignments.FindAsync(delegation.AssignmentId);
                if (assignment == null)
                {
                    return Forbid();
                }
                if (!await _context.ManagesAgency(userId, assignment.AgencyId))
                {
                    return Forbid();
                }

                delegation.NameServer = postDelegation.NameServer;
                await _context.SaveChangesAsync();

                return RedirectToAction("EditAssignment", "Manage", new { assignmentId = assignment.AssignmentId });
            }
            return View(postDelegation);
        }

		[Authorize]
		public async Task<IActionResult> DeleteDelegation(string delegationId)
		{
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            Delegation delegation = await _context.Delegations.FindAsync(delegationId);
            if (delegation == null) { return Forbid(); }

            var assignment = await _context.Assignments.FindAsync(delegation.AssignmentId);
            if (assignment == null)
            {
                return Forbid();
            }
            if (!await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }

			return View(delegation);
		}


        [Authorize]
		[HttpPost]
        public async Task<IActionResult> DeleteDelegation(Delegation postDelegation)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            Delegation delegation = await _context.Delegations.FindAsync(postDelegation.DelegationId);
            if (delegation == null) { return Forbid(); }

            var assignment = await _context.Assignments.FindAsync(delegation.AssignmentId);
            if (assignment == null)
            {
                return Forbid();
            }
            if (!await _context.ManagesAgency(userId, assignment.AgencyId))
            {
                return Forbid();
            }

            _context.Delegations.Remove(delegation);
            await _context.SaveChangesAsync();

			return RedirectToAction("EditAssignment", "Manage", new { assignmentId = assignment.AssignmentId });
        }

        #endregion

        #region Agency
        [Authorize]
        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            OverviewModel model = new OverviewModel();
            model.Agencies = await _context.GetAgenciesForUser(userId);
            model.People = await _context.GetPeopleForUser(userId);

            return View(model);
        }

        [Authorize]
        public async Task<IActionResult> AddAgency()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            ViewBag.People = await _context.GetPeopleForUser(userId);

            AgencyModel agencyModel = new AgencyModel()
            {
                AdminContactId = userId,
                TechnicalContactId = userId
            };
            return View(agencyModel);
        }

        [Authorize]
        public async Task<IActionResult> AddConceptRegistration(string agencyId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!await _context.ManagesAgency(userId, agencyId))
            {
                return Forbid();
            }

            var model = new ConceptRegistrationModel()
            {
                AgencyId = agencyId,
                Version = "1.0"
            };

            return View(model);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AddConceptRegistration(ConceptRegistrationModel model)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!await _context.ManagesAgency(userId, model.AgencyId))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var irdi = RegistryIrdi.BuildConceptIrdi(model.AgencyId, model.Name, model.Version);
            var existing = await _context.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == irdi);
            if (existing != null)
            {
                ModelState.AddModelError("", _localizer["ConceptExists"]);
                return View(model);
            }

            var concept = new ConceptRegistration
            {
                Irdi = irdi,
                AgencyId = model.AgencyId,
                Name = model.Name,
                Version = model.Version,
                Label = model.Label,
                ApprovalState = ApprovalState.Requested
            };

            _context.ConceptRegistrations.Add(concept);
            await _context.SaveChangesAsync();

            return RedirectToAction("ViewAgency", new { agencyId = model.AgencyId });
        }

        [Authorize]
        public async Task<IActionResult> EditConceptRegistration(string irdi)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            var concept = await _context.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == irdi);
            if (concept == null || !await _context.ManagesAgency(userId, concept.AgencyId))
            {
                return Forbid();
            }

            var model = new ConceptRegistrationModel
            {
                AgencyId = concept.AgencyId,
                Name = concept.Name,
                Version = concept.Version,
                Label = concept.Label
            };

            return View(model);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> EditConceptRegistration(ConceptRegistrationModel model)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            var irdi = RegistryIrdi.BuildConceptIrdi(model.AgencyId, model.Name, model.Version);
            var concept = await _context.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == irdi);
            if (concept == null || !await _context.ManagesAgency(userId, model.AgencyId))
            {
                return Forbid();
            }

            if (concept.ApprovalState != ApprovalState.Requested)
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            concept.Label = model.Label;
            concept.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return RedirectToAction("ViewAgency", new { agencyId = model.AgencyId });
        }

        [Authorize]
        public async Task<IActionResult> AddRepresentationRegistration(string agencyId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!await _context.ManagesAgency(userId, agencyId))
            {
                return Forbid();
            }

            var model = new RepresentationRegistrationModel()
            {
                AgencyId = agencyId,
                Version = "1.0",
                Type = "Code",
                JsonSchema = "{\"type\":\"string\"}"
            };

            return View(model);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AddRepresentationRegistration(RepresentationRegistrationModel model)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!await _context.ManagesAgency(userId, model.AgencyId))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var irdi = RegistryIrdi.BuildRepresentationIrdi(model.AgencyId, model.Name, model.Version);
            var existing = await _context.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == irdi);
            if (existing != null)
            {
                ModelState.AddModelError("", _localizer["RepresentationExists"]);
                return View(model);
            }

            var representation = new RepresentationRegistration
            {
                Irdi = irdi,
                AgencyId = model.AgencyId,
                Name = model.Name,
                Version = model.Version,
                Type = model.Type,
                JsonSchema = model.JsonSchema,
                ShaclTemplateIrdi = model.ShaclTemplateIrdi,
                ApprovalState = ApprovalState.Requested
            };

            _context.RepresentationRegistrations.Add(representation);
            await _context.SaveChangesAsync();

            return RedirectToAction("ViewAgency", new { agencyId = model.AgencyId });
        }

        [Authorize]
        public async Task<IActionResult> AddVariableRegistration(string agencyId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!await _context.ManagesAgency(userId, agencyId))
            {
                return Forbid();
            }

            var model = new VariableRegistrationModel()
            {
                AgencyId = agencyId,
                Version = "1.0"
            };

            return View(model);
        }

        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AddVariableRegistration(VariableRegistrationModel model)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!await _context.ManagesAgency(userId, model.AgencyId))
            {
                return Forbid();
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var concept = await _context.ConceptRegistrations.FirstOrDefaultAsync(c => c.Irdi == model.ConceptIrdi);
            var representation = await _context.RepresentationRegistrations.FirstOrDefaultAsync(r => r.Irdi == model.RepresentationIrdi);
            if (concept == null || representation == null)
            {
                ModelState.AddModelError("", _localizer["VariableReferenceNotFound"]);
                return View(model);
            }

            var validation = RegistrationValidation.ValidateVariableReferences(
                model.AgencyId,
                concept.AgencyId,
                representation.AgencyId,
                allowCrossAgency: false);
            if (!validation.IsValid)
            {
                ModelState.AddModelError("", _localizer[validation.ErrorCode]);
                return View(model);
            }

            var irdi = RegistryIrdi.BuildVariableIrdi(model.AgencyId, model.Name, model.Version);
            var existing = await _context.VariableRegistrations.FirstOrDefaultAsync(v => v.Irdi == irdi);
            if (existing != null)
            {
                ModelState.AddModelError("", _localizer["VariableExists"]);
                return View(model);
            }

            var variable = new VariableRegistration
            {
                Irdi = irdi,
                AgencyId = model.AgencyId,
                Name = model.Name,
                Version = model.Version,
                ConceptIrdi = model.ConceptIrdi,
                RepresentationIrdi = model.RepresentationIrdi,
                CollectionMethod = model.CollectionMethod,
                ApprovalState = ApprovalState.Requested
            };

            _context.VariableRegistrations.Add(variable);
            await _context.SaveChangesAsync();

            return RedirectToAction("ViewAgency", new { agencyId = model.AgencyId });
        }

        [Authorize]
        public async Task<IActionResult> EditAgency(string agencyId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            if (!await _context.ManagesAgency(userId, agencyId))
            {
                return Forbid();
            }
            Agency agency = await _context.GetAgency(agencyId);
            AgencyModel model = new AgencyModel()
            {
                AdminContactId = agency.AdminContactId,
                TechnicalContactId = agency.TechnicalContactId,
                CreatorId = agency.CreatorId,
                AgencyId = agency.AgencyId,
                Label = agency.Label
            };
            ViewBag.People = await _context.GetPeopleForUser(userId);
            return View(model);
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditAgency(AgencyModel model)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            Agency agency = await _context.GetAgency(model.AgencyId);
            if(agency == null)
            {
                return Forbid();
            }
            if (!await _context.ManagesAgency(userId, agency.AgencyId))
            {
                return Forbid();
            }

            agency.Label = model.Label;

            agency.AdminContactId = await SelectOrInviteUser(model.AdminContactId, model.AdminContactEmail, model.AgencyId);
            agency.TechnicalContactId = await SelectOrInviteUser(model.TechnicalContactId, model.TechnicalContactEmail, model.AgencyId);
                     
            agency.LastModified = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return RedirectToAction("Index", "Manage");
        }

        private async Task<string> SelectOrInviteUser(string userId, string email, string agencyId)
        {
            ApplicationUser user;
            if (!string.IsNullOrWhiteSpace(userId))
            {
                user = await _context.Users.FindAsync(userId);
                if(user != null)
                {
                    return userId;
                }
                return null;
            }

            if(string.IsNullOrWhiteSpace(email)) 
            {
                return null;
            }

            user = await _context.Users.Where(x => x.NormalizedEmail == email.ToUpperInvariant()).FirstOrDefaultAsync();
            if(user == null)
            { 
                var inviterId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
                ApplicationUser inviter = await _context.Users.FindAsync(inviterId);


                user = new ApplicationUser()
                {
                    UserName = email,
                    Email = email
                };

                var result = await _userManager.CreateAsync(user);
                if (result.Succeeded)
                {
                    var code = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                    var callbackUrl = Url.Page(
                        "/Account/ConfirmEmail",
                        pageHandler: null,
                        values: new { area = "Identity", userId = user.Id, code = code },
                        protocol: Request.Scheme);

                    var subject = _localizer["InviteEmailSubject"];
                    var body = string.Format(_localizer["InviteEmailBody"],
                        inviter.Name, inviter.Email, agencyId, HtmlEncoder.Default.Encode(callbackUrl));
                    await _email.SendEmailAsync(email, subject, body);

                    return user.Id;
                }
            }
            else
            {
                return user.Id;
            }
            return null;
        }


        [Authorize]
        [HttpPost]
        public async Task<IActionResult> AddAgency(AgencyModel addAgencyModel)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            // allow two digit codes, int, and uk
            if (addAgencyModel != null)
            {
                var validation = AgencyIdValidator.Validate(addAgencyModel.AgencyId, addAgencyModel.Label);
                if (!validation.Ok)
                {
                    var countryCode = addAgencyModel.AgencyId?.Split('.')[0] ?? string.Empty;
                    ModelState.AddModelError("",
                        validation.ErrorCode == "CountryCodeInvalid"
                            ? _localizer[validation.ErrorCode, countryCode]
                            : _localizer[validation.ErrorCode]);
                }
            }

            if (ModelState.IsValid)
            {
                Agency agency = await _context.GetAgency(addAgencyModel.AgencyId);
                if (agency != null)
                {
                    ModelState.AddModelError("", _localizer["AgencyIdExists"]);
                }
                else
                {
                    agency = new Agency() 
                    { 
                        AgencyId = addAgencyModel.AgencyId,
                        ApprovalState = ApprovalState.Requested,
                        Label = addAgencyModel.Label,
                        CreatorId = userId,
                        AdminContactId = userId,
                        TechnicalContactId = userId
                    };
                    agency.AdminContactId = await SelectOrInviteUser(addAgencyModel.AdminContactId, addAgencyModel.AdminContactEmail, addAgencyModel.AgencyId);
                    agency.TechnicalContactId = await SelectOrInviteUser(addAgencyModel.TechnicalContactId, addAgencyModel.TechnicalContactEmail, addAgencyModel.AgencyId);

                    _context.Agencies.Add(agency);
                    await _context.SaveChangesAsync();


                    // Send email.
                    var user = await _context.Users.FindAsync(userId);
                    if(user != null)
                    {
                        try
                        {
                            await SendConfirmationEmail(user, addAgencyModel.AgencyId);
                        }
                        catch(Exception e)
                        {
                            
                        }
                        
                    }

                    var approvers = await _userManager.GetUsersInRoleAsync("admin");
                    foreach(var approver in approvers)
                    {
                        try
                        {
                            await SendApproverEmail(approver, user, addAgencyModel.AgencyId);
                        }
                        catch (Exception e)
                        {

                        }
                        
                    }

                    return RedirectToAction("Index", "Manage");
                }
            }

            ViewBag.People = await _context.GetPeopleForUser(userId);
            return View();
        }

        private async Task SendApproverEmail(ApplicationUser approver, ApplicationUser user, string agencyName)
        {
            var bodyHtml = string.Format(_localizer["ApproverEmailBody"],
                user.Name, user.Email, agencyName);
            var subject = string.Format(_localizer["ApproverEmailSubject"], agencyName);

            await _email.SendEmailAsync(approver.Email, subject, bodyHtml);
        }

        private async Task SendConfirmationEmail(ApplicationUser user, string agencyName)
		{
            var bodyHtml = string.Format(_localizer["ConfirmationEmailBody"], agencyName);
            var subject = string.Format(_localizer["ConfirmationEmailSubject"], agencyName);

            await _email.SendEmailAsync(user.Email, subject, bodyHtml);
		}

        [Authorize]
        public async Task<IActionResult> ViewAgency(string agencyId)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;            

            if (!await _context.ManagesAgency(userId, agencyId))
            {
                return Forbid();
            }

            var agency = await _context.Agencies
                .Include(i => i.AdminContact)
                .Include(i => i.TechnicalContact)
                .Include(i => i.Creator)
                .Include(i => i.Assignments)
                    .ThenInclude(i => i.Services)
                .Include(i => i.Assignments)
                    .ThenInclude(i => i.Delegations)
                .Include(i => i.Assignments)
                    .ThenInclude(i => i.HttpResolvers)
                .FirstOrDefaultAsync(x => x.AgencyId == agencyId);
            
            AgencyOverviewModel model = new AgencyOverviewModel();
            model.Agency = agency;
            model.AdminContact = agency.AdminContact;
            model.TechnicalContact = agency.TechnicalContact;
            model.Assignments = agency.Assignments;
            model.Concepts = await _context.ConceptRegistrations
                .Where(x => x.AgencyId == agencyId)
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Version)
                .ToListAsync();
            model.Representations = await _context.RepresentationRegistrations
                .Where(x => x.AgencyId == agencyId)
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Version)
                .ToListAsync();
            model.Variables = await _context.VariableRegistrations
                .Where(x => x.AgencyId == agencyId)
                .OrderBy(x => x.Name)
                .ThenBy(x => x.Version)
                .ToListAsync();

            foreach (Assignment a in model.Assignments)
            {
                model.Services[a.AssignmentId] = a.Services;
                model.Delegations[a.AssignmentId] = a.Delegations;
                model.HttpResolvers[a.AssignmentId] = a.HttpResolvers;
            }

            return View(model);
        }
        #endregion

        #region Person

        [Authorize]
        public async Task<IActionResult> ViewPerson(string personId)
        {
            ApplicationUser person = await _context.Users.FindAsync(personId);
            if (person == null)
            {
                return NotFound();
            }
            return View(person);
        }

        #endregion

    }
}
