using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarketLinkWebsite.Controllers;

[ApiController]
public sealed class GeoController : Controller
{
    private readonly GeoLocationService geo;

    public GeoController(GeoLocationService geo)
    {
        this.geo = geo;
    }

    [HttpPost("/Geo/Save")]
    [IgnoreAntiforgeryToken]
    public IActionResult Save([FromForm] double latitude, [FromForm] double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
        {
            return BadRequest(new { ok = false });
        }

        geo.Write(latitude, longitude);
        return Json(new { ok = true, latitude = geo.Latitude, longitude = geo.Longitude });
    }

    [HttpGet("/Geo/Status")]
    public IActionResult Status()
    {
        var current = geo.Read();
        return Json(new
        {
            hasLocation = current.HasLocation,
            latitude = current.Latitude,
            longitude = current.Longitude
        });
    }
}
