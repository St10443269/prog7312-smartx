using System.Text.RegularExpressions;
using SmartX.Shared.Dto;

namespace SmartX.Shared.Validation;

/// <summary>
/// The rules for registering a sensor. The Sensors page checks these before it
/// sends anything, and the API checks the same rules again, so a bad request is
/// stopped in both places and both show the same messages.
/// </summary>
public static partial class SensorRegistrationValidator
{
    public const int MaxTextLength = 60;

    // six pairs of hex digits, all separated by ":" or all by "-", e.g. 24:6F:28:AA:11:01
    // (the \2 makes every separator the same as the first one)
    [GeneratedRegex(@"^[0-9A-Fa-f]{2}([:-])(?:[0-9A-Fa-f]{2}\1){4}[0-9A-Fa-f]{2}$")]
    private static partial Regex MacPattern();

    public static bool IsValidMac(string? macAddress) =>
        !string.IsNullOrWhiteSpace(macAddress) && MacPattern().IsMatch(macAddress.Trim());

    /// <summary>
    /// Returns a message for each field that is wrong (empty if everything is
    /// fine). The keys match the field names the API uses in its error
    /// replies: macAddress, facilityName, zoneName, subZoneName, nodeId, category.
    /// </summary>
    public static Dictionary<string, string> Validate(RegisterSensorRequest request)
    {
        var errors = new Dictionary<string, string>();

        if (string.IsNullOrWhiteSpace(request.MacAddress))
        {
            errors["macAddress"] = "MAC address is required.";
        }
        else if (!IsValidMac(request.MacAddress))
        {
            errors["macAddress"] = "Use six pairs of hex digits, like 24:6F:28:AA:11:01.";
        }

        CheckRequiredText(errors, "facilityName", "Facility", request.FacilityName);
        CheckRequiredText(errors, "zoneName", "Zone", request.ZoneName);
        CheckRequiredText(errors, "nodeId", "Node ID", request.NodeId);

        if (!string.IsNullOrWhiteSpace(request.SubZoneName) && request.SubZoneName.Trim().Length > MaxTextLength)
        {
            errors["subZoneName"] = $"Sub-Zone must be {MaxTextLength} characters or fewer.";
        }

        if (!Enum.IsDefined(request.Category))
        {
            errors["category"] = "Choose a category from the list.";
        }

        return errors;
    }

    private static void CheckRequiredText(Dictionary<string, string> errors, string key, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[key] = $"{label} is required.";
        }
        else if (value.Trim().Length > MaxTextLength)
        {
            errors[key] = $"{label} must be {MaxTextLength} characters or fewer.";
        }
    }
}
