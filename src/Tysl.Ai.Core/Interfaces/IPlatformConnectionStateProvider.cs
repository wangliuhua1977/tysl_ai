using Tysl.Ai.Core.Models;

namespace Tysl.Ai.Core.Interfaces;

public interface IPlatformConnectionStateProvider
{
    PlatformConnectionState GetCurrentState();
}
