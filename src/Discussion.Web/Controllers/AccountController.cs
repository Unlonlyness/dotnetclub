﻿using Discussion.Core.Models;
using Discussion.Core.Mvc;
using Discussion.Core.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using Discussion.Core.Data;
using Discussion.Core.Time;
using Discussion.Web.Services;
using Discussion.Web.Services.UserManagement;
using Discussion.Web.Services.UserManagement.Exceptions;
using Discussion.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;

namespace Discussion.Web.Controllers
{
    public class AccountController : Controller
    {
        readonly UserManager<User> _userManager;
        readonly SignInManager<User> _signInManager;
        readonly ILogger<AccountController> _logger;
        readonly IRepository<User> _userRepo;
        readonly IClock _clock;
        readonly SiteSettings _settings;
        readonly IUserService _userService;

        public AccountController(
            UserManager<User> userManager,
            SignInManager<User> signInManager,
            IUserService userService,
            ILogger<AccountController> logger,
            IRepository<User> userRepo,
            IClock clock,
            SiteSettings settings)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _userRepo = userRepo;
            _clock = clock;
            _settings = settings;
            _userService = userService;
        }

        [Route("/signin")]
        [IdentityServerAction(IdentityAction.Signin)]
        public IActionResult Signin([FromQuery] string returnUrl)
        {
            if (HttpContext.IsAuthenticated())
            {
                return RedirectTo(returnUrl);
            }
            return View();
        }

        [HttpPost]
        [Route("/signin")]
        public async Task<IActionResult> DoSignin(
            [FromForm] UserViewModel viewModel,
            [FromQuery] string returnUrl)
        {
            if (HttpContext.IsAuthenticated())
            {
                return RedirectTo(returnUrl);
            }

            var result = Microsoft.AspNetCore.Identity.SignInResult.Failed;
            if (ModelState.IsValid)
            {
                result = await _signInManager.PasswordSignInAsync(
                    viewModel.UserName,
                    viewModel.Password,
                    isPersistent: false,
                    lockoutOnFailure: false);

                var logLevel = result.Succeeded ? LogLevel.Information : LogLevel.Warning;
                var resultDesc = result.Succeeded ? "成功" : "失败";
                _logger.Log(logLevel, $"用户登录{resultDesc}：{{@LoginAttempt}}", new {viewModel.UserName, Result = result.ToString()} );
            }
            else
            {
                _logger.LogWarning("用户登录失败：{@LoginAttempt}", new {viewModel.UserName, Result = "数据格式不正确"});
            }

            if (!result.Succeeded)
            {
                ModelState.Clear(); // 将真正的验证结果隐藏掉（如果有的话）
                ModelState.AddModelError("UserName", "用户名或密码错误");
                return View("Signin");
            }

            var user = await _userManager.FindByNameAsync(viewModel.UserName);
            user.LastSeenAt = _clock.Now.UtcDateTime;
            _userRepo.Update(user);
            return RedirectTo(returnUrl);
        }

        [HttpPost]
        [Route("/signout")]
        [Authorize]
        [IdentityServerAction(IdentityAction.SignOut)]
        public async Task<IActionResult> DoSignOut()
        {
            await _signInManager.SignOutAsync();
            return RedirectTo("/");
        }

        [Route("/register")]
        [IdentityServerAction(IdentityAction.Register)]

        public IActionResult Register()
        {
            if (HttpContext.IsAuthenticated())
            {
                return RedirectTo("/");
            }

            return View();
        }

