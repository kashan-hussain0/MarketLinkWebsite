using MarketLinkWebsite.Models.ViewModels;
using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarketLinkWebsite.Controllers;

public sealed class HomeController : Controller
{
    private readonly ICatalogService catalogService;
    private readonly IEmailSender emailSender;
    private readonly IConfiguration configuration;
    private readonly MapService mapService;

    public HomeController(
        ICatalogService catalogService,
        IEmailSender emailSender,
        IConfiguration configuration,
        MapService mapService)
    {
        this.catalogService = catalogService;
        this.emailSender = emailSender;
        this.configuration = configuration;
        this.mapService = mapService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var model = await catalogService.GetHomeAsync(cancellationToken);
        var farms = await catalogService.GetFarmSpotlightAsync(8, cancellationToken);
        ViewData["Farms"] = farms;
        return View(model);
    }

    [HttpGet]
    public IActionResult About()
    {
        ViewData["PlatformName"] = configuration["Platform:Name"] ?? "MarketLink";
        return View();
    }

    [HttpGet]
    public IActionResult Privacy()
    {
        ViewData["PlatformName"] = configuration["Platform:Name"] ?? "MarketLink";
        return View();
    }

    [HttpGet]
    public IActionResult Contact()
    {
        var model = new ContactFormViewModel();
        PopulateContact(model);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Contact(ContactFormViewModel model, CancellationToken cancellationToken)
    {
        PopulateContact(model);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var recipient = configuration["Email:ContactRecipient"];
        if (string.IsNullOrWhiteSpace(recipient))
        {
            recipient = configuration["Platform:SupportEmail"] ?? configuration["Seed:AdminEmail"];
        }

        if (!string.IsNullOrWhiteSpace(recipient))
        {
            var body = $"Name: {model.Name.Trim()}\nEmail: {model.Email.Trim()}\nTopic: {model.Topic.Trim()}\n\n{model.Message.Trim()}";
            try
            {
                await emailSender.SendAsync(recipient, "MarketLink contact: " + model.Topic.Trim(), body, cancellationToken);
            }
            catch (Exception)
            {
            }
        }

        TempData["SuccessMessage"] = "Thanks for getting in touch. We reply to most messages within one working day.";
        return RedirectToAction(nameof(Contact));
    }

    private void PopulateContact(ContactFormViewModel model)
    {
        ViewData["PlatformName"] = configuration["Platform:Name"] ?? "MarketLink";
        ViewData["SupportEmail"] = configuration["Platform:SupportEmail"] ?? configuration["Seed:AdminEmail"] ?? string.Empty;
        ViewData["SupportPhone"] = configuration["Platform:SupportPhone"] ?? string.Empty;
        ViewData["SupportAddress"] = mapService.PlatformAddress;
        ViewData["MapProvider"] = mapService.ProviderLabel;
        ViewData["MapLatitude"] = mapService.PlatformLatitude;
        ViewData["MapLongitude"] = mapService.PlatformLongitude;
        ViewData["SupportEmail"] = mapService.PlatformEmail;
        ViewData["SupportPhone"] = mapService.PlatformPhone;
        ViewData["MapUrl"] = mapService.SinglePinUrl(new MapPin
        {
            Latitude = mapService.PlatformLatitude,
            Longitude = mapService.PlatformLongitude,
            Label = "MarketLink"
        }, 15);
        ViewData["DirectionsUrl"] = mapService.SearchUrl(mapService.PlatformAddress);
    }
}
