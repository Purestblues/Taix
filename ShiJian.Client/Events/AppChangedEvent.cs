using ShiJian.Client.Shared.Models;

namespace ShiJian.Client.Events;

public record AppChangedEvent(AppModel App, AppChangeType ChangeType);