        [HttpPost]
        [Route("/register")]
        public async Task<IActionResult> DoRegister(UserViewModel registerModel)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogInformation("用户注册失败：{@RegisterAttempt}", new {registerModel.UserName, Result = "数据格式不正确"});
                return View("Register");
            }

            if (!_settings.CanRegisterNewUsers())
            {
                const string errorMessage = "已关闭用户注册";
                _logger.LogWarning("用户注册失败：{@RegisterAttempt}", new {registerModel.UserName, Result = errorMessage});
                ModelState.AddModelError("UserName", errorMessage);
                return View("Register");
            }

            var newUser = new User
            {
                UserName = registerModel.UserName,
                DisplayName = registerModel.UserName,
                CreatedAtUtc = _clock.Now.UtcDateTime
            };

            var result = await _userManager.CreateAsync(newUser, registerModel.Password);
            if (!result.Succeeded)
            {
                var errorMessage = string.Join(";", result.Errors.Select(err => err.Description));
                ModelState.AddModelError("UserName", errorMessage);
                _logger.LogWarning("用户注册失败：{@RegisterAttempt}", new {registerModel.UserName, Result = errorMessage});
                return View("Register");
            }

            _logger.LogInformation("用户注册成功：{@RegisterAttempt}", new {registerModel.UserName, UserId = newUser.Id});
            await _signInManager.PasswordSignInAsync(
                registerModel.UserName,
                registerModel.Password,
                isPersistent: false,
                lockoutOnFailure: true);
            return RedirectTo("/");
        }

        [Route("/forgot-password")]
        public IActionResult ForgotPassword()
        {
            if (HttpContext.IsAuthenticated())
            {
                return RedirectTo("/");
            }

            return View();
        }

        [HttpPost]
        [Route("/forgot-password")]
        public async Task<ApiResponse> DoForgotPassword(ForgotPasswordModel model)
        {
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("发送重置密码邮件失败：{@ForgotPasswordAttempt}", new {model.UsernameOrEmail, Result = "数据格式不正确"});
                return ApiResponse.Error(ModelState);
            }

            try
            {
                var user = GetUserBy(model);
                await _userService.SendEmailRetrievePasswordAsync(user, Request.Scheme);
                _logger.LogInformation("发送重置密码邮件成功：{ConfirmedEmail}", user.ConfirmedEmail);
                return ApiResponse.NoContent();
            }
            catch (RetrievePasswordVerificationException e)
            {
                _logger.LogWarning("发送重置密码邮件失败：{@ForgotPasswordAttempt}", new {model.UsernameOrEmail, Result = e.Message});
                return ApiResponse.Error(e.Message);
            }
        }

        [HttpGet]
        [Route("/reset-password")]
        public IActionResult ResetPassword(ResetPasswordModel model)
        {
            ModelState.Clear();

            bool ret;
            var userEmailToken = UserEmailToken.ExtractFromQueryString(model.Token);
            if (userEmailToken == null)
            {
                var errorMessage = "无法识别的凭证";
                ModelState.AddModelError(nameof(model.Token), errorMessage);
                _logger.LogWarning("重置密码失败：{@ResetPasswordAttempt}", new {model.Token, model.UserId, Result = errorMessage});
                return View("ResetPassword", model);
            }
            
            model.Token = userEmailToken.Token;
            model.UserId = userEmailToken.UserId;
            return View(model);
        }

        [HttpPost]
        [Route("/reset-password")]
        public async Task<IActionResult> DoResetPassword(ResetPasswordModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByIdAsync(model.UserId.ToString());
            if (user == null)
            {
                var errorMessage = "用户不存在";
                ModelState.AddModelError(nameof(model.UserId), errorMessage);
                _logger.LogWarning("重置密码失败：{@ResetPasswordAttempt}", new { model.Token, model.UserId, Result = errorMessage});
                return View(model);
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
            if (result.Errors.Any())
            {
                var msg = string.Join(";", result.Errors.Select(e => e.Description));
                ModelState.AddModelError(nameof(model.Token), msg);
                _logger.LogWarning("重置密码失败：{@ResetPasswordAttempt}", new { model.Token, model.UserId, Result = msg});
                model.Succeeded = false;
            }
            else
            {
                _logger.LogInformation("重置密码成功：{UserName}", user.UserName);
                model.Succeeded = true;
            }

            return View(model);
        }

        User GetUserBy(ForgotPasswordModel model)
        {
            var usernameOrEmail = model.UsernameOrEmail.ToLower();

            var users = _userRepo
                .All()
                .Where(e => e.UserName.ToLower() == usernameOrEmail ||
                            e.EmailAddress != null && e.EmailAddress.ToLower() == usernameOrEmail)
                .ToList();

            if (!users.Any())
                throw new RetrievePasswordVerificationException("该用户不存在");

            var user = users.FirstOrDefault(e => e.EmailAddressConfirmed);

            if (user == null)
                throw new RetrievePasswordVerificationException("无法验证你对账号的所有权，因为之前没有已验证过的邮箱地址");

            return user;
        }


        IActionResult RedirectTo(string returnUrl)
        {
            if (string.IsNullOrEmpty(returnUrl))
            {
                returnUrl = "/";
            }

            return Redirect(returnUrl);
        }
    }
}