using System.ComponentModel.DataAnnotations;
using System.Globalization;
using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarketLinkWebsite.Validation;

namespace MarketLinkWebsite.Areas.Farmer.Controllers;

/// <summary>
/// Lets a farmer publish the pickup windows they actually work on, so customers
/// can only book a slot that the farm is really open for.
/// </summary>
[Area("Farmer")]
[Authorize(Policy = "ActiveFarmerAccess")]
public sealed class PickupSlotController : FarmerControllerBase
{
    public PickupSlotController(ApplicationDbContext db) : base(db)
    {
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        await SetFarmerChromeAsync(profile, cancellationToken: cancellationToken);

        var slots = await Db.PickupSlots
            .AsNoTracking()
            .Where(slot => slot.FarmerProfileId == profile.Id)
            .OrderBy(slot => slot.DayOfWeek)
            .ThenBy(slot => slot.StartTime)
            .ToListAsync(cancellationToken);

        var model = new PickupSlotViewModel
        {
            Slots = slots.Select(slot => new PickupSlotRow
            {
                Id = slot.Id,
                DayOfWeek = slot.DayOfWeek,
                DayName = DayName(slot.DayOfWeek),
                StartTime = slot.StartTime,
                EndTime = slot.EndTime,
                Label = $"{DayName(slot.DayOfWeek)} · {slot.StartTime} - {slot.EndTime}",
                IsActive = slot.IsActive
            }).ToList(),
            NewSlot = new PickupSlotForm
            {
                DayOfWeek = NextOperatingDay(profile.OperatingDays),
                StartTime = FirstWindowStart(profile.PickupWindows) ?? "09:00",
                EndTime = FirstWindowEnd(profile.PickupWindows) ?? "12:00"
            },
            Days = Enum.GetValues<DayOfWeek>()
                .Select(day => new PickupSlotDayOption
                {
                    Value = (int)day,
                    Name = day.ToString()
                })
                .ToList(),
            ProfileDays = profile.OperatingDays
        };

        ViewData["Title"] = "Pickup slots";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PickupSlotForm model, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        model ??= new PickupSlotForm();

        var startText = model.StartTime?.Trim() ?? string.Empty;
        var endText = model.EndTime?.Trim() ?? string.Empty;

        if (!IsValidTime(startText, out var start) || !IsValidTime(endText, out var end))
        {
            TempData["Error"] = "Enter the start and end time as HH:mm.";
            return RedirectToAction(nameof(Index));
        }

        if (end <= start)
        {
            TempData["Error"] = "The end time must be after the start time.";
            return RedirectToAction(nameof(Index));
        }

        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Check the slot times and try again.";
            return RedirectToAction(nameof(Index));
        }

        // Overlap is worked out from the clock values rather than the text, so a
        // slot written "9:00" is caught as well as one written "09:00".
        var sameDay = await Db.PickupSlots
            .AsNoTracking()
            .Where(slot => slot.FarmerProfileId == profile.Id
                && slot.DayOfWeek == model.DayOfWeek
                && slot.IsActive)
            .Select(slot => new { slot.StartTime, slot.EndTime })
            .ToListAsync(cancellationToken);

        var overlaps = sameDay.Any(slot =>
            IsValidTime(slot.StartTime, out var otherStart)
            && IsValidTime(slot.EndTime, out var otherEnd)
            && start < otherEnd
            && end > otherStart);

        if (overlaps)
        {
            TempData["Error"] = "That window overlaps a slot you already published for this day.";
            return RedirectToAction(nameof(Index));
        }

        Db.PickupSlots.Add(new PickupSlot
        {
            FarmerProfileId = profile.Id,
            DayOfWeek = model.DayOfWeek,
            StartTime = startText,
            EndTime = endText,
            IsActive = true
        });

        AddAudit("CreatePickupSlot", nameof(PickupSlot), profile.Id,
            $"Published a {DayName(model.DayOfWeek)} pickup slot from {startText} to {endText}.");

