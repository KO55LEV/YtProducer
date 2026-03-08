using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using YtProducer.Contracts.Prompts;
using YtProducer.Domain.Entities;
using YtProducer.Domain.Enums;
using YtProducer.Infrastructure.Persistence;

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
            Status = PromptGenerationStatus.Draft,
            Model = string.IsNullOrWhiteSpace(request.Model) ? template.DefaultModel : request.Model.Trim(),
            InputJson = inputJson,
            ResolvedPrompt = resolvedPrompt,
            CreatedAtUtc = now
        };

        dbContext.PromptGenerations.Add(item);
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
        var isValidJson = false;
        string? formattedJson = null;
        string? validationErrors = null;

        try
        {
            using var document = JsonDocument.Parse(rawText);
            formattedJson = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            isValidJson = true;
        }
        catch (JsonException ex)
        {
            validationErrors = ex.Message;
        }

        var output = new PromptGenerationOutput
        {
            Id = Guid.NewGuid(),
            PromptGenerationId = generation.Id,
            OutputType = string.IsNullOrWhiteSpace(request.OutputType) ? "album_json" : request.OutputType.Trim(),
            RawText = rawText,
            FormattedJson = formattedJson,
            IsValidJson = isValidJson,
            ValidationErrors = validationErrors,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        generation.Outputs.Add(output);
        generation.Status = isValidJson ? PromptGenerationStatus.Completed : PromptGenerationStatus.Failed;
        generation.FinishedAtUtc = DateTimeOffset.UtcNow;
        generation.ErrorMessage = validationErrors;

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
