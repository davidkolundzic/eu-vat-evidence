using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VatEvidence.Application.Evidence;

namespace VatEvidence.Web.Pages.Transactions
{
  [Authorize]
  public class VerifyModel(IEvidenceChainVerifier _verifier, ILogger<VerifyModel> _logger) : PageModel
  {
    public EvidenceChainVerifyResult Result { get; set; } = default!; // Initialized in OnGet method for demonstration purposes only 
    public async Task<IActionResult> OnGet(Guid id)
    {
      Result = await _verifier.VerifyAsync(id);
      return Page();
    }
  }
}
