using SmartX.Api.RealTime;
using SmartX.Api.Services;
using SmartX.Shared.Dto;
using SmartX.Shared.Models;
using SmartX.Shared.Validation;

namespace SmartX.Api.Endpoints;

public static class SensorEndpoints
{
    public static RouteGroupBuilder MapSensorEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", (ISensorRepository repo) =>
            Results.Ok(repo.GetAll().Select(ToSummary)));

        group.MapGet("/{id:guid}", (Guid id, ISensorRepository repo) =>
            repo.GetById(id) is { } sensor ? Results.Ok(ToSummary(sensor)) : Results.NotFound());

        group.MapPost("/", (RegisterSensorRequest request, ISensorRepository repo, DeviceRegistry registry) =>
        {
            // the same rules the Sensors page checks before sending
            var problems = SensorRegistrationValidator.Validate(request);
            if (problems.Count > 0)
            {
                return Results.ValidationProblem(problems.ToDictionary(p => p.Key, p => new[] { p.Value }));
            }

            // store the MAC in one standard format (upper case, ":" separators)
            // so "24-6f-28-aa-11-01" and "24:6F:28:AA:11:01" are the same device
            var mac = DeviceRegistry.NormaliseMac(request.MacAddress);

            if (registry.Contains(mac) || repo.MacAddressExists(mac))
            {
                return Results.Conflict(new { message = $"A sensor with MAC address '{mac}' is already registered." });
            }

            var sensor = repo.Add(new SensorRegistration
            {
                MacAddress = mac,
                FacilityName = request.FacilityName.Trim(),
                ZoneName = request.ZoneName.Trim(),
                SubZoneName = string.IsNullOrWhiteSpace(request.SubZoneName) ? null : request.SubZoneName.Trim(),
                NodeId = request.NodeId.Trim(),
                Category = request.Category
            });

            // Part 2 bridge: a device registered on the Part 1 Sensors page
            // also joins the real-time device registry (keyed by MAC), so the
            // simulator, live dashboard and commands include it too.
            registry.TryRegister(new SimulatedDevice(
                sensor.Id, sensor.MacAddress, sensor.NodeId,
                DemoNetworkSeeder.KindForCategory(sensor.Category), sensor.ZoneName));

            return Results.Created($"/api/sensors/{sensor.Id}", ToSummary(sensor));
        });

        group.MapPost("/{id:guid}/attachments", async (Guid id, HttpRequest httpRequest, ISensorRepository repo, IWebHostEnvironment env) =>
        {
            if (repo.GetById(id) is null)
            {
                return Results.NotFound(new { message = "Sensor not found." });
            }

            if (!httpRequest.HasFormContentType)
            {
                return Results.BadRequest(new { message = "Expected multipart/form-data." });
            }

            var form = await httpRequest.ReadFormAsync();
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "No file was uploaded." });
            }

            // Extension allow-listing and a size cap follow the upload-validation
            // guidance in OWASP Foundation (n.d.). The rules are shared with the
            // Sensors page, which checks them before uploading.
            if (AttachmentRules.Check(file.FileName, file.Length) is { } problem)
            {
                return Results.BadRequest(new { message = problem });
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

            var uploadsRoot = Path.Combine(env.ContentRootPath, "App_Data", "uploads");
            Directory.CreateDirectory(uploadsRoot);

            // Never trust the client-supplied file name for the on-disk path -
            // store under a generated id and keep the original name only as
            // metadata, per OWASP Foundation (n.d.).
            var storedFileName = $"{Guid.NewGuid()}{extension}";
            var storagePath = Path.Combine(uploadsRoot, storedFileName);

            await using (var stream = File.Create(storagePath))
            {
                await file.CopyToAsync(stream);
            }

            var attachment = repo.AddAttachment(id, new SensorAttachment
            {
                SensorId = id,
                FileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType,
                SizeBytes = file.Length,
                StoragePath = storagePath
            });

            return attachment is null
                ? Results.NotFound()
                : Results.Created($"/api/sensors/{id}/attachments/{attachment.Id}", ToAttachmentDto(attachment));
        }).DisableAntiforgery();

        // the files attached to one sensor, newest first
        group.MapGet("/{id:guid}/attachments", (Guid id, ISensorRepository repo) =>
        {
            var sensor = repo.GetById(id);
            if (sensor is null)
            {
                return Results.NotFound(new { message = "Sensor not found." });
            }

            List<SensorAttachment> files;
            lock (sensor.Attachments)
            {
                files = sensor.Attachments.ToList();
            }

            return Results.Ok(files.OrderByDescending(a => a.UploadedAtUtc).Select(ToAttachmentDto));
        });

        group.MapGet("/{id:guid}/attachments/{attachmentId:guid}", (Guid id, Guid attachmentId, ISensorRepository repo) =>
        {
            var sensor = repo.GetById(id);
            var attachment = sensor?.Attachments.FirstOrDefault(a => a.Id == attachmentId);
            if (attachment is null || !File.Exists(attachment.StoragePath))
            {
                return Results.NotFound();
            }

            return Results.File(attachment.StoragePath, attachment.ContentType, attachment.FileName);
        });

        return group;
    }

    private static SensorSummaryDto ToSummary(SensorRegistration sensor) => new()
    {
        Id = sensor.Id,
        MacAddress = sensor.MacAddress,
        DeploymentPath = sensor.DeploymentPath,
        NodeId = sensor.NodeId,
        Category = sensor.Category,
        RegisteredAtUtc = sensor.RegisteredAtUtc,
        AttachmentCount = sensor.Attachments.Count
    };

    private static SensorAttachmentDto ToAttachmentDto(SensorAttachment attachment) => new()
    {
        Id = attachment.Id,
        FileName = attachment.FileName,
        ContentType = attachment.ContentType,
        SizeBytes = attachment.SizeBytes,
        UploadedAtUtc = attachment.UploadedAtUtc
    };
}
