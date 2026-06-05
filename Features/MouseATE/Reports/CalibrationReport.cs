using System.IO;
using System.Text;

namespace MouseATE.Reports;

/// <summary>
/// Holds one row of calibration report data — either from ReCalibration or CheckCalibration.
/// "After" columns = DPI test with calibration applied; "Original" columns = sensor-native (no calibration).
/// </summary>
public class CalibrationReportRow
{
    public string SerialNumber { get; set; } = "";
    public int XAfterCount { get; set; }
    public double XAfterDeviationPct { get; set; }
    public int YAfterCount { get; set; }
    public double YAfterDeviationPct { get; set; }
    public int XOrigCount { get; set; }
    public double XOrigDeviationPct { get; set; }
    public int YOrigCount { get; set; }
    public double YOrigDeviationPct { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string AppVersion { get; set; } = "V01";

    public string XAfterCell => $"{XAfterCount} / {XAfterDeviationPct:F2}%";
    public string YAfterCell => $"{YAfterCount} / {YAfterDeviationPct:F2}%";
    public string XOrigCell => $"{XOrigCount} / {XOrigDeviationPct:F2}%";
    public string YOrigCell => $"{YOrigCount} / {YOrigDeviationPct:F2}%";
}

public static class CalibrationReport
{
    public static void WriteCsv(string path, CalibrationReportRow row)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SN,X ODM After Calibration,Y ODM After Calibration,X Sensor Original,Y Sensor Original,Time,Version");
        sb.AppendLine($"\"{row.SerialNumber}\",\"{row.XAfterCell}\",\"{row.YAfterCell}\",\"{row.XOrigCell}\",\"{row.YOrigCell}\",\"{row.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{row.AppVersion}\"");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    /// <summary>
    /// Writes an XLSX calibration report matching the original Python CalibrationReport format.
    /// Returns true on success, false if writing fails for any reason (disk full, permissions, etc.).
    /// Does NOT throw — XLSX output is failsafe; CSV is the primary output.
    /// </summary>
    public static bool TryWriteXlsx(string path, CalibrationReportRow row)
    {
        try
        {
            WriteXlsx(path, row);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteXlsx(string path, CalibrationReportRow row)
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add("Calibration Report");

        // Row 1: merged "Count / Deviation" header over columns B–E
        ws.Cell(1, 1).Value = "SN";
        ws.Range(1, 2, 1, 5).Merge().Value = "Count / Deviation";
        ws.Range(1, 2, 1, 5).Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
        ws.Cell(1, 7).Value = "Time";
        ws.Cell(1, 8).Value = "Version";

        // Row 2: sub-column headers
        ws.Cell(2, 1).Value = "SN";
        ws.Cell(2, 2).Value = "X_ODM after calibration";
        ws.Cell(2, 3).Value = "Y_ODM after calibration";
        ws.Cell(2, 4).Value = "X_Sensor original";
        ws.Cell(2, 5).Value = "Y_Sensor original";
        ws.Cell(2, 7).Value = "Time";
        ws.Cell(2, 8).Value = "Version";

        // Bold headers
        for (int col = 1; col <= 8; col++)
        {
            if (col == 6) continue;
            ws.Cell(1, col).Style.Font.Bold = true;
            ws.Cell(2, col).Style.Font.Bold = true;
        }

        // Data row 3
        ws.Cell(3, 1).Value = row.SerialNumber;
        ws.Cell(3, 2).Value = row.XAfterCell;
        ws.Cell(3, 3).Value = row.YAfterCell;
        ws.Cell(3, 4).Value = row.XOrigCell;
        ws.Cell(3, 5).Value = row.YOrigCell;
        ws.Cell(3, 7).Value = row.Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
        ws.Cell(3, 8).Value = row.AppVersion;

        ws.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }
}
