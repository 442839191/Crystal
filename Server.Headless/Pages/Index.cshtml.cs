using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Server.Headless.Pages
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly WebAdminSettings _web;

        public IndexModel(WebAdminSettings web)
        {
            _web = web;
        }

        public string Username => _web.Username;
        public bool MustChangePassword => _web.MustChangePassword;

        public void OnGet()
        {
        }
    }
}
