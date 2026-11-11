using System.Collections.Generic;

namespace ShiJian.Client.Servicers.Dialogs;

public interface IDialogResult
{
    IDictionary<object, object?> Parameters { get; }

    ButtonResult Result { get; }
}