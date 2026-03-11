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

        app.MapGet("/prompt-generations", GetAllPromptGenerationsAsync)
            .WithTags("PromptTemplates")
            .Produces<IReadOnlyList<PromptGenerationResponse>>(StatusCodes.Status200OK);

        app.MapGet("/prompt-generations/{id:guid}", GetPromptGenerationByIdAsync)
            .WithTags("PromptTemplates")
            .Produces<PromptGenerationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPut("/prompt-generations/{id:guid}", UpdatePromptGenerationAsync)
            .WithTags("PromptTemplates")
            .Produces<PromptGenerationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/prompt-generations/{id:guid}", DeletePromptGenerationAsync)
            .WithTags("PromptTemplates")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/prompt-generations/{id:guid}/run", SchedulePromptGenerationRunAsync)
            .WithTags("PromptTemplates")
            .Produces<SchedulePromptGenerationRunResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPost("/prompt-generations/{id:guid}/outputs", CreatePromptGenerationOutputAsync)
            .WithTags("PromptTemplates")
            .Produces<PromptGenerationResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapPut("/prompt-generation-outputs/{id:guid}", UpdatePromptGenerationOutputAsync)
            .WithTags("PromptTemplates")
            .Produces<PromptGenerationOutputResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);

        app.MapDelete("/prompt-generation-outputs/{id:guid}", DeletePromptGenerationOutputAsync)
            .WithTags("PromptTemplates")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetPromptTemplatesAsync(
        string? purpose,
        string? provider,
        bool? active,
        string? search,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PromptTemplates.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(purpose))
        {
            var normalizedPurpose = purpose.Trim();
            query = query.Where(x => x.Category == normalizedPurpose);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            var normalizedProvider = provider.Trim();
            query = query.Where(x => x.Provider == normalizedProvider);
        }

        if (active.HasValue)
        {
            query = query.Where(x => x.IsActive == active.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.Name, term) ||
                EF.Functions.ILike(x.Slug, term) ||
                EF.Functions.ILike(x.Category, term) ||
                EF.Functions.ILike(x.Description ?? string.Empty, term));
        }

        var items = await query
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
            Category = request.Purpose.Trim(),
            Description = request.Description?.Trim(),
            Notes = request.Notes?.Trim(),
            TemplateBody = ResolveTemplateBody(request),
            SystemPrompt = NormalizeOptionalText(request.SystemPrompt),
            UserPromptTemplate = NormalizeOptionalText(request.UserPromptTemplate),
            InputMode = request.InputMode.Trim(),
            Provider = request.Provider.Trim(),
            DefaultModel = NormalizeOptionalText(request.Model),
            OutputMode = request.OutputMode.Trim(),
            SchemaKey = NormalizeOptionalText(request.SchemaKey),
            SettingsJson = NormalizeJsonPayload(request.SettingsJson),
            InputContractJson = NormalizeJsonPayload(request.InputContractJson),
            MetadataJson = NormalizeJsonPayload(request.MetadataJson),
            IsActive = request.IsActive,
            IsDefault = request.IsDefault,
            SortOrder = request.SortOrder,
            Version = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        if (item.IsDefault)
        {
            await ClearExistingDefaultTemplatesAsync(dbContext, item.Category, item.Provider, null, cancellationToken);
        }

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
        item.Category = request.Purpose.Trim();
        item.Description = request.Description?.Trim();
        item.Notes = request.Notes?.Trim();
        item.TemplateBody = ResolveTemplateBody(request);
        item.SystemPrompt = NormalizeOptionalText(request.SystemPrompt);
        item.UserPromptTemplate = NormalizeOptionalText(request.UserPromptTemplate);
        item.InputMode = request.InputMode.Trim();
        item.Provider = request.Provider.Trim();
        item.DefaultModel = NormalizeOptionalText(request.Model);
        item.OutputMode = request.OutputMode.Trim();
        item.SchemaKey = NormalizeOptionalText(request.SchemaKey);
        item.SettingsJson = NormalizeJsonPayload(request.SettingsJson);
        item.InputContractJson = NormalizeJsonPayload(request.InputContractJson);
        item.MetadataJson = NormalizeJsonPayload(request.MetadataJson);
        item.IsActive = request.IsActive;
        item.IsDefault = request.IsDefault;
        item.SortOrder = request.SortOrder;
        item.Version += 1;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (item.IsDefault)
        {
            await ClearExistingDefaultTemplatesAsync(dbContext, item.Category, item.Provider, item.Id, cancellationToken);
        }

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
        CancellationToken cancellationToken)
    {
        var template = await dbContext.PromptTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (template is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.InputJson))
        {
            return Results.BadRequest(new { message = "InputJson is required." });
        }

        if (!TryValidateJsonPayload(request.InputJson, out var inputError))
        {
            return Results.BadRequest(new { message = inputError });
        }

        var inputJson = NormalizeJsonPayload(request.InputJson);
        var inputLabel = NormalizeOptionalText(request.InputLabel);
        var now = DateTimeOffset.UtcNow;

        var item = new PromptGeneration
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            Purpose = template.Category,
            Provider = template.Provider,
            Status = PromptGenerationStatus.Draft,
            Model = string.IsNullOrWhiteSpace(request.Model) ? template.DefaultModel : request.Model.Trim(),
            InputLabel = inputLabel,
            InputJson = inputJson,
            ResolvedSystemPrompt = NormalizeOptionalText(request.ResolvedSystemPrompt) ?? ResolveSystemPrompt(template),
            ResolvedUserPrompt = NormalizeOptionalText(request.ResolvedUserPrompt) ?? ResolveUserPrompt(template, inputLabel ?? string.Empty, inputJson),
            TokenUsageJson = "{}",
            RunMetadataJson = "{}",
            TargetType = NormalizeOptionalText(request.TargetType),
            TargetId = NormalizeOptionalText(request.TargetId),
            CreatedAtUtc = now,
            StartedAtUtc = null,
            FinishedAtUtc = null,
            ErrorMessage = null
        };

        dbContext.PromptGenerations.Add(item);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Created($"/prompt-generations/{item.Id}", MapGeneration(item));
    }

    private static async Task<IResult> GetAllPromptGenerationsAsync(
        string? purpose,
        string? provider,
        string? status,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var query = dbContext.PromptGenerations
            .AsNoTracking()
            .Include(x => x.Outputs)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(purpose))
        {
            var normalizedPurpose = purpose.Trim();
            query = query.Where(x => x.Purpose == normalizedPurpose);
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            var normalizedProvider = provider.Trim();
            query = query.Where(x => x.Provider == normalizedProvider);
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PromptGenerationStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(x => x.Status == parsedStatus);
        }

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(items.Select(MapGeneration).ToList());
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

        if (string.IsNullOrWhiteSpace(request.OutputText) && string.IsNullOrWhiteSpace(request.OutputJson))
        {
            return Results.BadRequest(new { message = "Either OutputText or OutputJson is required." });
        }

        if (!TryValidateJsonPayload(request.OutputJson, out var outputJsonError))
        {
            return Results.BadRequest(new { message = outputJsonError });
        }

        if (!TryValidateJsonPayload(request.ProviderResponseJson, out var responseJsonError))
        {
            return Results.BadRequest(new { message = responseJsonError });
        }

        var output = new PromptGenerationOutput
        {
            Id = Guid.NewGuid(),
            PromptGenerationId = generation.Id,
            OutputType = string.IsNullOrWhiteSpace(request.OutputType) ? "text" : request.OutputType.Trim(),
            OutputLabel = NormalizeOptionalText(request.OutputLabel),
            OutputText = NormalizeOptionalText(request.OutputText),
            OutputJson = NormalizeOptionalJsonPayload(request.OutputJson),
            IsPrimary = request.IsPrimary,
            IsValid = request.IsValid,
            ValidationErrors = NormalizeOptionalText(request.ValidationErrors),
            ProviderResponseJson = NormalizeOptionalJsonPayload(request.ProviderResponseJson),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        if (output.IsPrimary)
        {
            foreach (var existingOutput in generation.Outputs)
            {
                existingOutput.IsPrimary = false;
            }
        }

        generation.Outputs.Add(output);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Results.Ok(MapGeneration(generation));
    }

    private static async Task<IResult> UpdatePromptGenerationAsync(
        Guid id,
        PromptGenerationRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var generation = await dbContext.PromptGenerations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (generation is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.InputJson))
        {
            return Results.BadRequest(new { message = "InputJson is required." });
        }

        if (!TryValidateJsonPayload(request.InputJson, out var inputError))
        {
            return Results.BadRequest(new { message = inputError });
        }

        var template = await dbContext.PromptTemplates.AsNoTracking().FirstOrDefaultAsync(x => x.Id == generation.TemplateId, cancellationToken);
        if (template is null)
        {
            return Results.BadRequest(new { message = "Prompt template not found." });
        }

        var inputJson = NormalizeJsonPayload(request.InputJson);
        var inputLabel = NormalizeOptionalText(request.InputLabel);
        generation.Model = string.IsNullOrWhiteSpace(request.Model) ? generation.Model : request.Model.Trim();
        generation.InputLabel = inputLabel;
        generation.InputJson = inputJson;
        generation.ResolvedSystemPrompt = NormalizeOptionalText(request.ResolvedSystemPrompt) ?? ResolveSystemPrompt(template);
        generation.ResolvedUserPrompt = NormalizeOptionalText(request.ResolvedUserPrompt) ?? ResolveUserPrompt(template, inputLabel ?? string.Empty, inputJson);
        generation.TargetType = NormalizeOptionalText(request.TargetType);
        generation.TargetId = NormalizeOptionalText(request.TargetId);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(MapGeneration(generation));
    }

    private static async Task<IResult> DeletePromptGenerationAsync(
        Guid id,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var generation = await dbContext.PromptGenerations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (generation is null)
        {
            return Results.NotFound();
        }

        dbContext.PromptGenerations.Remove(generation);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> SchedulePromptGenerationRunAsync(
        Guid id,
        YtProducerDbContext dbContext,
        IJobService jobService,
        CancellationToken cancellationToken)
    {
        var generation = await dbContext.PromptGenerations
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (generation is null)
        {
            return Results.NotFound();
        }

        generation.Status = PromptGenerationStatus.Queued;
        generation.ErrorMessage = null;
        generation.StartedAtUtc = null;
        generation.FinishedAtUtc = null;
        generation.LatencyMs = null;
        generation.TokenUsageJson = "{}";
        generation.RunMetadataJson = "{}";

        var payloadArguments = new CreatePromptGenerationJobArguments(generation.Id);
        var payloadJson = JsonSerializer.Serialize(new ScheduledCommandPayload(
            "run-prompt-generation",
            1,
            JsonSerializer.SerializeToElement(payloadArguments)));

        var result = await jobService.CreateAsync(new Job
        {
            Type = JobType.RunPromptGeneration,
            TargetType = "prompt_generation",
            TargetId = generation.Id,
            PayloadJson = payloadJson,
            MaxRetries = 3
        }, cancellationToken);

        generation.JobId = result.Job.Id;
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = new SchedulePromptGenerationRunResponse(
            generation.Id,
            result.Job.Id,
            result.Job.Type.ToString(),
            result.Job.Status.ToString());

        return result.CreatedNew
            ? Results.Created($"/jobs/{result.Job.Id}", response)
            : Results.Ok(response);
    }

    private static async Task<IResult> UpdatePromptGenerationOutputAsync(
        Guid id,
        PromptGenerationOutputRequest request,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var output = await dbContext.PromptGenerationOutputs.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (output is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.OutputText) && string.IsNullOrWhiteSpace(request.OutputJson))
        {
            return Results.BadRequest(new { message = "Either OutputText or OutputJson is required." });
        }

        if (!TryValidateJsonPayload(request.OutputJson, out var outputJsonError))
        {
            return Results.BadRequest(new { message = outputJsonError });
        }

        if (!TryValidateJsonPayload(request.ProviderResponseJson, out var responseJsonError))
        {
            return Results.BadRequest(new { message = responseJsonError });
        }

        if (request.IsPrimary)
        {
            var siblings = await dbContext.PromptGenerationOutputs
                .Where(x => x.PromptGenerationId == output.PromptGenerationId && x.Id != output.Id)
                .ToListAsync(cancellationToken);

            foreach (var sibling in siblings)
            {
                sibling.IsPrimary = false;
            }
        }

        output.OutputType = string.IsNullOrWhiteSpace(request.OutputType) ? output.OutputType : request.OutputType.Trim();
        output.OutputLabel = NormalizeOptionalText(request.OutputLabel);
        output.OutputText = NormalizeOptionalText(request.OutputText);
        output.OutputJson = NormalizeOptionalJsonPayload(request.OutputJson);
        output.IsPrimary = request.IsPrimary;
        output.IsValid = request.IsValid;
        output.ValidationErrors = NormalizeOptionalText(request.ValidationErrors);
        output.ProviderResponseJson = NormalizeOptionalJsonPayload(request.ProviderResponseJson);

        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.Ok(MapOutput(output));
    }

    private static async Task<IResult> DeletePromptGenerationOutputAsync(
        Guid id,
        YtProducerDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var output = await dbContext.PromptGenerationOutputs.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (output is null)
        {
            return Results.NotFound();
        }

        dbContext.PromptGenerationOutputs.Remove(output);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
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

        if (string.IsNullOrWhiteSpace(request.Purpose))
        {
            return "Purpose is required.";
        }

        if (string.IsNullOrWhiteSpace(request.SystemPrompt) && string.IsNullOrWhiteSpace(request.UserPromptTemplate))
        {
            return "At least one prompt body is required.";
        }

        if (string.IsNullOrWhiteSpace(request.InputMode))
        {
            return "Input mode is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Provider))
        {
            return "Provider is required.";
        }

        if (string.IsNullOrWhiteSpace(request.OutputMode))
        {
            return "Output mode is required.";
        }

        if (!TryValidateJsonPayload(request.SettingsJson, out var settingsError))
        {
            return settingsError;
        }

        if (!TryValidateJsonPayload(request.InputContractJson, out var inputError))
        {
            return inputError;
        }

        if (!TryValidateJsonPayload(request.MetadataJson, out var metadataError))
        {
            return metadataError;
        }

        return null;
    }

    private static async Task ClearExistingDefaultTemplatesAsync(
        YtProducerDbContext dbContext,
        string purpose,
        string provider,
        Guid? keepId,
        CancellationToken cancellationToken)
    {
        var existingDefaults = await dbContext.PromptTemplates
            .Where(x => x.Category == purpose && x.Provider == provider && x.IsDefault && (keepId == null || x.Id != keepId.Value))
            .ToListAsync(cancellationToken);

        foreach (var existingDefault in existingDefaults)
        {
            existingDefault.IsDefault = false;
            existingDefault.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private static string BuildResolvedPrompt(PromptTemplate template, string theme, string inputJson)
    {
        var systemPrompt = ResolveSystemPrompt(template);
        var renderedUserPrompt = ResolveUserPrompt(template, theme, inputJson);

        return $"[SYSTEM PROMPT]\n{systemPrompt}\n\n[USER PROMPT]\n{renderedUserPrompt}";
    }

    private static string ResolveSystemPrompt(PromptTemplate template)
    {
        return string.IsNullOrWhiteSpace(template.SystemPrompt)
            ? template.TemplateBody
            : template.SystemPrompt.Trim();
    }

    private static string ResolveUserPrompt(PromptTemplate template, string inputLabel, string inputJson)
    {
        var userPromptTemplate = string.IsNullOrWhiteSpace(template.UserPromptTemplate)
            ? "{\n  \"input_label\": \"{{input_label}}\",\n  \"input_json\": {{input_json}}\n}"
            : template.UserPromptTemplate.Trim();

        return userPromptTemplate
            .Replace("{{theme}}", inputLabel, StringComparison.Ordinal)
            .Replace("{{input_label}}", inputLabel, StringComparison.Ordinal)
            .Replace("{{input_json}}", inputJson, StringComparison.Ordinal);
    }

    private static bool TryValidateJsonPayload(string? rawJson, out string? error)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            error = null;
            return true;
        }

        try
        {
            using var _ = JsonDocument.Parse(rawJson);
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON payload: {ex.Message}";
            return false;
        }
    }

    private static string NormalizeJsonPayload(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(rawJson);
        return JsonSerializer.Serialize(document.RootElement);
    }

    private static string? NormalizeOptionalJsonPayload(string? rawJson)
    {
        return string.IsNullOrWhiteSpace(rawJson) ? null : NormalizeJsonPayload(rawJson);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ResolveTemplateBody(PromptTemplateRequest request)
    {
        return NormalizeOptionalText(request.SystemPrompt)
            ?? NormalizeOptionalText(request.UserPromptTemplate)
            ?? string.Empty;
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

    private static PromptTemplateResponse MapTemplate(PromptTemplate item)
    {
        return new PromptTemplateResponse(
            item.Id,
            item.Name,
            item.Slug,
            item.Category,
            item.Description,
            item.Notes,
            item.SystemPrompt ?? item.TemplateBody,
            item.UserPromptTemplate,
            item.InputMode,
            item.Provider,
            item.DefaultModel,
            item.OutputMode,
            item.SchemaKey,
            item.SettingsJson ?? "{}",
            item.InputContractJson ?? "{}",
            item.MetadataJson ?? "{}",
            item.IsActive,
            item.IsDefault,
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
            item.Purpose,
            item.Provider,
            item.Status.ToString(),
            item.Model,
            item.InputLabel,
            item.InputJson,
            item.ResolvedSystemPrompt,
            item.ResolvedUserPrompt,
            item.JobId,
            item.LatencyMs,
            item.TokenUsageJson,
            item.RunMetadataJson,
            item.TargetType,
            item.TargetId,
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
            item.OutputLabel,
            item.OutputText,
            item.OutputJson,
            item.IsPrimary,
            item.IsValid,
            item.ValidationErrors,
            item.ProviderResponseJson,
            item.CreatedAtUtc);
    }
}
