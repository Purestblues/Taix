using System.Collections.Generic;

namespace ShiJian.Client.Shared.Models.Data;

public class ExportDataResult
{
    public List<DailyLogModel> DailyLogs { get; set; } = new();
    public List<HoursLogModel> HoursLogs { get; set; } = new();
}
