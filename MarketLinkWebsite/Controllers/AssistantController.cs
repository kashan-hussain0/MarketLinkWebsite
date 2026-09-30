using MarketLinkWebsite.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarketLinkWebsite.Controllers;

public sealed class AssistantController : Controller
{
    private readonly AiAssistantService assistant;

    public AssistantController(AiAssistantService assistant)
    {
        this.assistant = assistant;
    }

    [HttpGet]
    public IActionResult Index()
    {
        ViewData["Title"] = "Market assistant";
        ViewData["AssistantMode"] = assistant.ModeLabel;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Ask(string question, CancellationToken cancellationToken)
    {
        var answer = await assistant.AnswerAsync(question, cancellationToken);

        return Json(new
        {
            message = answer.Message,
            isHtml = false,
            mode = answer.FromHostedModel ? "smart" : "local"
        });
    }
}
