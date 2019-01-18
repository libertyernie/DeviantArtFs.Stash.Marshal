using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DeviantArtFs.Stash.Marshal.Examples.StashInterface.Data;
using DeviantArtFs.Stash.Marshal.Examples.StashInterface.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeviantArtFs.Stash.Marshal.Examples.StashInterface.Controllers
{
    public class HomeController : Controller
    {
        private readonly ExampleDbContext _context;
        private readonly DeviantArtAuth _appReg;

        public HomeController(ExampleDbContext context, DeviantArtAuth appReg)
        {
            _context = context;
            _appReg = appReg;
        }

        public IActionResult Index()
        {
            return View();
        }

        private async Task<Token> GetAccessTokenAsync()
        {
            string str = User.Claims
                .Where(c => c.Type == "token-id")
                .Select(c => c.Value)
                .FirstOrDefault();
            if (str != null && Guid.TryParse(str, out Guid tokenId))
            {
                var token = await _context.Tokens.SingleOrDefaultAsync(t => t.Id == tokenId);
                if (token != null)
                {
                    if (token.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(5))
                    {
                        var result = await _appReg.RefreshAsync(token.RefreshToken);
                        token.AccessToken = result.AccessToken;
                        token.RefreshToken = result.RefreshToken;
                        token.ExpiresAt = result.ExpiresAt;
                        await _context.SaveChangesAsync();
                    }
                    return token;
                }
            }
            return null;
        }

        public async Task<IActionResult> Login()
        {
            if (await GetAccessTokenAsync() != null)
            {
                return RedirectToAction("Index");
            }
            int client_id = Startup.DeviantArtClientId;
            return Redirect($"https://www.deviantart.com/oauth2/authorize?response_type=code&client_id={client_id}&redirect_uri=https://{HttpContext.Request.Host}/Home/Callback&scope=stash");
        }

        public async Task<IActionResult> Callback(string code, string state = null)
        {
            var result = await _appReg.GetTokenAsync(code, new Uri($"https://{HttpContext.Request.Host}/Home/Callback"));
            var me = await Requests.User.Whoami.ExecuteAsync(result);
            var token = new Token
            {
                Id = Guid.NewGuid(),
                UserId = me.Userid,
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken,
                ExpiresAt = result.ExpiresAt
            };
            _context.Tokens.Add(token);
            await _context.SaveChangesAsync();

            var claimsIdentity = new ClaimsIdentity(
                new[] {
                    new Claim(ClaimTypes.Name, me.Username),
                    new Claim("token-id", token.Id.ToString())
                }, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity));
            return RedirectToAction("Index");
        }

        public async Task<IActionResult> Whoami()
        {
            var t = await GetAccessTokenAsync();
            if (t == null)
                return RedirectToAction("Login");

            var me = await Requests.User.Whoami.ExecuteAsync(t);
            return Json(me);
        }

        public async Task<IActionResult> FullRefresh()
        {
            var t = await GetAccessTokenAsync();
            if (t == null)
                return RedirectToAction("Login");

            var existingCursors = await _context.DeltaCursors
                .Where(x => x.UserId == t.UserId)
                .ToListAsync();

            var existingItems = await _context.StashEntries
                .Where(x => x.UserId == t.UserId)
                .OrderBy(x => x.Position)
                .ToListAsync();

            _context.RemoveRange(existingCursors);
            _context.RemoveRange(existingItems);
            await _context.SaveChangesAsync();

            return await Refresh();
        }

        public async Task<IActionResult> Dump()
        {
            var t = await GetAccessTokenAsync();
            if (t == null)
                return RedirectToAction("Login");

            var existingCursor = await _context.DeltaCursors
                .Where(x => x.UserId == t.UserId)
                .Select(x => x.Cursor)
                .SingleOrDefaultAsync();

            var existingItems = await _context.StashEntries
                .Where(x => x.UserId == t.UserId)
                .OrderBy(x => x.Position)
                .ToListAsync();

            return Json(new
            {
                existingCursor,
                existingItems
            });
        }

        public async Task<IActionResult> Refresh()
        {
            var t = await GetAccessTokenAsync();
            if (t == null)
                return RedirectToAction("Login");

            var existingCursor = await _context.DeltaCursors
                .Where(x => x.UserId == t.UserId)
                .Select(x => x.Cursor)
                .SingleOrDefaultAsync();

            var existingItems = await _context.StashEntries
                .Where(x => x.UserId == t.UserId)
                .OrderBy(x => x.Position)
                .ToListAsync();
            var stashRoot = new StashRoot();
            foreach (var i in existingItems)
            {
                stashRoot.Apply(i);
            }

            var req = new Requests.Stash.DeltaRequest
            {
                Cursor = existingCursor,
                Offset = 0,
                Limit = 120
            };

            while (true)
            {
                var delta = await Requests.Stash.Delta.ExecuteAsync(t, req);
                existingCursor = delta.Cursor;
                if (delta.Reset)
                {
                    stashRoot.Clear();
                }
                foreach (var i in delta.Entries)
                {
                    stashRoot.Apply(i);
                }
                if (!delta.HasMore) break;
                req.Offset = delta.NextOffset ?? 0;
            }

            _context.StashEntries.RemoveRange(existingItems);
            foreach (var new_entry in stashRoot.Save())
            {
                _context.StashEntries.Add(new StashEntry
                {
                    UserId = t.UserId,
                    ItemId = new_entry.Itemid,
                    StackId = new_entry.Stackid,
                    MetadataJson = new_entry.MetadataJson,
                    Position = new_entry.Position,
                });
            }

            var ex = await _context.DeltaCursors
                .Where(x => x.UserId == t.UserId)
                .SingleOrDefaultAsync();
            if (ex == null)
            {
                ex = new DeltaCursor { UserId = t.UserId };
                _context.DeltaCursors.Add(ex);
            }
            ex.Cursor = existingCursor;

            await _context.SaveChangesAsync();

            return RedirectToAction("ViewStack");
        }

        public async Task<IActionResult> ViewStack(long? stackid = null)
        {
            var t = await GetAccessTokenAsync();
            if (t == null)
                return RedirectToAction("Login");

            var existingItems = await _context.StashEntries
                .Where(x => x.UserId == t.UserId)
                .OrderBy(x => x.Position)
                .ToListAsync();
            var stashRoot = new StashRoot();
            foreach (var i in existingItems)
            {
                stashRoot.Apply(i);
            }

            var children = stackid is long s
                ? stashRoot.FindStackById(s).Children
                : stashRoot.Children;
            if (children.Count() == 1) {
                return RedirectToAction("ViewItem", new { itemid = children.First().BclMetadata.Itemid });
            }
            return View("ViewStack", children);
        }

        public async Task<IActionResult> ViewItem(long itemid)
        {
            var t = await GetAccessTokenAsync();
            if (t == null)
                return RedirectToAction("Login");

            var existingItems = await _context.StashEntries
                .Where(x => x.UserId == t.UserId)
                .Where(x => x.ItemId == itemid)
                .SingleAsync();
            IBclStashMetadata m = StashMetadata.Parse(existingItems.MetadataJson);
            return View("ViewItem", m);
        }

        public async Task<IActionResult> LogOut()
        {
            var t = await GetAccessTokenAsync();
            if (t != null)
            {
                _context.Tokens.RemoveRange(_context.Tokens.Where(x => x.Id == t.Id));
                await _context.SaveChangesAsync();
                await HttpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
            }
            return RedirectToAction("Index");
        }
    }
}