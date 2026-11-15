using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Server.Headless.Pages
{
    public class LoginModel : PageModel
    {
        private readonly WebAdminSettings _web;

        public LoginModel(WebAdminSettings web)
        {
            _web = web;
        }

        [BindProperty]
        public string Username { get; set; } = string.Empty;

        [BindProperty]
        public string Password { get; set; } = string.Empty;

        public string Error { get; set; }

        public void OnGet()
        {
        }

        public async Task<IActionResult> OnPostAsync()
        {
            var key = $"{Username}|{HttpContext.Connection.RemoteIpAddress}";
            if (LoginGuard.IsBlocked(key, out var seconds))
            {
                Error = $"登录尝试过多，请 {seconds} 秒后再试。";
                return Page();
            }

            if (!_web.Verify(Username, Password))
            {
                LoginGuard.Fail(key);
                Error = "账号或密码错误。";
                return Page();
            }

            LoginGuard.Reset(key);

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, _web.Username),
                new Claim(ClaimTypes.Role, "Admin")
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)));

            return Redirect("/");
        }
    }
}
