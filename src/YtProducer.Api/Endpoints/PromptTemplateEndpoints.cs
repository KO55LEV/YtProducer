using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YtProducer.Contracts.Jobs;
using YtProducer.Contracts.Prompts;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Persistence;
using YtProducer.Infrastructure.Services;

namespace YtProducer.Api.Endpoints;

public static class PromptTemplateEndpoints
{
    public static IEndpointRouteBuilder MapPromptTemplateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/prompt-templates").WithTags("PromptTemplates");

        group.MapGet("/", GetPromptTemplatesAsync)
            .Produces<IReadOnlyList<PromptTemplateResponse>>(StatusCodes.Status200OK);

        group.MapGet("/{id:guid}", GetPromptTemplateByIdAsync)
            .Produces<PromptTemplateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", CreatePromptTemplateAsync)
            .Produces<PromptTemplateResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);

        group.MapPut("/{id:guid}", UpdatePromptTemplateAsync)
            .Produces<PromptTemplateResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/generations", GetPromptGenerationsAsync)
            .Produces<IReadOnlyList<PromptGenerationResponse>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/generations", CreatePromptGenerationAsync)
            .Produces<PromptGenerationResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapGet("/prompt-generations/{id:guid}", GetPromptGenerationByIdAsync)
            .WithTags("PromptTemplates")
            .Produces<PromptGenerationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/prompt-generations/{id:guid}/outputs", CreatePromptGenerationOutputAsync)
            .WithTags("PromptTemplates")
            .Produces<PromptGenerationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetPromptTemplatesAsync(
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.PromptTemplates
            .AsNoTracking()
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return Results.Ok(items.Select(MapTemplate).ToList());
    }

    private static async Task<IResult> GetPromptTemplateByIdAsync(
        Guid id,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.PromptTemplates
            .AsNoTracking()
            .Where(x => x.Id == id)
            .FirstOrDefaultAsync(cancellationToken);

        return item is null ? Results.NotFound() : Results.Ok(MapTemplate(item));
    }

    private static async Task<IResult> CreatePromptTemplateAsync(
        PromptTemplateRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validation = ValidateTemplateRequest(request);
        if (validation is not null)
        {
            return Results.BadRequest(new { message = validation });
        }

        var slug = request.Slug.Trim();
        var exists = await dbContext.PromptTemplates
            .AsNoTracking()
            .AnyAsync(x => x.Slug == slug, cancellationToken);
        if (exists)
        {
            return Results.BadRequest(new { message = $"Template slug already exists: {slug}" });
        }

        var now = DateTimeOffset.UtcNow;
        var item = new PromptTemplate
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Slug = slug,
            Category = request.Category.Trim(),
            Description = request.Description?.Trim(),
            TemplateBody = request.TemplateBody,
            InputMode = request.InputMode.Trim(),
            DefaultModel = string.IsNullOrWhiteSpace(request.DefaultModel) ? null : request.DefaultModel.Trim(),
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.PromptTemplates.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Created($"/prompt-templates/{item.Id}", MapTemplate(item));
    }

    private static async Task<IResult> UpdatePromptTemplateAsync(
        Guid id,
        PromptTemplateRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var validation = ValidateTemplateRequest(request);
        if (validation is not null)
        {
            return Results.BadRequest(new { message = validation });
        }

        var item = await dbContext.PromptTemplates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        var slug = request.Slug.Trim();
        var slugExists = await dbContext.PromptTemplates
            .AsNoTracking()
            .AnyAsync(x => x.Id != id && x.Slug == slug, cancellationToken);
        if (slugExists)
        {
            return Results.BadRequest(new { message = $"Template slug already exists: {slug}" });
        }

        item.Name = request.Name.Trim();
        item.Slug = slug;
        item.Category = request.Category.Trim();
        item.Description = request.Description?.Trim();
        item.TemplateBody = request.TemplateBody;
        item.InputMode = request.InputMode.Trim();
        item.DefaultModel = string.IsNullOrWhiteSpace(request.DefaultModel) ? null : request.DefaultModel.Trim();
        item.IsActive = request.IsActive;
        item.SortOrder = request.SortOrder;
        item.Version += 1;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(MapTemplate(item));
    }

    private static async Task<IResult> GetPromptGenerationsAsync(
        Guid id,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var templateExists = await dbContext.PromptTemplates
            .AsNoTracking()
            .AnyAsync(x => x.Id == id, cancellationToken);
        if (!templateExists)
        {
            return Results.NotFound();
        }

        var items = await dbContext.PromptGenerations
            .AsNoTracking()
            .Include(x => x.Outputs)
            .Where(x => x.TemplateId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(items.Select(MapGeneration).ToList());
    }

    private static async Task<IResult> CreatePromptGenerationAsync(
        Guid id,
        PromptGenerationRequest request,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var template = await dbContext.PromptTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Theme))
        {
            return Results.BadRequest(new { message = "Theme is required." });
        }

        var theme = request.Theme.Trim();
        var inputJson = JsonSerializer.Serialize(new { theme });
        var resolvedPrompt = $"[SYSTEM PROMPT]\n{template.TemplateBody}\n\n[USER INPUT JSON]\n{inputJson}";
        var now = DateTimeOffset.UtcNow;

        var item = new PromptGeneration
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            Theme = theme,
            Status = PromptGenerationStatus.Queued,
            Model = string.IsNullOrWhiteSpace(request.Model) ? template.DefaultModel : request.Model.Trim(),
            InputJson = inputJson,
            ResolvedPrompt = resolvedPrompt,
            CreatedAtUtc = now
        };

        dbContext.PromptGenerations.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        var payloadArguments = new CreatePromptGenerationJobArguments(item.Id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "generate-album-json",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.GenerateAlbumJson,
            TargetType = "prompt_generation",
            TargetId = item.Id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        item.JobId = result.Job.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/prompt-generations/{item.Id}", MapGeneration(item));
    }

    private static async Task<IResult> GetPromptGenerationByIdAsync(
        Guid id,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.PromptGenerations
            .AsNoTracking()
            .Include(x => x.Outputs.OrderByDescending(o => o.CreatedAtUtc))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        return item is null ? Results.NotFound() : Results.Ok(MapGeneration(item));
    }

    private static async Task<IResult> CreatePromptGenerationOutputAsync(
        Guid id,
        PromptGenerationOutputRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var generation = await dbContext.PromptGenerations
            .Include(x => x.Outputs)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (generation is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.RawText))
        {
            return Results.BadRequest(new { message = "RawText is required." });
        }

        var rawText = request.RawText.Trim();
        var normalizedRawText = NormalizeReturnedJson(rawText);
        var isValidJson = TryValidateAlbumJson(normalizedRawText, out var formattedJson, out var validationErrors);

        if (!isValidJson)
        {
            return Results.BadRequest(new { message = validationErrors ?? "Album JSON schema is invalid." });
        }

        var output = new PromptGenerationOutput
        {
            Id = Guid.NewGuid(),
            PromptGenerationId = generation.Id,
            OutputType = string.IsNullOrWhiteSpace(request.OutputType) ? "album_json" : request.OutputType.Trim(),
            RawText = rawText,
            FormattedJson = formattedJson,
            IsValidJson = true,
            ValidationErrors = null,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        generation.Outputs.Add(output);
        generation.Status = PromptGenerationStatus.Completed;
        generation.FinishedAtUtc = DateTimeOffset.UtcNow;
        generation.ErrorMessage = null;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(MapGeneration(generation));
    }

    private static string? ValidateTemplateRequest(PromptTemplateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Slug))
        {
            return "Slug is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            return "Category is required.";
        }

        if (string.IsNullOrWhiteSpace(request.TemplateBody))
        {
            return "Template body is required.";
        }

        if (string.IsNullOrWhiteSpace(request.InputMode))
        {
            return "Input mode is required.";
        }

        return null;
    }

    private static string NormalizeReturnedJson(string rawText)
    {
        var trimmed = rawText.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var normalized = trimmed.Trim('`').Trim();
        if (normalized.StartsWith("json", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[4..].Trim();
        }

        return normalized;
    }

    private static bool TryValidateAlbumJson(string rawText, out string? formattedJson, out string? validationErrors)
    {
        try
        {
            using var document = JsonDocument.Parse(rawText);
            formattedJson = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            var errors = ValidateAlbumRoot(document.RootElement);
            validationErrors = errors.Count == 0 ? null : string.Join(" ", errors);
            return errors.Count == 0;
        }
        catch (JsonException ex)
        {
            formattedJson = null;
            validationErrors = ex.Message;
            return false;
        }
    }

    private static List<string> ValidateAlbumRoot(JsonElement root)
    {
        var errors = new List<string>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            errors.Add("Root must be a JSON object.");
            return errors;
        }

        ValidateRequiredString(root, "theme", errors);
        ValidateRequiredString(root, "playlist_title", errors);
        ValidateRequiredString(root, "playlist_description", errors);
        ValidateRequiredString(root, "playlist_strategy", errors);
        ValidateRequiredString(root, "target_platform", errors);

        if (!root.TryGetProperty("tracks", out var tracksElement) || tracksElement.ValueKind != JsonValueKind.Array)
        {
            errors.Add("tracks must be an array.");
            return errors;
        }

        var trackItems = tracksElement.EnumerateArray().ToList();
        if (trackItems.Count == 0)
        {
            errors.Add("tracks must contain at least one item.");
            return errors;
        }

        for (var index = 0; index < trackItems.Count; index++)
        {
            ValidateTrack(trackItems[index], index, errors);
        }

        return errors;
    }

    private static void ValidateTrack(JsonElement track, int index, List<string> errors)
    {
        var prefix = $"tracks[{index}]";
        if (track.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{prefix} must be an object.");
            return;
        }

        ValidatePositiveInt(track, "playlist_position", prefix, errors);
        ValidateRequiredString(track, "title", errors, prefix);
        ValidateRequiredString(track, "youtube_title", errors, prefix);
        ValidateRequiredString(track, "style_summary", errors, prefix);
        ValidatePositiveInt(track, "duration_seconds", prefix, errors);
        ValidatePositiveInt(track, "tempo_bpm", prefix, errors);
        ValidateRequiredString(track, "key", errors, prefix);
        ValidatePositiveInt(track, "energy_level", prefix, errors);
        ValidateRequiredString(track, "music_generation_prompt", errors, prefix);
        ValidateRequiredString(track, "image_prompt", errors, prefix);
        ValidateRequiredString(track, "youtube_description", errors, prefix);
        ValidateStringArray(track, "youtube_tags", prefix, errors);
    }

    private static void ValidateRequiredString(JsonElement element, string propertyName, List<string> errors, string? prefix = null)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            errors.Add($"{FormatPath(prefix, propertyName)} is required.");
        }
    }

    private static void ValidatePositiveInt(JsonElement element, string propertyName, string prefix, List<string> errors)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var number) ||
            number <= 0)
        {
            errors.Add($"{FormatPath(prefix, propertyName)} must be a positive integer.");
        }
    }

    private static void ValidateStringArray(JsonElement element, string propertyName, string prefix, List<string> errors)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{FormatPath(prefix, propertyName)} must be an array of strings.");
            return;
        }

        var items = value.EnumerateArray().ToList();
        if (items.Count == 0 || items.Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
        {
            errors.Add($"{FormatPath(prefix, propertyName)} must contain at least one non-empty string.");
        }
    }

    private static string FormatPath(string? prefix, string propertyName)
    {
        return string.IsNullOrWhiteSpace(prefix) ? propertyName : $"{prefix}.{propertyName}";
    }

    private static PromptTemplateResponse MapTemplate(PromptTemplate item)
    {
        return new PromptTemplateResponse(
            item.Id,
            item.Name,
            item.Slug,
            item.Category,
            item.Description,
            item.TemplateBody,
            item.InputMode,
            item.DefaultModel,
            item.IsActive,
            item.SortOrder,
            item.Version,
            item.CreatedAtUtc,
            item.UpdatedAtUtc);
    }

    private static PromptGenerationResponse MapGeneration(PromptGeneration item)
    {
        return new PromptGenerationResponse(
            item.Id,
            item.TemplateId,
            item.Theme,
            item.Status.ToString(),
            item.Model,
            item.InputJson,
            item.ResolvedPrompt,
            item.JobId,
            item.CreatedAtUtc,
            item.StartedAtUtc,
            item.FinishedAtUtc,
            item.ErrorMessage,
            item.Outputs
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(MapOutput)
                .ToList());
    }

    private static PromptGenerationOutputResponse MapOutput(PromptGenerationOutput item)
    {
        return new PromptGenerationOutputResponse(
            item.Id,
            item.PromptGenerationId,
            item.OutputType,
            item.RawText,
            item.FormattedJson,
            item.IsValidJson,
            item.ValidationErrors,
            item.CreatedAtUtc);
    }
}
