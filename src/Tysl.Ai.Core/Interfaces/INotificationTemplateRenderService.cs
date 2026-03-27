using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface INotificationTemplateRenderService
{
    IReadOnlyDictionary<string, string> GetSupportedVariables();

    string Render(string templateContent, NotificationTemplateRenderContext context);
}
