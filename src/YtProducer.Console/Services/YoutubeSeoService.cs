using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using YtProducer.Domain.Entities;

namespace YtProducer.Console.Services;

public sealed class YoutubeSeoService
{
    public YoutubeUploadMetadata BuildTrackUploadMetadata(
        Track track,
        Playlist playlist,
        double? durationSeconds,
        string? youtubePlaylistId)
    {
        return BuildUploadMetadata(track, playlist, YoutubeContentType.Track, durationSeconds, youtubePlaylistId);
    }

    public YoutubeUploadMetadata BuildLoopUploadMetadata(
        Track track,
        Playlist playlist,
        double? durationSeconds,
        string? youtubePlaylistId)
    {
        return BuildUploadMetadata(track, playlist, YoutubeContentType.Loop, durationSeconds, youtubePlaylistId);
    }

    private static YoutubeUploadMetadata BuildUploadMetadata(
        Track track,
        Playlist playlist,
        YoutubeContentType contentType,
        double? durationSeconds,
        string? youtubePlaylistId)
    {
        var trackMetadata = track.Metadata;
        var playlistMetadata = playlist.Metadata;
        var musicPrompt = TryGetMetadataString(trackMetadata, "musicGenerationPrompt")
            ?? TryGetMetadataString(playlistMetadata, "musicGenerationPrompt");
        var scenario = FirstNonEmpty(
            TryGetMetadataString(trackMetadata, "listeningScenario"),
            TryGetMetadataString(trackMetadata, "playlistCategory"),
            TryGetMetadataString(playlistMetadata, "playlistCategory"),
            playlist.Theme,
            "general listening");
        var exactGenre = FirstNonEmpty(
            TryGetMetadataString(trackMetadata, "exactGenre"),
            TryGetMetadataString(trackMetadata, "exactSubgenre"),
            TryExtractFieldFromPrompt(musicPrompt, "Subgenre"),
            TryExtractFieldFromPrompt(musicPrompt, "Genre"),
            track.Style,
            "Music");
        var primaryTopic = ResolvePrimaryTopic(playlist, scenario);
        var hookLabel = ResolveHookLabel(track, playlist);
        var secondaryKeyword = ResolveSecondaryKeyword(track, playlist, scenario, exactGenre, primaryTopic, hookLabel);
        var metadataDescription = TryGetMetadataString(trackMetadata, "youtubeDescription")
            ?? TryGetMetadataString(playlistMetadata, "youtubeDescription");
        var tags = BuildYoutubeTags(track, playlist, exactGenre, primaryTopic, scenario);
        var hashtags = BuildYoutubeHashtags(tags, contentType, exactGenre, primaryTopic, scenario);
        var durationMinutes = durationSeconds.HasValue && durationSeconds.Value > 0
            ? Math.Max(1, (int)Math.Ceiling(durationSeconds.Value / 60d))
            : 0;

        var context = new YoutubeSeoContext(
            contentType,
            track.Title,
            playlist.Title,
            playlist.Description,
            playlist.PlaylistStrategy,
            exactGenre,
            primaryTopic,
            secondaryKeyword,
            hookLabel,
            scenario,
            FirstNonEmpty(TryGetMetadataString(trackMetadata, "targetAudience"), "listeners"),
            FirstNonEmpty(
                track.Style,
                TryGetMetadataString(trackMetadata, "thumbnailEmotion"),
                TryGetMetadataString(trackMetadata, "energyCurve"),
                TryGetMetadataString(trackMetadata, "hookType"),
                "focused"),
            track.TempoBpm?.ToString() ?? TryExtractFieldFromPrompt(musicPrompt, "BPM"),
            track.Key ?? TryExtractFieldFromPrompt(musicPrompt, "Key"),
            track.EnergyLevel,
            metadataDescription,
            ResolveYoutubeSubscribeLink(),
            ResolveYoutubePlaylistLink(playlist, youtubePlaylistId),
            ResolveYoutubeBrandName(),
            durationSeconds,
            durationMinutes,
            ResolveYoutubeCategoryId(trackMetadata, playlistMetadata),
            ResolveYoutubeDefaultLanguage(trackMetadata, playlistMetadata),
            ResolveYoutubeDefaultAudioLanguage(trackMetadata, playlistMetadata),
            ResolveYoutubeMadeForKids(trackMetadata, playlistMetadata),
            tags,
            hashtags);

        var chapters = BuildYoutubeChapters(context);
        var title = BuildYoutubeTitle(context);
        var description = BuildYoutubeDescription(context, chapters);

        return new YoutubeUploadMetadata(
            title,
            description,
            context.Tags,
            context.Hashtags,
            chapters,
            context.CategoryId,
            context.DefaultLanguage,
            context.DefaultAudioLanguage,
            context.MadeForKids,
            context.DurationMinutes);
    }

