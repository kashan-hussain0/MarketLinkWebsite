using System.ComponentModel.DataAnnotations;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Models.Enums;
using MarketLinkWebsite.Security;
using MarketLinkWebsite.Services;
using MarketLinkWebsite.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketLinkWebsite.Controllers
{
    public sealed class AccountController : Controller
    {
        private const string CustomerRole = "Customer";
        private const string FarmerRole = "Farmer";
        private const string AdminRole = "Admin";
        private const string ResetPasswordPurpose = "ResetPassword";
        private const string CodeCookieName = ".MarketLink.ResetCode";
        private const string TokenProviderName = "Default";
        private readonly ApplicationDbContext db;
        private readonly UserManager<ApplicationUser> userManager;
        private readonly SignInManager<ApplicationUser> signInManager;
        private readonly RoleManager<IdentityRole> roleManager;
        private readonly IEmailSender emailSender;
        private readonly PasswordResetCodeService passwordResetCodes;
        private readonly IConfiguration configuration;
        private readonly IWebHostEnvironment environment;

        public AccountController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            RoleManager<IdentityRole> roleManager,
            IEmailSender emailSender,
            PasswordResetCodeService passwordResetCodes,
            IConfiguration configuration,
            IWebHostEnvironment environment)
        {
            this.db = db;
            this.userManager = userManager;
            this.signInManager = signInManager;
            this.roleManager = roleManager;
            this.emailSender = emailSender;
            this.passwordResetCodes = passwordResetCodes;
            this.configuration = configuration;
            this.environment = environment;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["Title"] = "Log in";
            return View(new LoginForm { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginForm model)
        {
            ViewData["Title"] = "Log in";

            if (!ModelState.IsValid)
            {
                ViewData["ErrorMessage"] = "Check the highlighted fields and try again.";
                return View(model);
            }

            var email = model.Email.Trim();
            var user = await userManager.FindByEmailAsync(email);

            if (user is null)
            {
                ModelState.AddModelError(string.Empty, "The email address or password is incorrect.");
                ViewData["ErrorMessage"] = "The email address or password is incorrect.";
                return View(model);
            }

            if (!user.IsActive)
            {
                await signInManager.SignOutAsync();
                ModelState.AddModelError(string.Empty, "This account is inactive. Contact MarketLink support for help.");
                ViewData["ErrorMessage"] = "This account is inactive. Contact MarketLink support for help.";
                return View(model);
            }

            var result = await signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);

            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "Too many unsuccessful attempts. Try again in a few minutes.");
                ViewData["ErrorMessage"] = "Too many unsuccessful attempts. Try again in a few minutes.";
                return View(model);
            }

            if (result.RequiresTwoFactor)
            {
                await signInManager.SignOutAsync();
                ModelState.AddModelError(string.Empty, "Two-factor sign-in is not available for this account yet.");
                ViewData["ErrorMessage"] = "Two-factor sign-in is not available for this account yet.";
                return View(model);
            }

            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "The email address or password is incorrect.");
                ViewData["ErrorMessage"] = "The email address or password is incorrect.";
                return View(model);
            }

            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync(HttpContext.RequestAborted);
            await StampCurrentSessionAsync(user, isPersistent: model.RememberMe);

            if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return LocalRedirect(model.ReturnUrl);
            }

            var roles = await userManager.GetRolesAsync(user);
            if (roles.Contains(AdminRole, StringComparer.OrdinalIgnoreCase))
            {
                return RedirectToAction("Index", "Dashboard", new { area = "Admin" });
            }

            if (roles.Contains(CustomerRole, StringComparer.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(CustomerController.Dashboard), "Customer");
            }

            if (roles.Contains(FarmerRole, StringComparer.OrdinalIgnoreCase))
            {
                var farmerStatus = await db.FarmerProfiles
                    .Where(profile => profile.UserId == user.Id)
                    .Select(profile => (FarmerStatus?)profile.Status)
                    .FirstOrDefaultAsync(HttpContext.RequestAborted);

                if (farmerStatus == FarmerStatus.Active)
                {
                    return RedirectToAction("Index", "Dashboard", new { area = "Farmer" });
                }

                if (farmerStatus is null or FarmerStatus.Suspended or FarmerStatus.Rejected)
                {
                    await signInManager.SignOutAsync();
                    TempData["ErrorMessage"] = "Your farmer account does not currently have marketplace access.";
                    return RedirectToAction(nameof(Login));
                }

                return RedirectToAction(nameof(PendingApproval));
            }

            await signInManager.SignOutAsync();
            TempData["ErrorMessage"] = "This account is not assigned to a marketplace role.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await signInManager.SignOutAsync();
            HttpContext.Session.Clear();
            Response.Cookies.Delete(".MarketLink.Cart", new CookieOptions { Path = "/" });
            TempData["SuccessMessage"] = "You have been safely logged out.";
            return RedirectToAction(nameof(Login));
        }

        [HttpGet]
        public IActionResult Register()
        {
            ViewData["Title"] = "Create your account";
            return View(new CustomerRegistrationForm());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(CustomerRegistrationForm model)
        {
            ViewData["Title"] = "Create your account";

            if (!model.AcceptTerms)
            {
                ModelState.AddModelError(nameof(model.AcceptTerms), "Accept the terms to create your account.");
            }

            if (!ModelState.IsValid)
            {
                ViewData["ErrorMessage"] = "Please complete the required account and address fields.";
                return View(model);
            }

            var email = model.Email.Trim();
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                ModelState.AddModelError(nameof(model.Email), "An account already exists for this email address.");
                ViewData["ErrorMessage"] = "An account already exists for this email address.";
                return View(model);
            }

            var roleResult = await EnsureRoleExistsAsync(CustomerRole);
            if (!roleResult.Succeeded)
            {
                AddIdentityErrors(roleResult);
                ViewData["ErrorMessage"] = "Customer registration is temporarily unavailable.";
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = model.FirstName.Trim(),
                LastName = model.LastName.Trim(),
                PhoneNumber = model.PhoneNumber.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Latitude = model.Latitude,
                Longitude = model.Longitude,
            };

            await using var transaction = await db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
            var createResult = await userManager.CreateAsync(user, model.Password);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(HttpContext.RequestAborted);
                db.ChangeTracker.Clear();
                AddIdentityErrors(createResult);
                ViewData["ErrorMessage"] = "We could not create your account. Check your details and try again.";
                return View(model);
            }

            var roleAssignment = await userManager.AddToRoleAsync(user, CustomerRole);
            if (!roleAssignment.Succeeded)
            {
                await transaction.RollbackAsync(HttpContext.RequestAborted);
                db.ChangeTracker.Clear();
                AddIdentityErrors(roleAssignment);
                ViewData["ErrorMessage"] = "We could not finish creating your account.";
                return View(model);
            }

            db.Addresses.Add(new Address
            {
                UserId = user.Id,
                Label = "Primary",
                RecipientName = $"{user.FirstName} {user.LastName}",
                Phone = user.PhoneNumber ?? string.Empty,
                AddressLine = model.AddressLine.Trim(),
                City = model.City.Trim(),
                Region = model.Region.Trim(),
                PostalCode = model.PostalCode.Trim(),
                IsDefault = true,
                CreatedAt = DateTime.UtcNow
            });

            db.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Type = NotificationType.Account,
                Title = "Welcome to MarketLink",
                Message = "Your customer account is ready. Explore the harvest and save favorites for your next market visit.",
                ActionUrl = "/Product/Index",
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(HttpContext.RequestAborted);
            await transaction.CommitAsync(HttpContext.RequestAborted);
            await signInManager.SignInAsync(user, isPersistent: false);
            await StampCurrentSessionAsync(user, isPersistent: false);
            TempData["SuccessMessage"] = "Your MarketLink account and first address are ready. Welcome to the neighborhood!";
            return RedirectToAction(nameof(CustomerController.Dashboard), "Customer");
        }

        [HttpGet]
        public IActionResult RegisterChoice()
        {
            ViewData["Title"] = "Join MarketLink";
            return View();
        }

        [HttpGet]
        public IActionResult FarmerRegister()
        {
            ViewData["Title"] = "Join as a farmer";
            return View(new FarmerRegistrationForm());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FarmerRegister(FarmerRegistrationForm model)
        {
            ViewData["Title"] = "Join as a farmer";

            if (!model.AcceptTerms)
            {
                ModelState.AddModelError(nameof(model.AcceptTerms), "Accept the seller terms to continue.");
            }

            if (!ModelState.IsValid)
            {
                ViewData["ErrorMessage"] = "Please complete the required account and farm details.";
                return View(model);
            }

            var email = model.Email.Trim();
            if (await userManager.FindByEmailAsync(email) is not null)
            {
                ModelState.AddModelError(nameof(model.Email), "An account already exists for this email address.");
                ViewData["ErrorMessage"] = "An account already exists for this email address.";
                return View(model);
            }

            var fullAddress = BuildFullAddress(model.Address, model.City, model.Region, model.PostalCode);
            if (fullAddress.Length > 250)
            {
                ModelState.AddModelError(nameof(model.Address), "The complete farm address must be 250 characters or fewer.");
                ViewData["ErrorMessage"] = "Please shorten the complete farm address.";
                return View(model);
            }

            var roleResult = await EnsureRoleExistsAsync(FarmerRole);
            if (!roleResult.Succeeded)
            {
                AddIdentityErrors(roleResult);
                ViewData["ErrorMessage"] = "Farmer registration is temporarily unavailable.";
                return View(model);
            }

            string? savedPicturePath = null;
            try
            {
                savedPicturePath = await SaveProfilePictureAsync(model.ProfilePicture, HttpContext.RequestAborted);
            }
            catch (InvalidOperationException exception)
            {
                ModelState.AddModelError(nameof(model.ProfilePicture), exception.Message);
                ViewData["ErrorMessage"] = exception.Message;
                return View(model);
            }
            catch (IOException)
            {
                ModelState.AddModelError(nameof(model.ProfilePicture), "The profile photo could not be saved. Try again without a photo.");
                ViewData["ErrorMessage"] = "The profile photo could not be saved. You can retry without it.";
                return View(model);
            }
            catch (UnauthorizedAccessException)
            {
                ModelState.AddModelError(nameof(model.ProfilePicture), "The profile photo could not be saved. You can continue without it.");
                ViewData["ErrorMessage"] = "The profile photo could not be saved. You can continue without it.";
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = model.FirstName.Trim(),
                LastName = model.LastName.Trim(),
                PhoneNumber = model.PhoneNumber.Trim(),
                ProfilePictureUrl = savedPicturePath,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                Latitude = model.Latitude,
                Longitude = model.Longitude,
            };

            await using var transaction = await db.Database.BeginTransactionAsync(HttpContext.RequestAborted);
            var createResult = await userManager.CreateAsync(user, model.Password);
            if (!createResult.Succeeded)
            {
                await transaction.RollbackAsync(HttpContext.RequestAborted);
                db.ChangeTracker.Clear();
                DeleteProfilePicture(savedPicturePath);
                AddIdentityErrors(createResult);
                ViewData["ErrorMessage"] = "We could not create your farmer account. Check your details and try again.";
                return View(model);
            }

            var roleAssignment = await userManager.AddToRoleAsync(user, FarmerRole);
            if (!roleAssignment.Succeeded)
            {
                await transaction.RollbackAsync(HttpContext.RequestAborted);
                db.ChangeTracker.Clear();
                DeleteProfilePicture(savedPicturePath);
                AddIdentityErrors(roleAssignment);
                ViewData["ErrorMessage"] = "We could not finish creating your farmer account.";
                return View(model);
            }

            db.FarmerProfiles.Add(new FarmerProfile
            {
                UserId = user.Id,
                FarmName = model.FarmName.Trim(),
                Description = model.Description.Trim(),
                Address = fullAddress,
                City = model.City.Trim(),
                Latitude = model.Latitude ?? 0m,
                Longitude = model.Longitude ?? 0m,
                Status = FarmerStatus.PendingApproval,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            db.Notifications.Add(new Notification
            {
                UserId = user.Id,
                Type = NotificationType.Account,
                Title = "Farmer application received",
                Message = "Your farm profile is pending review. Marketplace access begins after approval.",
                ActionUrl = "/Account/PendingApproval",
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(HttpContext.RequestAborted);
            await transaction.CommitAsync(HttpContext.RequestAborted);
            await signInManager.SignInAsync(user, isPersistent: false);
            await StampCurrentSessionAsync(user, isPersistent: false);
            return RedirectToAction(nameof(PendingApproval));
        }

        [HttpGet]
        public IActionResult ForgotPassword()
        {
            ViewData["Title"] = "Reset your password";
            return View(new ForgotPasswordForm());
        }

        /// <summary>
        /// Step one of the reset. A six digit code is mailed out and the visitor is
        /// moved straight on to the code screen. The wording is deliberately the same
        /// whether or not the address belongs to an account, so the form cannot be
        /// used to find out who has registered.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordForm model)
        {
            ViewData["Title"] = "Reset your password";

            if (!ModelState.IsValid)
            {
                ViewData["ErrorMessage"] = "Enter a valid email address so we can send you a code.";
                return View(model);
            }

            var email = model.Email.Trim();
            var user = await userManager.FindByEmailAsync(email);

            if (user is not null && user.IsActive)
            {
                await passwordResetCodes.SendAsync(user, ResolveClientIp(), HttpContext.RequestAborted);
            }

            TempData["SuccessMessage"] = "If an active account matches that address, a verification code is on its way.";
            return RedirectToAction(nameof(VerifyCode), new { email });
        }

        [HttpGet]
        public async Task<IActionResult> VerifyCode(string? email = null)
        {
            ViewData["Title"] = "Enter your code";

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction(nameof(ForgotPassword));
            }

            var user = await userManager.FindByEmailAsync(email);
            if (user is null || !user.IsActive)
            {
                return RedirectToAction(nameof(ForgotPassword));
            }

            ViewData["EmailAddress"] = email;
            return View(new VerifyCodeForm { Email = email });
        }

        /// <summary>
        /// Step two. The code is checked on its own before the shopper is trusted with
        /// a new-password form, so a mistyped code is caught before anything is
        /// written to the account.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyCode(VerifyCodeForm model)
        {
            ViewData["Title"] = "Enter your code";

            if (!ModelState.IsValid)
            {
                ViewData["EmailAddress"] = model.Email;
                return View(model);
            }

            var user = await userManager.FindByEmailAsync(model.Email.Trim());
            if (user is null || !user.IsActive)
            {
                TempData["ErrorMessage"] = "That account is not active. Contact support if you need help.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var outcome = await passwordResetCodes.CheckAsync(user.Id, model.Code, HttpContext.RequestAborted);
            if (outcome != ResetCodeOutcome.Accepted)
            {
                ViewData["ErrorMessage"] = CodeErrorMessage(outcome);
                ViewData["EmailAddress"] = model.Email;
                model.Code = string.Empty;
                return View(model);
            }

            // The code is carried in a short lived cookie rather than the query
            // string, so it never lands in a browser history or a server log.
            Response.Cookies.Append(CodeCookieName, model.Code.Trim(), BuildCodeCookieOptions());

            return RedirectToAction(nameof(ResetPassword), new { email = model.Email.Trim() });
        }

        [HttpGet]
        public async Task<IActionResult> ResetPassword(string? email = null)
        {
            ViewData["Title"] = "Choose a new password";

            if (string.IsNullOrWhiteSpace(email))
            {
                return RedirectToAction(nameof(ForgotPassword));
            }

            var user = await userManager.FindByEmailAsync(email);
            if (user is null || !user.IsActive)
            {
                return RedirectToAction(nameof(ForgotPassword));
            }

            // The code has to have passed the check screen first.
            if (!Request.Cookies.TryGetValue(CodeCookieName, out var code) || string.IsNullOrWhiteSpace(code))
            {
                TempData["ErrorMessage"] = "Enter your verification code before choosing a new password.";
                return RedirectToAction(nameof(VerifyCode), new { email });
            }

            ViewData["EmailAddress"] = email;
            return View(new ResetPasswordForm { Email = email, Code = code });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordForm model)
        {
            ViewData["Title"] = "Choose a new password";

            if (!ModelState.IsValid)
            {
                ViewData["EmailAddress"] = model.Email;
                return View(model);
            }

            var user = await userManager.FindByEmailAsync(model.Email.Trim());
            if (user is null || !user.IsActive)
            {
                TempData["ErrorMessage"] = "That account is not active. Contact support if you need help.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            var outcome = await passwordResetCodes.CheckAsync(user.Id, model.Code, HttpContext.RequestAborted);
            if (outcome != ResetCodeOutcome.Accepted)
            {
                Response.Cookies.Delete(CodeCookieName);
                ViewData["EmailAddress"] = model.Email;
                ModelState.AddModelError(string.Empty, CodeErrorMessage(outcome));
                ViewData["ErrorMessage"] = CodeErrorMessage(outcome);
                model.Code = string.Empty;
                return View(model);
            }

            // The token is generated here and never leaves the server, so there is
            // nothing for an attacker to intercept between the check and the reset.
            var token = await userManager.GeneratePasswordResetTokenAsync(user);
            var result = await userManager.ResetPasswordAsync(user, token, model.Password);

            if (!result.Succeeded)
            {
                AddIdentityErrors(result);
                ViewData["EmailAddress"] = model.Email;
                ViewData["ErrorMessage"] = "The new password does not meet all account security requirements.";
                return View(model);
            }

            await passwordResetCodes.ConsumeAsync(user.Id, HttpContext.RequestAborted);

            // Every existing session is dropped, so a stolen sign-in does not
            // survive a password reset.
            foreach (var login in await userManager.GetLoginsAsync(user))
            {
                await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
            }

            await userManager.UpdateSecurityStampAsync(user);
            await signInManager.SignOutAsync();
            Response.Cookies.Delete(CodeCookieName);

            TempData["SuccessMessage"] = "Your password has been changed. Log in with your new password.";
            return RedirectToAction(nameof(Login));
        }

        private string CodeErrorMessage(ResetCodeOutcome outcome) => outcome switch
        {
            ResetCodeOutcome.Expired => "That code has expired. Ask for a new one and use it straight away.",
            ResetCodeOutcome.AlreadyUsed => "That code has already been used. Ask for a new one.",
            ResetCodeOutcome.TooManyAttempts => "Too many wrong attempts. Ask for a new code and try again.",
            ResetCodeOutcome.NoCodeFound => "We have no code for that address. Ask for a new one.",
            _ => "That code is not right. Check the email and try again."
        };

        private CookieOptions BuildCodeCookieOptions() => new()
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.Add(PasswordResetCodeService.Lifetime),
            Path = "/"
        };

        private string? ResolveClientIp()
        {
            var forwarded = HttpContext.Connection.RemoteIpAddress;
            return forwarded?.ToString();
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> PendingApproval()
        {
            if (!User.IsInRole(FarmerRole))
            {
                return Forbid();
            }

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            var account = userId is null
                ? null
                : await db.Users
                    .AsNoTracking()
                    .Include(user => user.FarmerProfile)
                    .FirstOrDefaultAsync(user => user.Id == userId, HttpContext.RequestAborted);
            if (account is null || !account.IsActive)
            {
                await signInManager.SignOutAsync();
                return RedirectToAction(nameof(Login));
            }

            if (account.FarmerProfile is null)
            {
                await signInManager.SignOutAsync();
                TempData["ErrorMessage"] = "No farmer profile is associated with this account.";
                return RedirectToAction(nameof(Login));
            }

            if (account.FarmerProfile.Status == FarmerStatus.Active)
            {
                return RedirectToAction("Index", "Dashboard", new { area = "Farmer" });
            }

            if (account.FarmerProfile.Status is FarmerStatus.Suspended or FarmerStatus.Rejected)
            {
                await signInManager.SignOutAsync();
                TempData["ErrorMessage"] = "Your farmer profile does not currently have marketplace access.";
                return RedirectToAction(nameof(Login));
            }

            ViewData["Title"] = "Farmer approval pending";
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> AccessDenied()
        {
            ViewData["Title"] = "Access denied";

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var status = await db.FarmerProfiles
                    .AsNoTracking()
                    .Where(profile => profile.UserId == userId)
                    .Select(profile => (FarmerStatus?)profile.Status)
                    .FirstOrDefaultAsync(HttpContext.RequestAborted);

                if (status == FarmerStatus.PendingApproval)
                {
                    return RedirectToAction(nameof(PendingApproval));
                }

                if (status is FarmerStatus.Suspended or FarmerStatus.Rejected)
                {
                    ViewData["FarmerBlocked"] = status;
                }
            }

            // When an administrator or a farmer is stopped on the way to checkout,
            // the page explains that ordering needs a customer account.
            var returnUrl = Request.Query["ReturnUrl"].ToString();
            if (returnUrl.Contains("Checkout", StringComparison.OrdinalIgnoreCase)
                || returnUrl.Contains("Confirmation", StringComparison.OrdinalIgnoreCase))
            {
                ViewData["CustomerOrderNotice"] = true;
            }

            return View();
        }

        private async Task<IdentityResult> EnsureRoleExistsAsync(string role)
        {
            return await roleManager.RoleExistsAsync(role)
                ? IdentityResult.Success
                : await roleManager.CreateAsync(new IdentityRole(role));
        }

        private async Task StampCurrentSessionAsync(ApplicationUser user, bool isPersistent)
        {
            var stamp = HttpContext.RequestServices.GetRequiredService<ServerInstanceStamp>();
            var claims = new List<System.Security.Claims.Claim>
            {
                new(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id),
                new(System.Security.Claims.ClaimTypes.Name, user.UserName ?? user.Email ?? string.Empty),
                new(ServerInstanceStamp.ClaimType, stamp.Value)
            };

            foreach (var role in await userManager.GetRolesAsync(user))
            {
                claims.Add(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, role));
            }

            await signInManager.SignInWithClaimsAsync(user, isPersistent, claims);
        }

        private void AddIdentityErrors(IdentityResult result)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        private static string BuildFullAddress(string address, string city, string region, string postalCode)
        {
            var parts = new List<string> { address.Trim(), city.Trim() };
            var regionalPart = $"{region.Trim()} {postalCode.Trim()}".Trim();
            if (!string.IsNullOrWhiteSpace(regionalPart))
            {
                parts.Add(regionalPart);
            }

            return string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        private async Task<string> SaveProfilePictureAsync(IFormFile? file, CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0)
            {
                return string.Empty;
            }

            const long maximumBytes = 8 * 1024 * 1024;
            if (file.Length > maximumBytes)
            {
                throw new InvalidOperationException("The profile photo must be 8 MB or smaller.");
            }

            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();
            var extension = GetValidatedImageExtension(bytes, file.ContentType);
            var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            var directory = Path.Combine(webRoot, "uploads", "farmer-profiles");
            Directory.CreateDirectory(directory);
            var fileName = $"{Guid.NewGuid():N}{extension}";
            var physicalPath = Path.Combine(directory, fileName);
            await using (var output = new FileStream(physicalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await output.WriteAsync(bytes, cancellationToken);
            }

            return $"/uploads/farmer-profiles/{fileName}";
        }

        private static string GetValidatedImageExtension(byte[] bytes, string contentType)
        {
            var extension = MarketLinkWebsite.Areas.Farmer.Controllers.ImageUpload
                .ExtensionFor(contentType, contentType, bytes);

            return extension
                ?? throw new InvalidOperationException(
                    $"That file is not a picture. Choose an image: {MarketLinkWebsite.Areas.Farmer.Controllers.ImageUpload.Describe()}.");
        }

        private void DeleteProfilePicture(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return;
            }

            var fileName = Path.GetFileName(relativePath);
            var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            var physicalPath = Path.Combine(webRoot, "uploads", "farmer-profiles", fileName);
            try
            {
                if (System.IO.File.Exists(physicalPath))
                {
                    System.IO.File.Delete(physicalPath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public class LoginForm
    {
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(256, ErrorMessage = "Email address must be 256 characters or fewer.")]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your password.")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        [StringLength(100, ErrorMessage = "Password must be 100 characters or fewer.")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Keep me signed in")]
        public bool RememberMe { get; set; }

        public string? ReturnUrl { get; set; }
    }

    public class CustomerRegistrationForm
    {
        [Required(ErrorMessage = "Enter your first name.")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "First name must be between 2 and 50 characters.")]
        [PersonName]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your last name.")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Last name must be between 2 and 50 characters.")]
        [PersonName]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(256, ErrorMessage = "Email address must be 256 characters or fewer.")]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your phone number.")]
        [Phone(ErrorMessage = "Enter a valid phone number.")]
        [StringLength(30, ErrorMessage = "Phone number must be 30 characters or fewer.")]
        [PhoneNumber]
        [Display(Name = "Phone number")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your street address.")]
        [StringLength(250, MinimumLength = 5, ErrorMessage = "Enter a complete street address.")]
        [StreetAddress]
        [Display(Name = "Street address")]
        public string AddressLine { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your city.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Enter a valid city.")]
        [BusinessName]
        [Display(Name = "City")]
        public string City { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your state or region.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Enter a state or region.")]
        [Display(Name = "State / region")]
        public string Region { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your postal code.")]
        [StringLength(20, MinimumLength = 3, ErrorMessage = "Enter a valid postal code.")]
        [Display(Name = "Postal code")]
        public string PostalCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Create a password.")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        
            [Coordinate]
            [Range(typeof(decimal), "-90", "90", ErrorMessage = "Latitude must be between -90 and 90.")]
            [Display(Name = "Latitude")]
            public decimal? Latitude { get; set; }

            [Coordinate]
            [Range(typeof(decimal), "-180", "180", ErrorMessage = "Longitude must be between -180 and 180.")]
            [Display(Name = "Longitude")]
            public decimal? Longitude { get; set; }

        [Display(Name = "I agree to the MarketLink terms and privacy policy")]
        [MustBeTrue("Tick the box to accept the terms and the privacy policy.")]
        public bool AcceptTerms { get; set; }
    }

    public class FarmerRegistrationForm
    {
        [Required(ErrorMessage = "Enter your first name.")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "First name must be between 2 and 50 characters.")]
        [PersonName]
        [Display(Name = "First name")]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your last name.")]
        [StringLength(50, MinimumLength = 2, ErrorMessage = "Last name must be between 2 and 50 characters.")]
        [PersonName]
        [Display(Name = "Last name")]
        public string LastName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(256, ErrorMessage = "Email address must be 256 characters or fewer.")]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your phone number.")]
        [Phone(ErrorMessage = "Enter a valid phone number.")]
        [StringLength(30, ErrorMessage = "Phone number must be 30 characters or fewer.")]
        [PhoneNumber]
        [Display(Name = "Phone number")]
        public string PhoneNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Create a password.")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your farm or business name.")]
        [StringLength(150, MinimumLength = 2, ErrorMessage = "Farm name must be between 2 and 150 characters.")]
        [BusinessName]
        [Display(Name = "Farm or business name")]
        public string FarmName { get; set; } = string.Empty;

        [StringLength(600, ErrorMessage = "Farm description must be 600 characters or fewer.")]
        [Display(Name = "Tell shoppers about your farm")]
        public string Description { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your farm address.")]
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Enter a complete farm address.")]
        [StreetAddress]
        [Display(Name = "Farm street address")]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your city.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Enter a valid city.")]
        [BusinessName]
        [Display(Name = "City")]
        public string City { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your state or region.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Enter a state or region.")]
        [Display(Name = "State / region")]
        public string Region { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your postal code.")]
        [StringLength(20, MinimumLength = 3, ErrorMessage = "Enter a valid postal code.")]
        [Display(Name = "Postal code")]
        public string PostalCode { get; set; } = string.Empty;

        [Display(Name = "Farm profile photo")]
        [ImageUpload(8, "Choose a JPG, PNG, GIF, BMP or WEBP picture up to 8 MB.")]
        public IFormFile? ProfilePicture { get; set; }

        
            [Coordinate]
            [Range(typeof(decimal), "-90", "90", ErrorMessage = "Latitude must be between -90 and 90.")]
            [Display(Name = "Latitude")]
            public decimal? Latitude { get; set; }

            [Coordinate]
            [Range(typeof(decimal), "-180", "180", ErrorMessage = "Longitude must be between -180 and 180.")]
            [Display(Name = "Longitude")]
            public decimal? Longitude { get; set; }

        [Display(Name = "I agree to the seller terms and marketplace guidelines")]
        [MustBeTrue("Tick the box to accept the seller terms and the marketplace guidelines.")]
        public bool AcceptTerms { get; set; }
    }

    public class ForgotPasswordForm
    {
        [Required(ErrorMessage = "Enter your email address.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        [StringLength(256, ErrorMessage = "Email address must be 256 characters or fewer.")]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;
    }

    public class VerifyCodeForm
    {
        [Required]
        [EmailAddress]
        [StringLength(256, ErrorMessage = "Email address must be 256 characters or fewer.")]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(64, ErrorMessage = "Enter the code from the email.")]
        [Display(Name = "Verification code")]
        public string Code { get; set; } = string.Empty;
    }

    public class ResetPasswordForm
    {
        [Required]
        [EmailAddress]
        [StringLength(256, ErrorMessage = "Email address must be 256 characters or fewer.")]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(64, ErrorMessage = "Enter the code from the email.")]
        [Display(Name = "Verification code")]
        public string Code { get; set; } = string.Empty;

        [Required(ErrorMessage = "Create a new password.")]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters.")]
        [Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm new password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