        await Db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Pickup slot published. Customers can now book it.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int id, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        var slot = await Db.PickupSlots
            .FirstOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);

        if (slot is null)
        {
            TempData["Error"] = "That slot could not be found.";
            return RedirectToAction(nameof(Index));
        }

        slot.IsActive = !slot.IsActive;
        AddAudit("TogglePickupSlot", nameof(PickupSlot), slot.Id,
            $"{(slot.IsActive ? "Opened" : "Closed")} the {DayName(slot.DayOfWeek)} slot {slot.StartTime} to {slot.EndTime}.");

        await Db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = slot.IsActive ? "Slot reopened for bookings." : "Slot closed for new bookings.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
    {
        var profile = await GetCurrentFarmerAsync(cancellationToken);
        if (profile is null)
        {
            return FarmerAccessResult();
        }

        var slot = await Db.PickupSlots
            .FirstOrDefaultAsync(item => item.Id == id && item.FarmerProfileId == profile.Id, cancellationToken);

        if (slot is null)
        {
            TempData["Error"] = "That slot could not be found.";
            return RedirectToAction(nameof(Index));
        }

        Db.PickupSlots.Remove(slot);
        AddAudit("DeletePickupSlot", nameof(PickupSlot), id,
            $"Removed the {DayName(slot.DayOfWeek)} pickup slot {slot.StartTime} to {slot.EndTime}.");

        await Db.SaveChangesAsync(cancellationToken);

        TempData["Success"] = "Pickup slot removed.";
        return RedirectToAction(nameof(Index));
    }

    // DayOfWeek counts Sunday as zero, so the names come from the enum rather than
    // a hand-written list that could drift out of step with it.
    private static string DayName(int dayOfWeek) =>
        Enum.IsDefined(typeof(DayOfWeek), dayOfWeek)
            ? ((DayOfWeek)dayOfWeek).ToString()
            : "Any day";

    private static bool IsValidTime(string? value, out TimeSpan parsed) =>
        TimeSpan.TryParseExact(value?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out parsed)
        || TimeSpan.TryParseExact(value?.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out parsed);

    private static int NextOperatingDay(string? operatingDays)
    {
        if (string.IsNullOrWhiteSpace(operatingDays))
        {
            return (int)DateTime.Today.DayOfWeek;
        }

        var days = operatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var offset = 0; offset < 7; offset++)
        {
            var candidate = DateTime.Today.AddDays(offset);
            if (days.Any(day => day.Equals(candidate.DayOfWeek.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                return (int)candidate.DayOfWeek;
            }
        }

        return (int)DateTime.Today.DayOfWeek;
    }

    private static string? FirstWindowStart(string? pickupWindows) => WindowPart(pickupWindows, 0);
    private static string? FirstWindowEnd(string? pickupWindows) => WindowPart(pickupWindows, 1);

    private static string? WindowPart(string? pickupWindows, int index)
    {
        if (string.IsNullOrWhiteSpace(pickupWindows))
        {
            return null;
        }

        var first = pickupWindows.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        if (first is null)
        {
            return null;
        }

        var times = first.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (times.Length != 2)
        {
            return null;
        }

        // The profile windows read like "09:00 AM - 12:00 PM", which the HH:mm form
        // does not accept, so the clock part is pulled out and normalised.
        var raw = times[index].Trim();
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateTime))
        {
            return $"{dateTime.Hour:D2}:{dateTime.Minute:D2}";
        }

        if (TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed))
        {
            return $"{parsed.Hours:D2}:{parsed.Minutes:D2}";
        }

        return null;
    }
}

public sealed class PickupSlotViewModel
{
    public List<PickupSlotRow> Slots { get; set; } = new();
    public PickupSlotForm NewSlot { get; set; } = new();
    public List<PickupSlotDayOption> Days { get; set; } = new();
    public string ProfileDays { get; set; } = string.Empty;
}

public sealed class PickupSlotRow
{
    public int Id { get; set; }
    public int DayOfWeek { get; set; }
    public string DayName { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public sealed class PickupSlotDayOption
{
    public int Value { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class PickupSlotForm
{
    [Range(0, 6)]
    public int DayOfWeek { get; set; }

    [Required]
    [StringLength(5)]
    [ClockTime("Enter the start time in 24 hour form, for example 09:00.")]
    public string? StartTime { get; set; }

    [Required]
    [StringLength(5)]
    [ClockTime("Enter the end time in 24 hour form, for example 12:00.")]
    public string? EndTime { get; set; }
}
