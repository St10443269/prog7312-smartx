using SmartX.Shared.Dto;
using SmartX.Shared.Enums;
using SmartX.Shared.Validation;

namespace SmartX.Tests;

/// <summary>The rules for registering a sensor and attaching files (checked by both the Sensors page and the API).</summary>
public class SensorInputTests
{
    private static RegisterSensorRequest Request(
        string mac = "24:6F:28:AA:11:01", string facility = "Facility A", string zone = "Zone 1",
        string? subZone = null, string nodeId = "Node 1", SensorCategory category = SensorCategory.Environmental) => new()
    {
        MacAddress = mac,
        FacilityName = facility,
        ZoneName = zone,
        SubZoneName = subZone,
        NodeId = nodeId,
        Category = category
    };

    // ------------------------------------------------------------- MAC address

    [Theory]
    [InlineData("24:6F:28:AA:11:01")]
    [InlineData("24-6f-28-aa-11-01")]
    [InlineData("  24:6F:28:AA:11:01  ")]
    [InlineData("00:00:00:00:00:00")]
    public void Good_mac_addresses_are_accepted(string mac)
    {
        Assert.True(SensorRegistrationValidator.IsValidMac(mac));
        Assert.Empty(SensorRegistrationValidator.Validate(Request(mac: mac)));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("24:6F:28:AA:11")]            // too short
    [InlineData("24:6F:28:AA:11:01:02")]      // too long
    [InlineData("24:6F:28:AA:11:ZZ")]         // not hex
    [InlineData("246F28AA1101")]              // no separators
    [InlineData("24:6F-28:AA-11:01")]         // mixed separators are fine in some tools, but not here
    [InlineData("24.6F.28.AA.11.01")]
    public void Bad_mac_addresses_are_rejected_with_a_message_for_the_mac_field(string mac)
    {
        var errors = SensorRegistrationValidator.Validate(Request(mac: mac));

        Assert.False(SensorRegistrationValidator.IsValidMac(mac));
        Assert.Contains("macAddress", errors.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_mac_says_it_is_required(string mac)
    {
        var errors = SensorRegistrationValidator.Validate(Request(mac: mac));

        Assert.Contains("required", errors["macAddress"]);
    }

    // ------------------------------------------------------------- other fields

    [Fact]
    public void Blank_required_fields_each_get_their_own_message()
    {
        var errors = SensorRegistrationValidator.Validate(Request(facility: " ", zone: "", nodeId: ""));

        Assert.Equal(["facilityName", "nodeId", "zoneName"], errors.Keys.Order());
        Assert.Contains("Facility is required", errors["facilityName"]);
    }

    [Fact]
    public void Sub_zone_is_optional_but_not_too_long()
    {
        Assert.Empty(SensorRegistrationValidator.Validate(Request(subZone: null)));
        Assert.Empty(SensorRegistrationValidator.Validate(Request(subZone: "")));
        Assert.Contains("subZoneName", SensorRegistrationValidator.Validate(Request(subZone: new string('x', 61))).Keys);
    }

    [Fact]
    public void Text_longer_than_the_limit_is_rejected()
    {
        var tooLong = new string('x', SensorRegistrationValidator.MaxTextLength + 1);

        var errors = SensorRegistrationValidator.Validate(Request(nodeId: tooLong, zone: tooLong));

        Assert.Contains("60 characters", errors["nodeId"]);
        Assert.Contains("zoneName", errors.Keys);
        Assert.Empty(SensorRegistrationValidator.Validate(Request(nodeId: new string('x', 60))));
    }

    [Fact]
    public void A_category_that_does_not_exist_is_rejected()
    {
        var errors = SensorRegistrationValidator.Validate(Request(category: (SensorCategory)99));

        Assert.Contains("category", errors.Keys);
    }

    [Fact]
    public void Every_wrong_field_is_reported_at_once()
    {
        var errors = SensorRegistrationValidator.Validate(Request(mac: "abc", facility: "", zone: "", nodeId: "", category: (SensorCategory)99));

        Assert.Equal(5, errors.Count);
    }

    // -------------------------------------------------------------- attachments

    [Theory]
    [InlineData("config.json")]
    [InlineData("photo.PNG")]
    [InlineData("hardware.log")]
    [InlineData("notes.txt")]
    [InlineData("scan.pdf")]
    [InlineData("deploy.yaml")]
    public void Allowed_file_types_pass_whatever_the_letter_case(string name)
    {
        Assert.Null(AttachmentRules.Check(name, 1024));
    }

    [Theory]
    [InlineData("program.exe")]
    [InlineData("script.ps1")]
    [InlineData("page.html")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    public void Other_file_types_are_refused_and_the_message_lists_what_is_allowed(string name)
    {
        var problem = AttachmentRules.Check(name, 1024);

        Assert.NotNull(problem);
        Assert.Contains(".json", problem);
    }

    [Fact]
    public void Files_over_the_size_limit_or_empty_files_are_refused()
    {
        Assert.Null(AttachmentRules.Check("big.log", AttachmentRules.MaxBytes));
        Assert.Contains("over the", AttachmentRules.Check("big.log", AttachmentRules.MaxBytes + 1));
        Assert.Contains("empty", AttachmentRules.Check("empty.log", 0));
        Assert.Contains("no name", AttachmentRules.Check("", 10));
    }

    [Theory]
    [InlineData(500, "500 B")]
    [InlineData(2048, "2 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    public void File_sizes_are_shown_in_a_readable_way(long bytes, string expected)
    {
        Assert.Equal(expected, AttachmentRules.FormatSize(bytes));
    }
}
