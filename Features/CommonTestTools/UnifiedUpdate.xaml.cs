using Base.Core;
using Base.Pages;
using Base.Services.APIService;
using System.IO;

namespace CommonTestTools;

/// <summary>
/// Interaction logic for UnifiedUpdate.xaml
/// </summary>
[PageInfo("Unified Update", Glyph = "", Path = ["ATE"])]
public partial class UnifiedUpdate : PageBase
{
    [Persist, Config("Paths", Type = ConfigType.Folder)]
    private readonly List<DeviceMasterUpdateFolder> Paths = [];

    public UnifiedUpdate()
    {
        InitializeComponent();
    }

    [POST("UpdateFromBin")]
    public ApiResponse UpdateFromBin(UpdateRequest request)
    {
        ApiResponse response = new();

        try
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.deviceName) ||
                string.IsNullOrWhiteSpace(request.sourceBinLocation) ||
                string.IsNullOrWhiteSpace(request.destinationBinFileName) ||
                string.IsNullOrWhiteSpace(request.updateBatch))
            {
                response.Status = 400;
                return response;
            }

            DeviceMasterUpdateFolder? device = Paths.FirstOrDefault(x =>
                string.Equals(
                    x.deviceName,
                    request.deviceName,
                    StringComparison.OrdinalIgnoreCase));

            if (device == null)
            {
                response.Status = 404;
                return response;
            }

            if (string.IsNullOrWhiteSpace(device.folderLocation))
            {
                response.Status = 500;
                return response;
            }

            if (!File.Exists(request.sourceBinLocation))
            {
                response.Status = 404;
                return response;
            }

            CopyBin(
                request.sourceBinLocation,
                device.folderLocation,
                request.destinationBinFileName);

            int exitCode = ExecuteUpdateBatch(
                device.folderLocation,
                request.updateBatch);

            response.Status = exitCode == 0 ? 200 : 500;
            response.Data = exitCode == 0
                ? "BIN copied and update batch completed successfully."
                : $"Update batch failed with exit code {exitCode}.";

            return response;
        }
        catch (FileNotFoundException ex)
        {
            response.Status = 404;
            response.Data = ex.Message;
            return response;
        }
        catch (Exception ex)
        {
            response.Status = 500;
            response.Data = ex.Message;
            return response;
        }
    }

    private static string CopyBin(
    string sourceBinLocation,
    string folderLocation,
    string destinationBinFileName)
    {
        Directory.CreateDirectory(folderLocation);

        string destinationPath = Path.Combine(
            folderLocation,
            Path.GetFileName(destinationBinFileName));

        File.Copy(sourceBinLocation, destinationPath, overwrite: true);

        return destinationPath;
    }

    private static int ExecuteUpdateBatch(
        string folderLocation,
        string updateBatch)
    {
        string batchPath = Path.Combine(
            folderLocation,
            Path.GetFileName(updateBatch));

        return !File.Exists(batchPath)
        ? throw new FileNotFoundException("Batch file not found.", batchPath)
        : FirmwarePage.ExecuteBatch(batchPath);
    }

    public class DeviceMasterUpdateFolder
    {
        [Config]
        public string deviceName;

        [Config("path", Type = ConfigType.Folder)]
        public string folderLocation;
    }

    public class UpdateRequest
    {
        public string sourceBinLocation;
        public string deviceName;
        public string destinationBinFileName;
        public string updateBatch;
    }
}