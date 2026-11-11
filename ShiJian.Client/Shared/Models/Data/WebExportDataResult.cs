using System.Collections.Generic;
using ShiJian.Client.Shared.Models.Web;

namespace ShiJian.Client.Shared.Models.Data;

public class WebExportDataResult
{
    public List<WebBrowseLogModel> Logs { get; set; } = new();
}
