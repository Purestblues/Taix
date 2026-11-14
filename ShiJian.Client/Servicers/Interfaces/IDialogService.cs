using System;
using System.Threading.Tasks;

namespace ShiJian.Client.Servicers.Interfaces;

public interface IDialogService
{
    Task<bool> ShowConfirmDialogAsync(string title, string message);
    Task<string?> ShowInputModalAsync(string title, string placeholder, string value = null, Func<string, bool>? validate = null);
    Task<string?> ShowFolderPickerAsync(string? title = null);
    Task<string?> ShowSaveFilePickerAsync(string suggestedFileName, string defaultExtension, string filterName, string[] patterns, string? title = null);
    Task<string?> ShowOpenFilePickerAsync(string filterName, string[] patterns, string? title = null);
    Task<int> ShowActionDialogAsync(string title, string message, string[] buttons);
}