    private static string BuildYoutubeTitle(YoutubeSeoContext context)
    {
        var bpmPrefix = string.IsNullOrWhiteSpace(context.Bpm) ? string.Empty : $"{context.Bpm.Trim()} BPM ";
        var exactGenre = NormalizeTitleToken(context.ExactGenre, "Music");
        var primaryTopic = NormalizeTitleToken(context.PrimaryTopic, "Music");
        var secondary = NormalizeTitleToken(context.SecondaryKeyword, null);
        var hook = NormalizeTitleToken(context.HookLabel, context.TrackTitle);
        if (!string.IsNullOrWhiteSpace(secondary) &&
            secondary.Equals(hook, StringComparison.OrdinalIgnoreCase))
        {
            secondary = string.Empty;
        }

        string title = context.ContentType switch
        {
            YoutubeContentType.Loop => $"{bpmPrefix}{exactGenre} {primaryTopic}{(string.IsNullOrWhiteSpace(secondary) ? string.Empty : $" ⚡ {secondary}")} | {Math.Max(1, context.DurationMinutes)} Min Loop",
            _ => $"{bpmPrefix}{exactGenre} {primaryTopic}{(string.IsNullOrWhiteSpace(secondary) ? string.Empty : $" ⚡ {secondary}")} | {hook}"
        };

        title = Regex.Replace(title, "\\s+", " ").Trim();
        return title.Length <= 100
            ? title
            : $"{title[..97].TrimEnd()}...";
    }

    private static string BuildYoutubeDescription(YoutubeSeoContext context, IReadOnlyList<string> chapters)
    {
        var lineOne = BuildPrimaryDescriptionLine(context);
        var lineTwo = BuildSecondaryDescriptionLine(context);
        var cta = BuildEngagementCta(context);
        var details = new List<string>
        {
            $"BPM: {NormalizeSentenceToken(context.Bpm, "n/a")}",
            $"Key: {NormalizeSentenceToken(context.MusicalKey, "n/a")}",
            $"Energy: {(context.EnergyLevel.HasValue ? $"{context.EnergyLevel.Value}/10" : "n/a")}",
            $"Genre: {context.ExactGenre}"
        };

        if (context.ContentType == YoutubeContentType.Loop)
        {
            details.Insert(0, $"Duration: {Math.Max(1, context.DurationMinutes)} min");
        }

        var lines = new List<string>
        {
            lineOne,
            lineTwo,
            string.Empty,
            cta,
            string.Empty,
            "Chapters:"
        };

        lines.AddRange(chapters);
        lines.Add(string.Empty);
        lines.Add("Track Details:");
        lines.AddRange(details);
        lines.Add(string.Empty);
        lines.Add($"Playlist: {context.PlaylistTitle}");

        if (!string.IsNullOrWhiteSpace(context.PlaylistLink))
        {
            lines.Add($"Listen in the playlist: {context.PlaylistLink}");
        }

        lines.Add($"Subscribe: {context.SubscribeLink}");

        if (context.Hashtags.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add(string.Join(" ", context.Hashtags));
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static IReadOnlyList<string> BuildYoutubeChapters(YoutubeSeoContext context)
    {
        var totalSeconds = context.DurationSeconds.HasValue && context.DurationSeconds.Value > 0
            ? Math.Max(60, (int)Math.Round(context.DurationSeconds.Value))
            : 180;

        var labels = context.ContentType == YoutubeContentType.Loop
            ? BuildLoopChapterLabels(context)
            : BuildTrackChapterLabels(context);

        var targetCount = totalSeconds < 170 ? 3 : 4;
        var positions = targetCount == 3
            ? new[] { 0, (int)Math.Round(totalSeconds * 0.38), (int)Math.Round(totalSeconds * 0.76) }
            : new[] { 0, (int)Math.Round(totalSeconds * 0.28), (int)Math.Round(totalSeconds * 0.62), (int)Math.Round(totalSeconds * 0.86) };

        var chapters = new List<string>();
        for (var index = 0; index < targetCount; index++)
        {
            chapters.Add($"{FormatChapterTimestamp(positions[index])} - {labels[index]}");
        }

        return chapters;
    }

    private static IReadOnlyList<string> BuildYoutubeTags(
        Track track,
        Playlist playlist,
        string exactGenre,
        string primaryTopic,
        string scenario)
    {
        var tags = TryGetMetadataStringArray(track.Metadata, "youtubeTags");
        if (tags.Count == 0)
        {
            tags = TryGetMetadataStringArray(playlist.Metadata, "youtubeTags");
        }

        var ordered = new List<string>();
        AddTagIfMissing(ordered, exactGenre);
        AddTagIfMissing(ordered, primaryTopic);
        AddTagIfMissing(ordered, scenario);
        AddTagIfMissing(ordered, playlist.Theme);

        foreach (var tag in tags)
        {
            AddTagIfMissing(ordered, tag);
        }

        return ordered.Take(8).ToList();
    }

    private static IReadOnlyList<string> BuildYoutubeHashtags(
        IReadOnlyList<string> tags,
        YoutubeContentType contentType,
        string exactGenre,
        string primaryTopic,
        string scenario)
    {
        var hashtags = new List<string>();

        AddHashtagIfMissing(hashtags, NormalizeHashtag(exactGenre));
        AddHashtagIfMissing(hashtags, NormalizeHashtag(primaryTopic, allowLong: false));
        AddHashtagIfMissing(hashtags, NormalizeHashtag(scenario, allowLong: false));

        foreach (var tag in tags)
        {
            AddHashtagIfMissing(hashtags, NormalizeHashtag(tag, allowLong: false));
        }

        if (contentType == YoutubeContentType.Loop)
        {
            AddHashtagIfMissing(hashtags, "#Loop");
        }

        return hashtags.Take(5).ToList();
    }

    private static void AddTagIfMissing(ICollection<string> tags, string? tag)
    {
        var normalized = NormalizeYoutubeTag(tag);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (tags.Any(existing => existing.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        tags.Add(normalized);
    }

    private static string? NormalizeYoutubeTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var normalized = Regex.Replace(tag.Trim(), "\\s+", " ");
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string ResolvePrimaryTopic(Playlist playlist, string scenario)
    {
        var combined = $"{scenario} {playlist.Theme} {playlist.Title}".ToLowerInvariant();
        if (ContainsAny(combined, "gym", "workout", "lifting", "cardio", "hiit", "warm-up", "warm up", "cooldown", "cool down", "running"))
        {
            return "Workout Music";
        }

        if (ContainsAny(combined, "focus", "study", "deep work"))
        {
            return "Focus Music";
        }

        if (ContainsAny(combined, "sleep", "rest", "night"))
        {
            return "Sleep Music";
        }

        if (ContainsAny(combined, "meditation", "healing", "ambient"))
        {
            return "Ambient Music";
        }

        if (ContainsAny(combined, "drive", "driving", "road trip"))
        {
            return "Driving Music";
        }

        return "Music";
    }

    private static string ResolveSecondaryKeyword(
        Track track,
        Playlist playlist,
        string scenario,
        string exactGenre,
        string primaryTopic,
        string hookLabel)
    {
        var candidates = new[]
        {
            TryGetMetadataString(track.Metadata, "youtubeSecondaryKeyword"),
            hookLabel,
            TryGetMetadataString(track.Metadata, "playlistCategory"),
            scenario,
            TryGetMetadataString(track.Metadata, "targetAudience"),
            track.Style,
            playlist.Theme
        };

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeKeywordCandidate(candidate, exactGenre, primaryTopic);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    private static string ResolveHookLabel(Track track, Playlist playlist)
    {
        var candidates = new[]
        {
            TryGetMetadataString(track.Metadata, "hookType"),
            track.Title,
            TryGetMetadataString(track.Metadata, "thumbnailTitle"),
            playlist.Title
        };

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeHookCandidate(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return "Momentum";
    }

    private static string BuildPrimaryDescriptionLine(YoutubeSeoContext context)
    {
        var bpmToken = string.IsNullOrWhiteSpace(context.Bpm) ? string.Empty : $"{context.Bpm.Trim()} BPM ";
        var scenario = NormalizeSentenceToken(context.Scenario, "listeners");
        var audience = NormalizeAudiencePhrase(context.TargetAudience);
        var genreTopic = $"{context.ExactGenre} {context.PrimaryTopic}".Trim();

        return context.ContentType == YoutubeContentType.Loop
            ? $"This {Math.Max(1, context.DurationMinutes)} minute {bpmToken}{genreTopic} loop is built for {scenario} and {audience}."
            : $"This {bpmToken}{genreTopic} track is built for {scenario} and {audience}.";
    }

    private static string BuildSecondaryDescriptionLine(YoutubeSeoContext context)
    {
        var metadataDescription = CleanMetadataDescription(context.MetadataDescription);
        if (!string.IsNullOrWhiteSpace(metadataDescription))
        {
            return metadataDescription;
        }

        var hook = NormalizeSentenceToken(context.HookLabel, "strong hooks").ToLowerInvariant();
        var vibe = ResolveDescriptionVibe(context);
        var scenarioPhrase = NormalizeSentenceToken(context.Scenario, "repeat listening");

        return context.ContentType == YoutubeContentType.Loop
            ? $"Expect {vibe} and {hook} to keep each repeat locked in for {scenarioPhrase}."
            : $"Expect {vibe} and {hook} to keep the track working for {scenarioPhrase}.";
    }

    private static string BuildEngagementCta(YoutubeSeoContext context)
    {
        var combined = $"{context.Scenario} {context.PrimaryTopic} {context.PlaylistTitle}".ToLowerInvariant();
        if (context.ContentType == YoutubeContentType.Loop)
        {
            return "How long are you running this loop today? Drop it in the comments! 👇";
        }

        if (ContainsAny(combined, "gym", "workout", "lifting", "cardio", "running"))
        {
            return "What are you training on this one? Drop it in the comments! 👇";
        }

        if (ContainsAny(combined, "focus", "study", "deep work"))
        {
            return "What are you working on with this track? Drop it in the comments! 👇";
        }

        return "Where are you listening from with this track? Drop it in the comments! 👇";
    }

    private static IReadOnlyList<string> BuildLoopChapterLabels(YoutubeSeoContext context)
    {
        return BuildDistinctChapterLabels(
            ("Intro & Focus", "Intro & Focus"),
            (BuildHookDrivenChapterLabel(context, "Main Drive"), "Main Drive"),
            (BuildScenarioDrivenChapterLabel(context, "Locked In"), "Locked In"),
            ("Final Surge", "Final Surge"));
    }

    private static IReadOnlyList<string> BuildTrackChapterLabels(YoutubeSeoContext context)
    {
        return BuildDistinctChapterLabels(
            ("Intro & Hook", "Intro & Hook"),
            ("Build & Momentum", "Build & Momentum"),
            (BuildScenarioDrivenChapterLabel(context, "Peak Section"), "Peak Section"),
            ("Final Stretch", "Final Stretch"));
    }

    private static string BuildHookDrivenChapterLabel(YoutubeSeoContext context, string fallback)
    {
        var hook = NormalizeKeywordCandidate(context.HookLabel, context.ExactGenre, context.PrimaryTopic);
        return string.IsNullOrWhiteSpace(hook) ? fallback : hook;
    }

    private static string BuildScenarioDrivenChapterLabel(YoutubeSeoContext context, string fallback)
    {
        var keyword = NormalizeKeywordCandidate(context.SecondaryKeyword, context.ExactGenre, context.PrimaryTopic);
        return string.IsNullOrWhiteSpace(keyword) ? fallback : keyword;
    }

    private static IReadOnlyList<string> BuildDistinctChapterLabels(params (string Preferred, string Fallback)[] labels)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(labels.Length);

        foreach (var (preferred, fallback) in labels)
        {
            var preferredLabel = NormalizeChapterLabel(preferred, fallback);
            if (seen.Add(preferredLabel))
            {
                result.Add(preferredLabel);
                continue;
            }

            var fallbackLabel = NormalizeChapterLabel(fallback, "Section");
            if (seen.Add(fallbackLabel))
            {
                result.Add(fallbackLabel);
                continue;
            }

            var genericLabel = $"Section {result.Count + 1}";
            result.Add(genericLabel);
            seen.Add(genericLabel);
        }

        return result;
    }

    private static string FormatChapterTimestamp(int totalSeconds)
    {
        var safe = Math.Max(0, totalSeconds);
        var minutes = safe / 60;
        var seconds = safe % 60;
        return $"{minutes}:{seconds:00}";
    }

    private static string NormalizeTitleToken(string? value, string? fallback)
    {
        var token = NormalizeSentenceToken(value, fallback);
        return string.IsNullOrWhiteSpace(token) ? string.Empty : ToHeadlineCase(token);
    }

    private static string NormalizeSentenceToken(string? value, string? fallback)
    {
        var source = string.IsNullOrWhiteSpace(value) ? fallback : value;
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        return Regex.Replace(source.Trim(), "\\s+", " ");
    }

    private static string NormalizeChapterLabel(string? value, string? fallback)
    {
        var token = NormalizeSentenceToken(value, fallback);
        return string.IsNullOrWhiteSpace(token) ? "Section" : ToHeadlineCase(token);
    }

    private static string NormalizeHookCandidate(string? value)
    {
        var candidate = NormalizeSentenceToken(value, null);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        candidate = StripHashtags(candidate);
        if (LooksLikeWeakKeyword(candidate))
        {
            return string.Empty;
        }

        return candidate;
    }

    private static string NormalizeKeywordCandidate(string? value, string exactGenre, string primaryTopic)
    {
        var candidate = NormalizeSentenceToken(value, null);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        candidate = StripHashtags(candidate);
        if (string.IsNullOrWhiteSpace(candidate) || LooksLikeWeakKeyword(candidate))
        {
            return string.Empty;
        }

        if (candidate.Contains(exactGenre, StringComparison.OrdinalIgnoreCase)
            || candidate.Contains(primaryTopic, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return candidate.Length > 28
            ? string.Empty
            : candidate;
    }

    private static bool LooksLikeWeakKeyword(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return normalized is
            "featured" or
            "listeners" or
            "music fans" or
            "general listening" or
            "focused" or
            "steady" or
            "driving" or
            "build energy" or
            "energy build" or
            "workout music" or
            "focus music" or
            "sleep music" or
            "ambient music" or
            "driving music" or
            "music";
    }

    private static string CleanMetadataDescription(string? value)
    {
        var candidate = NormalizeSentenceToken(value, null);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        candidate = StripHashtags(candidate);
        candidate = Regex.Replace(candidate, @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        if (candidate.StartsWith("Playlist Strategy:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (LooksPromotionalDescription(candidate))
        {
            return string.Empty;
        }

        return candidate;
    }

    private static string ResolveDescriptionVibe(YoutubeSeoContext context)
    {
        var source = NormalizeSentenceToken(context.Vibe, null);
        if (!string.IsNullOrWhiteSpace(source))
        {
            source = StripHashtags(source);
            source = source.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            source = Regex.Replace(source, @"\bfor\b.*$", string.Empty, RegexOptions.IgnoreCase).Trim(' ', ',', ';', ':', '-');

            foreach (var genreToken in SplitTokens(context.ExactGenre))
            {
                source = Regex.Replace(source, $@"\b{Regex.Escape(genreToken)}\b", string.Empty, RegexOptions.IgnoreCase).Trim();
            }

            source = Regex.Replace(source, "\\s+", " ").Trim(' ', ',', ';', ':', '-');
            if (!string.IsNullOrWhiteSpace(source) && source.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5)
            {
                return $"{source.ToLowerInvariant()} energy";
            }
        }

        if (context.EnergyLevel is >= 8)
        {
            return "high-impact energy";
        }

        if (context.EnergyLevel is >= 6)
        {
            return "driving energy";
        }

        return "focused energy";
    }

    private static bool LooksPromotionalDescription(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return ContainsAny(
            normalized,
            "subscribe",
            "drop it in the comments",
            "leave your excuses",
            "activate true",
            "activate beast mode",
            "specifically designed",
            "playlist strategy",
            "ignite your adrenaline",
            "blood pumping",
            "banger",
            "extreme session",
            "muscles ready",
            "demolish your limits",
            "crushing",
            "engineered for",
            "wrecking ball",
            "gym carnage",
            "brutal reps",
            "heaviest sets");
    }

    private static string NormalizeAudiencePhrase(string? value)
    {
        var normalized = NormalizeSentenceToken(value, "listeners");
        var lowered = normalized.ToLowerInvariant();

        return lowered switch
        {
            "hardcore lifters" => "high-intensity training sessions",
            "gym beginners" => "beginner-friendly workouts",
            _ => normalized
        };
    }

    private static string ResolveYoutubeBrandName()
    {
        var brandName = Environment.GetEnvironmentVariable("YT_PRODUCER_BRAND_NAME")?.Trim();
        return string.IsNullOrWhiteSpace(brandName) ? "AuruZ Music" : brandName;
    }

    private static string ResolveYoutubeSubscribeLink()
    {
        var subscribeLink = Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_SUBSCRIBE_LINK")?.Trim();
        return string.IsNullOrWhiteSpace(subscribeLink) ? "[Link]" : subscribeLink;
    }

    private static string ResolveYoutubePlaylistLink(Playlist playlist, string? youtubePlaylistId)
    {
        var resolvedPlaylistId = !string.IsNullOrWhiteSpace(youtubePlaylistId)
            ? youtubePlaylistId
            : playlist.YoutubePlaylistId;

        if (!string.IsNullOrWhiteSpace(resolvedPlaylistId))
        {
            return $"https://www.youtube.com/playlist?list={resolvedPlaylistId}";
        }

        var playlistLink = Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_PLAYLIST_LINK")?.Trim();
        return string.IsNullOrWhiteSpace(playlistLink) ? "[Link]" : playlistLink;
    }

    private static int? ResolveYoutubeCategoryId(string? trackMetadata, string? playlistMetadata)
    {
        return TryGetMetadataInt(trackMetadata, "youtubeCategoryId")
            ?? TryGetMetadataInt(playlistMetadata, "youtubeCategoryId")
            ?? ParseNullableInt(Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_CATEGORY_ID"))
            ?? 10;
    }

    private static string ResolveYoutubeDefaultLanguage(string? trackMetadata, string? playlistMetadata)
    {
        return FirstNonEmpty(
            TryGetMetadataString(trackMetadata, "defaultLanguage"),
            TryGetMetadataString(playlistMetadata, "defaultLanguage"),
            Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_DEFAULT_LANGUAGE"),
            "en");
    }

    private static string ResolveYoutubeDefaultAudioLanguage(string? trackMetadata, string? playlistMetadata)
    {
        return FirstNonEmpty(
            TryGetMetadataString(trackMetadata, "defaultAudioLanguage"),
            TryGetMetadataString(playlistMetadata, "defaultAudioLanguage"),
            Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_DEFAULT_AUDIO_LANGUAGE"),
            "en");
    }

    private static bool ResolveYoutubeMadeForKids(string? trackMetadata, string? playlistMetadata)
    {
        return TryGetMetadataBool(trackMetadata, "madeForKids")
            ?? TryGetMetadataBool(playlistMetadata, "madeForKids")
            ?? ParseNullableBool(Environment.GetEnvironmentVariable("YT_PRODUCER_YOUTUBE_MADE_FOR_KIDS"))
            ?? false;
    }

    private static int? ParseNullableInt(string? value)
    {
        return int.TryParse(value?.Trim(), out var parsed) ? parsed : null;
    }

    private static bool? ParseNullableBool(string? value)
    {
        return bool.TryParse(value?.Trim(), out var parsed) ? parsed : null;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        return needles.Any(needle => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryExtractFieldFromPrompt(string? prompt, string field)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return null;
        }

        var regex = new Regex($"{Regex.Escape(field)}\\s*:\\s*(?<value>[^\\.\\n\\r]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var match = regex.Match(prompt);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? TryGetMetadataString(string? metadata, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return TryGetStringProperty(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? TryGetMetadataInt(string? metadata, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return TryGetIntProperty(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool? TryGetMetadataBool(string? metadata, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            return TryGetBoolProperty(document.RootElement, propertyName);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<string> TryGetMetadataStringArray(string? metadata, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(metadata);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            foreach (var property in root.EnumerateObject())
            {
                if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    return Array.Empty<string>();
                }

                var list = new List<string>();
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            list.Add(value.Trim());
                        }
                    }
                }

                return list;
            }

            return Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? TryGetStringProperty(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = property.Value.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }

    private static int? TryGetIntProperty(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool? TryGetBoolProperty(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.True || property.Value.ValueKind == JsonValueKind.False)
            {
                return property.Value.GetBoolean();
            }
        }

        return null;
    }

    private static string? NormalizeHashtag(string? value, bool allowLong = true)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = Regex.Replace(value, @"[^a-zA-Z0-9]+", string.Empty);
        if (string.IsNullOrWhiteSpace(compact))
        {
            return null;
        }

        if (!allowLong && compact.Length > 18)
        {
            return null;
        }

        return $"#{compact}";
    }

    private static string StripHashtags(string value)
    {
        return Regex.Replace(value, @"#\w+", string.Empty).Trim();
    }

    private static IEnumerable<string> SplitTokens(string value)
    {
        return Regex.Split(value, @"[^a-zA-Z0-9]+")
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Select(token => token.Trim());
    }

    private static string ToHeadlineCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lower = value.ToLowerInvariant();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(lower);
    }

    private static void AddHashtagIfMissing(ICollection<string> hashtags, string? hashtag)
    {
        if (string.IsNullOrWhiteSpace(hashtag))
        {
            return;
        }

        if (hashtags.Any(existing => existing.Equals(hashtag, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        hashtags.Add(hashtag);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private enum YoutubeContentType
    {
        Track = 0,
        Loop = 1
    }

    private sealed record YoutubeSeoContext(
        YoutubeContentType ContentType,
        string TrackTitle,
        string PlaylistTitle,
        string? PlaylistDescription,
        string? PlaylistStrategy,
        string ExactGenre,
        string PrimaryTopic,
        string SecondaryKeyword,
        string HookLabel,
        string Scenario,
        string TargetAudience,
        string Vibe,
        string? Bpm,
        string? MusicalKey,
        int? EnergyLevel,
        string? MetadataDescription,
        string SubscribeLink,
        string PlaylistLink,
        string BrandName,
        double? DurationSeconds,
        int DurationMinutes,
        int? CategoryId,
        string DefaultLanguage,
        string DefaultAudioLanguage,
        bool MadeForKids,
        IReadOnlyList<string> Tags,
        IReadOnlyList<string> Hashtags);
}

public sealed record YoutubeUploadMetadata(
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Hashtags,
    IReadOnlyList<string> Chapters,
    int? CategoryId,
    string DefaultLanguage,
    string DefaultAudioLanguage,
    bool MadeForKids,
    int DurationMinutes);
